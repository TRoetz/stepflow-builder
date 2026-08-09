using System.Xml;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Text.RegularExpressions;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // BPMN TO STEP-FLOW CONVERTER
    // Parses Camunda 8 (Zeebe) BPMN XML → Amazon States Language JSON
    // Maps BPMN service tasks to http://, rule://, ai://, flow:// resources
    // ═══════════════════════════════════════════════════════════════════════════════

    public class BpmnConverter
    {
        private readonly ILogger<BpmnConverter> _logger;

        public BpmnConverter(ILogger<BpmnConverter> logger)
        {
            _logger = logger;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public StateMachineDefinition ConvertXml(string bpmnXml)
        {
            var definitions = DeserializeBpmn(bpmnXml);
            if (definitions?.Processes == null || definitions.Processes.Count == 0)
                throw new ArgumentException("BPMN file contains no processes");

            // Enrich with zeebe/camunda properties from raw XML
            EnrichWithXmlAttributes(bpmnXml, definitions.Processes[0]);

            // Convert first (or only) process
            return ConvertProcess(definitions.Processes[0]);
        }

        public StateMachineDefinition ConvertProcess(BpmnProcess process)
        {
            var definition = new StateMachineDefinition
            {
                Comment = $"Converted from BPMN process '{process.Id}'{(!string.IsNullOrEmpty(process.Name) ? $" ({process.Name})" : "")}",
                StartAt = "",
                States = new Dictionary<string, StateDefinition>()
            };

            // Build a lookup of all flow nodes by id
            var allNodes = BuildFlowNodeMap(process);
            var gatewayNodes = new HashSet<string>();
            var endEventIds = new HashSet<string>(process.EndEvents?.Select(e => e.Id) ?? Array.Empty<string>());

            // Mark gateways
            foreach (var eg in process.ExclusiveGateways ?? new()) gatewayNodes.Add(eg.Id);
            foreach (var pg in process.ParallelGateways ?? new()) gatewayNodes.Add(pg.Id);
            foreach (var ig in process.InclusiveGateways ?? new()) gatewayNodes.Add(ig.Id);

            // Find start event(s)
            var startEvents = process.StartEvents ?? new();
            if (startEvents.Count == 0)
            {
                _logger.LogWarning("No start event found in process '{ProcessId}', using first task", process.Id);
            }

            // ── Convert Start Events ─────────────────────────────────────────
            foreach (var start in startEvents)
            {
                var nextState = GetSingleOutgoingTarget(process, start.Id);
                definition.States[start.Id] = new StateDefinition
                {
                    Type = StateType.Pass,
                    Comment = $"Start Event{(!string.IsNullOrEmpty(start.Name) ? $": {start.Name}" : "")}",
                    Next = nextState
                };
                definition.StartAt = start.Id;
            }

            // ── Convert Service Tasks ────────────────────────────────────────
            foreach (var st in process.ServiceTasks ?? new())
            {
                var resource = ResolveResource(st, process);
                var nextState = GetSingleOutgoingTarget(process, st.Id);
                var isEnd = endEventIds.Contains(st.Id); // should not happen but safety
                var hasNext = !string.IsNullOrEmpty(nextState);

                var state = new StateDefinition
                {
                    Type = StateType.Task,
                    Comment = $"Service Task: {st.Name ?? st.Id}",
                    Resource = resource,
                    Next = nextState,
                    End = !hasNext && !isEnd ? true : (bool?)null
                };

                // Apply zeebe io-mapping as Parameters / ResultPath
                ApplyZeebeIoMapping(state, st);

                // Multi-instance → wrap in Map
                if (st.MultiInstance != null && !string.IsNullOrEmpty(st.MultiInstance.Collection))
                {
                    var mapState = WrapInMapState(state, st.MultiInstance);
                    definition.States[st.Id] = mapState;
                }
                else
                {
                    definition.States[st.Id] = state;
                }
            }

            // ── Convert Regular Tasks ────────────────────────────────────────
            foreach (var task in process.Tasks ?? new())
            {
                var nextState = GetSingleOutgoingTarget(process, task.Id);
                definition.States[task.Id] = new StateDefinition
                {
                    Type = StateType.Pass,
                    Comment = $"Task: {task.Name ?? task.Id}",
                    Result = new JObject { ["taskExecuted"] = task.Id },
                    Next = nextState,
                    End = string.IsNullOrEmpty(nextState) ? true : (bool?)null
                };
            }

            // ── Convert Script Tasks ────────────────────────────────────────
            foreach (var st in process.ScriptTasks ?? new())
            {
                var nextState = GetSingleOutgoingTarget(process, st.Id);
                definition.States[st.Id] = new StateDefinition
                {
                    Type = StateType.Pass,
                    Comment = $"Script Task: {st.Name ?? st.Id} ({st.ScriptFormat})",
                    Result = new JObject { ["script"] = st.Script ?? "", ["format"] = st.ScriptFormat ?? "unknown" },
                    Next = nextState,
                    End = string.IsNullOrEmpty(nextState) ? true : (bool?)null
                };
            }

            // ── Convert Receive Tasks ───────────────────────────────────────
            foreach (var rt in process.ReceiveTasks ?? new())
            {
                var nextState = GetSingleOutgoingTarget(process, rt.Id);
                definition.States[rt.Id] = new StateDefinition
                {
                    Type = StateType.Wait,
                    Comment = $"Receive Task: {rt.Name ?? rt.Id}",
                    Seconds = 0, // placeholder; in real use, callback-driven
                    Next = nextState,
                    End = string.IsNullOrEmpty(nextState) ? true : (bool?)null
                };
            }

            // ── Convert Send Tasks ──────────────────────────────────────────
            foreach (var st in process.SendTasks ?? new())
            {
                var resource = ResolveSendTaskResource(st);
                var nextState = GetSingleOutgoingTarget(process, st.Id);
                definition.States[st.Id] = new StateDefinition
                {
                    Type = string.IsNullOrEmpty(resource) ? StateType.Pass : StateType.Task,
                    Comment = $"Send Task: {st.Name ?? st.Id}",
                    Resource = resource,
                    Result = resource == null ? new JObject { ["sent"] = true } : null,
                    Next = nextState,
                    End = string.IsNullOrEmpty(nextState) ? true : (bool?)null
                };
            }

            // ── Convert Exclusive Gateways → Choice states ──────────────────
            _logger.LogInformation("Exclusive gateways: {Count}, Sequence flows: {FlowCount}",
                process.ExclusiveGateways?.Count ?? 0, process.SequenceFlows?.Count ?? 0);
            foreach (var eg in process.ExclusiveGateways ?? new())
            {
                var choiceState = ConvertExclusiveGateway(eg, process);
                definition.States[eg.Id] = choiceState;
            }

            // ── Convert Inclusive Gateways → Choice states (best-effort) ────
            foreach (var ig in process.InclusiveGateways ?? new())
            {
                var choiceState = ConvertInclusiveGateway(ig, process);
                definition.States[ig.Id] = choiceState;
            }

            // ── Convert Parallel Gateways → Parallel states ─────────────────
            foreach (var pg in process.ParallelGateways ?? new())
            {
                var parallelState = ConvertParallelGateway(pg, process, definition, allNodes, gatewayNodes, endEventIds);
                definition.States[pg.Id] = parallelState;
            }

            // ── Convert End Events ──────────────────────────────────────────
            foreach (var ee in process.EndEvents ?? new())
            {
                definition.States[ee.Id] = new StateDefinition
                {
                    Type = StateType.Succeed,
                    Comment = $"End Event: {ee.Name ?? ee.Id}",
                    End = true
                };
            }

            _logger.LogInformation("Converted BPMN process '{ProcessId}' -> {StateCount} states",
                process.Id, definition.States.Count);
            return definition;
        }

        // ── Resource Resolution ─────────────────────────────────────────────

        private string ResolveResource(BpmnServiceTask task, BpmnProcess process)
        {
            // 1. Check zeebe taskDefinition type (Camunda 8)
            if (task.ZeebeTaskDefinition != null && !string.IsNullOrEmpty(task.ZeebeTaskDefinition.Type))
            {
                return MapZeebeTypeToResource(task.ZeebeTaskDefinition.Type, task);
            }

            // 2. Check camunda:calledElement (Camunda 7)
            if (!string.IsNullOrEmpty(task.CalledElement))
            {
                return $"flow://{task.CalledElement}";
            }

            // 3. Check extension properties for resource hints
            var resource = ExtractResourceFromExtensions(task.ExtensionElements);
            if (!string.IsNullOrEmpty(resource)) return resource;

            // 4. Check process properties for resource mapping
            resource = ExtractResourceFromProcessProperties(task, process);
            if (!string.IsNullOrEmpty(resource)) return resource;

            // 5. Default: treat as http call using task name
            var url = InferHttpUrl(task);
            return url ?? $"internal://echo";
        }

        private string MapZeebeTypeToResource(string zeebeType, BpmnServiceTask task)
        {
            // Zeebe types like "http-call", "rest-api", "ai-prompt", "decision" map to our resource schemes
            var lower = zeebeType.ToLowerInvariant();

            if (lower.Contains("http") || lower.Contains("rest") || lower.Contains("api") || lower.Contains("webhook"))
            {
                // Try to extract URL from properties
                var httpUrl = ExtractHttpUrlFromExtensions(task.ExtensionElements);
                return httpUrl ?? $"http://{task.Name?.Replace(" ", "").ToLowerInvariant() ?? task.Id}";
            }

            if (lower.Contains("ai") || lower.Contains("llm") || lower.Contains("prompt") || lower.Contains("openai") || lower.Contains("gpt"))
            {
                return "ai://decision";
            }

            if (lower.Contains("decision") || lower.Contains("dmn") || lower.Contains("rule"))
            {
                var decisionId = ExtractDecisionId(task.ExtensionElements) ?? task.Name ?? task.Id;
                return $"rule://{decisionId}";
            }

            if (lower.Contains("flow") || lower.Contains("process") || lower.Contains("subprocess"))
            {
                return $"flow://{task.Name ?? task.Id}";
            }

            // Default: warn about unrecognized type and fallback
            var url = ExtractHttpUrlFromExtensions(task.ExtensionElements);
            return url ?? $"internal://echo";
        }

        private string ResolveSendTaskResource(BpmnSendTask task)
        {
            var url = ExtractHttpUrlFromExtensions(task.ExtensionElements);
            if (!string.IsNullOrEmpty(url)) return url;
            return null;
        }

        // ── Gateway Conversion ──────────────────────────────────────────────

        private StateDefinition ConvertExclusiveGateway(BpmnExclusiveGateway gateway, BpmnProcess process)
        {
            var outgoingFlows = process.SequenceFlows
                .Where(f => f.SourceRef == gateway.Id)
                .OrderBy(f => f.ConditionExpression ?? "") // default (no condition) last
                .ToList();

            var choices = new List<ChoiceRule>();

            foreach (var flow in outgoingFlows)
            {
                if (!string.IsNullOrEmpty(flow.ConditionExpression))
                {
                    var choiceRule = ConvertConditionToChoiceRule(flow.ConditionExpression, flow.TargetRef, flow.Name);
                    if (choiceRule != null) choices.Add(choiceRule);
                }
            }

            // The flow without a condition expression is the default
            var defaultFlow = outgoingFlows.FirstOrDefault(f => string.IsNullOrEmpty(f.ConditionExpression));
            var defaultTarget = defaultFlow?.TargetRef ?? gateway.DefaultFlow;

            // If no explicit default but there are outgoing flows, use last as default
            if (string.IsNullOrEmpty(defaultTarget) && outgoingFlows.Count > 0)
            {
                defaultTarget = outgoingFlows.Last().TargetRef;
            }

            return new StateDefinition
            {
                Type = StateType.Choice,
                Comment = $"Exclusive Gateway: {gateway.Name ?? gateway.Id}",
                Choices = choices.Count > 0 ? choices : null,
                Default = defaultTarget
            };
        }

        private StateDefinition ConvertInclusiveGateway(BpmnInclusiveGateway gateway, BpmnProcess process)
        {
            // Inclusive gateways are not natively supported in ASL, so we convert to Choice
            // (first matching path wins — approximation)
            var outgoingFlows = process.SequenceFlows
                .Where(f => f.SourceRef == gateway.Id)
                .ToList();

            var choices = new List<ChoiceRule>();
            foreach (var flow in outgoingFlows)
            {
                if (!string.IsNullOrEmpty(flow.ConditionExpression))
                {
                    var choiceRule = ConvertConditionToChoiceRule(flow.ConditionExpression, flow.TargetRef, flow.Name);
                    if (choiceRule != null) choices.Add(choiceRule);
                }
            }

            var defaultFlow = outgoingFlows.FirstOrDefault(f => string.IsNullOrEmpty(f.ConditionExpression));
            var defaultTarget = defaultFlow?.TargetRef ?? gateway.DefaultFlow;

            return new StateDefinition
            {
                Type = StateType.Choice,
                Comment = $"Inclusive Gateway (approximated as Choice): {gateway.Name ?? gateway.Id}",
                Choices = choices.Count > 0 ? choices : null,
                Default = defaultTarget
            };
        }

        private StateDefinition ConvertParallelGateway(
            BpmnParallelGateway gateway,
            BpmnProcess process,
            StateMachineDefinition definition,
            Dictionary<string, BpmnFlowNodeInfo> allNodes,
            HashSet<string> gatewayNodes,
            HashSet<string> endEventIds)
        {
            var outgoingFlows = process.SequenceFlows.Where(f => f.SourceRef == gateway.Id).ToList();

            if (outgoingFlows.Count <= 1)
            {
                // No split — just pass through to next
                var target = outgoingFlows.FirstOrDefault()?.TargetRef;
                return new StateDefinition
                {
                    Type = StateType.Pass,
                    Comment = $"Parallel Gateway (merge): {gateway.Name ?? gateway.Id}",
                    Next = target,
                    End = string.IsNullOrEmpty(target) ? true : (bool?)null
                };
            }

            // Build branches: each branch traces from an outgoing target to the next merge point
            var branches = new List<StateMachineDefinition>();
            var mergePoints = FindMergePoints(process, gateway.Id, outgoingFlows);

            foreach (var flow in outgoingFlows)
            {
                var branch = BuildBranchChain(flow.TargetRef, mergePoints, process, allNodes, gatewayNodes, endEventIds);
                branches.Add(branch);
            }

            // Find the converging gateway (where all branches rejoin)
            var convergeTarget = FindConvergeTarget(process, gateway.Id, outgoingFlows);

            return new StateDefinition
            {
                Type = StateType.Parallel,
                Comment = $"Parallel Gateway (split): {gateway.Name ?? gateway.Id}",
                Branches = branches,
                Next = convergeTarget,
                End = string.IsNullOrEmpty(convergeTarget) ? true : (bool?)null
            };
        }

        // ── Condition Expression Conversion ─────────────────────────────────

        private ChoiceRule? ConvertConditionToChoiceRule(string condition, string next, string? flowName)
        {
            // BPMN conditions are typically FEEL expressions
            // We convert common patterns to ASL choice rules

            var trimmed = condition.Trim();

            // Pattern: variable == value
            var eqMatch = Regex.Match(trimmed, @"(\w+)\s*==\s*['""]?(.+?)['""]?\s*$");
            if (eqMatch.Success)
            {
                var varName = "$." + eqMatch.Groups[1].Value;
                var val = eqMatch.Groups[2].Value.Trim('\'', '"');

                if (bool.TryParse(val, out var boolVal))
                {
                    return new ChoiceRule { Variable = varName, BooleanEquals = boolVal, Next = next };
                }
                if (double.TryParse(val, out var numVal))
                {
                    return new ChoiceRule { Variable = varName, NumericEquals = numVal, Next = next };
                }
                return new ChoiceRule { Variable = varName, StringEquals = val, Next = next };
            }

            // Pattern: variable > value, variable < value, etc.
            var compMatch = Regex.Match(trimmed, @"(\w+)\s*([><]=?|!=)\s*['""]?(.+?)['""]?\s*$");
            if (compMatch.Success)
            {
                var varName = "$." + compMatch.Groups[1].Value;
                var op = compMatch.Groups[2].Value;
                var val = compMatch.Groups[3].Value.Trim('\'', '"');

                if (double.TryParse(val, out var numVal))
                {
                    return op switch
                    {
                        ">" => new ChoiceRule { Variable = varName, NumericGreaterThan = numVal, Next = next },
                        ">=" => new ChoiceRule { Variable = varName, NumericGreaterThanEquals = numVal, Next = next },
                        "<" => new ChoiceRule { Variable = varName, NumericLessThan = numVal, Next = next },
                        "<=" => new ChoiceRule { Variable = varName, NumericLessThanEquals = numVal, Next = next },
                        "!=" => new ChoiceRule { Variable = varName, StringEquals = val, Next = next }, // inverted, approximate
                        _ => new ChoiceRule { Variable = varName, StringEquals = val, Next = next }
                    };
                }
            }

            // Pattern: variable contains value
            var containsMatch = Regex.Match(trimmed, @"contains\s*\(\s*(\w+)\s*,\s*['""]?(.+?)['""]?\s*\)");
            if (containsMatch.Success)
            {
                return new ChoiceRule
                {
                    Variable = "$." + containsMatch.Groups[1].Value,
                    StringMatches = "*" + Regex.Escape(containsMatch.Groups[2].Value) + "*",
                    Next = next
                };
            }

            // Pattern: startsWith(...)
            var startsMatch = Regex.Match(trimmed, @"startsWith\s*\(\s*(\w+)\s*,\s*['""]?(.+?)['""]?\s*\)");
            if (startsMatch.Success)
            {
                return new ChoiceRule
                {
                    Variable = "$." + startsMatch.Groups[1].Value,
                    StringMatches = startsMatch.Groups[2].Value + "*",
                    Next = next
                };
            }

            // Pattern: variable > 0 (truthy check)
            var truthyMatch = Regex.Match(trimmed, @"(\w+)\s*>\s*0");
            if (truthyMatch.Success)
            {
                return new ChoiceRule
                {
                    Variable = "$." + truthyMatch.Groups[1].Value,
                    NumericGreaterThan = 0,
                    Next = next
                };
            }

            // ── Compound expressions: "A and B", "A or B" ──────────────────

            // Pattern: expr1 AND expr2 (case-insensitive)
            var andMatch = Regex.Match(trimmed, @"(.+?)\s+and\s+(.+)", RegexOptions.IgnoreCase);
            if (andMatch.Success)
            {
                var left = ConvertConditionToChoiceRule(andMatch.Groups[1].Value, null, null);
                var right = ConvertConditionToChoiceRule(andMatch.Groups[2].Value, null, null);
                if (left != null && right != null)
                {
                    return new ChoiceRule
                    {
                        And = new List<ChoiceRule> { left, right },
                        Next = next
                    };
                }
            }

            // Pattern: expr1 OR expr2 (case-insensitive)
            var orMatch = Regex.Match(trimmed, @"(.+?)\s+or\s+(.+)", RegexOptions.IgnoreCase);
            if (orMatch.Success)
            {
                var left = ConvertConditionToChoiceRule(orMatch.Groups[1].Value, null, null);
                var right = ConvertConditionToChoiceRule(orMatch.Groups[2].Value, null, null);
                if (left != null && right != null)
                {
                    return new ChoiceRule
                    {
                        Or = new List<ChoiceRule> { left, right },
                        Next = next
                    };
                }
            }

            // Strip leading '=' from FEEL expressions and retry
            if (trimmed.StartsWith("="))
            {
                return ConvertConditionToChoiceRule(trimmed[1..], next, flowName);
            }

            // Fallback: treat as string equals with the whole expression
            _logger.LogDebug("Could not fully parse BPMN condition '{Condition}', using fallback", trimmed);
            return new ChoiceRule
            {
                Variable = "$",
                StringEquals = trimmed,
                Next = next
            };
        }

        // ── Helper Methods ──────────────────────────────────────────────────

        private BpmnDefinitions? DeserializeBpmn(string xml)
        {
            using var reader = new StringReader(xml);
            var serializer = new XmlSerializer(typeof(BpmnDefinitions));

            // Handle XML namespaces in Camunda 8 files
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("bpmn", "http://www.omg.org/spec/BPMN/20100524/MODEL");
            namespaces.Add("camunda", "http://camunda.org/schema/1.0/bpmn");
            namespaces.Add("zeebe", "http://camunda.org/schema/zeebe/1.0");
            namespaces.Add("omgdi", "http://www.omg.org/spec/BPMN/20100524/DI");
            namespaces.Add("omgdc", "http://www.omg.org/spec/BPMN/20100524/DC");

            try
            {
                return (BpmnDefinitions)serializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize BPMN XML");
                throw new ArgumentException("Invalid BPMN XML", ex);
            }
        }

        // ── XML Post-Processing: Extract zeebe/camunda properties ─────────────

        private void EnrichWithXmlAttributes(string xml, BpmnProcess process)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                var bpmnNs = "http://www.omg.org/spec/BPMN/20100524/MODEL";
                var zeebeNs = "http://camunda.org/schema/zeebe/1.0";
                var camundaNs = "http://camunda.org/schema/1.0/bpmn";

                var processEl = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "process" && e.Attribute("id")?.Value == process.Id);
                if (processEl == null) return;

                // Enrich service tasks
                foreach (var st in process.ServiceTasks ?? new())
                {
                    var taskEl = processEl.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "serviceTask" && e.Attribute("id")?.Value == st.Id);
                    if (taskEl == null) continue;

                    // Extract zeebe:taskDefinition
                    var taskDef = taskEl.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "taskDefinition" && e.Name.Namespace == zeebeNs);
                    if (taskDef != null && st.ZeebeTaskDefinition == null)
                    {
                        st.ZeebeTaskDefinition = new BpmnZeebeTaskDefinition
                        {
                            Type = taskDef.Attribute("type")?.Value,
                            Retries = taskDef.Attribute("retries")?.Value
                        };
                    }
                    else if (taskDef != null)
                    {
                        st.ZeebeTaskDefinition.Type ??= taskDef.Attribute("type")?.Value;
                    }

                    // Extract zeebe:ioMapping
                    var ioMapping = taskEl.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "ioMapping" && e.Name.Namespace == zeebeNs);
                    if (ioMapping != null && st.ZeebeIoMapping == null)
                    {
                        st.ZeebeIoMapping = new BpmnZeebeIoMapping
                        {
                            Inputs = ioMapping.Elements()
                                .Where(e => e.Name.LocalName == "input")
                                .Select(e => new BpmnZeebeInput
                                {
                                    Source = e.Attribute("source")?.Value,
                                    Target = e.Attribute("target")?.Value
                                }).ToList(),
                            Outputs = ioMapping.Elements()
                                .Where(e => e.Name.LocalName == "output")
                                .Select(e => new BpmnZeebeOutput
                                {
                                    Source = e.Attribute("source")?.Value,
                                    Target = e.Attribute("target")?.Value
                                }).ToList()
                        };
                    }

                    // Extract zeebe/camunda properties (url, endpoint, etc.)
                    var props = taskEl.Descendants()
                        .Where(e => e.Name.LocalName == "property" &&
                                   (e.Name.Namespace == zeebeNs || e.Name.Namespace == camundaNs))
                        .ToList();
                    if (props.Count > 0 && st.ExtensionElements?.ZeebeProperties == null)
                    {
                        st.ExtensionElements ??= new BpmnExtensionElements();
                        st.ExtensionElements.ZeebeProperties = new BpmnProperties
                        {
                            Items = props.Select(p => new BpmnPropertyItem
                            {
                                Name = p.Attribute("name")?.Value ?? "",
                                Value = p.Attribute("value")?.Value ?? ""
                            }).ToList()
                        };
                    }
                }

                // Enrich sequence flows with condition expressions
                foreach (var sf in process.SequenceFlows ?? new())
                {
                    var flowEl = processEl.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "sequenceFlow" && e.Attribute("id")?.Value == sf.Id);
                    if (flowEl == null) continue;

                    var condEl = flowEl.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "conditionExpression");
                    if (condEl != null && string.IsNullOrEmpty(sf.ConditionExpression))
                    {
                        sf.ConditionExpression = condEl.Value;
                    }
                }

                // Enrich send tasks
                foreach (var st in process.SendTasks ?? new())
                {
                    var taskEl = processEl.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "sendTask" && e.Attribute("id")?.Value == st.Id);
                    if (taskEl == null) continue;

                    var props = taskEl.Descendants()
                        .Where(e => e.Name.LocalName == "property" &&
                                   (e.Name.Namespace == zeebeNs || e.Name.Namespace == camundaNs))
                        .ToList();
                    if (props.Count > 0)
                    {
                        st.ExtensionElements ??= new BpmnExtensionElements();
                        st.ExtensionElements.ZeebeProperties = new BpmnProperties
                        {
                            Items = props.Select(p => new BpmnPropertyItem
                            {
                                Name = p.Attribute("name")?.Value ?? "",
                                Value = p.Attribute("value")?.Value ?? ""
                            }).ToList()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich BPMN with XML attributes");
            }
        }

        private record BpmnFlowNodeInfo(string Id, string NodeType);

        private Dictionary<string, BpmnFlowNodeInfo> BuildFlowNodeMap(BpmnProcess process)
        {
            var map = new Dictionary<string, BpmnFlowNodeInfo>();
            foreach (var se in process.StartEvents ?? new()) map[se.Id] = new(se.Id, "StartEvent");
            foreach (var ee in process.EndEvents ?? new()) map[ee.Id] = new(ee.Id, "EndEvent");
            foreach (var t in process.Tasks ?? new()) map[t.Id] = new(t.Id, "Task");
            foreach (var st in process.ServiceTasks ?? new()) map[st.Id] = new(st.Id, "ServiceTask");
            foreach (var st in process.ScriptTasks ?? new()) map[st.Id] = new(st.Id, "ScriptTask");
            foreach (var rt in process.ReceiveTasks ?? new()) map[rt.Id] = new(rt.Id, "ReceiveTask");
            foreach (var st in process.SendTasks ?? new()) map[st.Id] = new(st.Id, "SendTask");
            foreach (var eg in process.ExclusiveGateways ?? new()) map[eg.Id] = new(eg.Id, "ExclusiveGateway");
            foreach (var pg in process.ParallelGateways ?? new()) map[pg.Id] = new(pg.Id, "ParallelGateway");
            foreach (var ig in process.InclusiveGateways ?? new()) map[ig.Id] = new(ig.Id, "InclusiveGateway");
            foreach (var sp in process.SubProcesses ?? new()) map[sp.Id] = new(sp.Id, "SubProcess");
            return map;
        }

        private string? GetSingleOutgoingTarget(BpmnProcess process, string nodeId)
        {
            var outgoing = process.SequenceFlows
                .Where(f => f.SourceRef == nodeId)
                .ToList();

            if (outgoing.Count == 0) return null;
            if (outgoing.Count == 1) return outgoing[0].TargetRef;

            // Multiple outgoing without conditions — use first
            var unconditional = outgoing.Where(f => string.IsNullOrEmpty(f.ConditionExpression)).ToList();
            return unconditional.Count > 0 ? unconditional[0].TargetRef : outgoing[0].TargetRef;
        }

        private void ApplyZeebeIoMapping(StateDefinition state, BpmnServiceTask task)
        {
            if (task.ZeebeIoMapping == null) return;

            var mapping = task.ZeebeIoMapping;

            // Build Parameters from zeebe inputs
            if (mapping.Inputs.Count > 0)
            {
                var paramsObj = new JObject();
                foreach (var input in mapping.Inputs)
                {
                    if (!string.IsNullOrEmpty(input.Source) && !string.IsNullOrEmpty(input.Target))
                    {
                        // Source is a variable reference, target is the parameter name
                        var sourcePath = input.Source.StartsWith("$") ? input.Source : "$." + input.Source;
                        paramsObj[input.Target] = new JValue(sourcePath);
                    }
                }
                state.Parameters = paramsObj;
            }

            // ResultPath from zeebe outputs
            if (mapping.Outputs.Count > 0 && mapping.Outputs[0].Target != null)
            {
                var target = mapping.Outputs[0].Target;
                if (!target.StartsWith("$")) target = "$." + target;
                state.ResultPath = target;
            }
        }

        private StateDefinition WrapInMapState(StateDefinition innerState, BpmnMultiInstance mi)
        {
            var itemsPath = mi.Collection?.StartsWith("$") == true ? mi.Collection : "$." + (mi.Collection ?? "items");

            return new StateDefinition
            {
                Type = StateType.Map,
                Comment = $"Multi-Instance (collection: {mi.Collection})",
                ItemsPath = itemsPath,
                Iterator = new StateMachineDefinition
                {
                    StartAt = "MapItem",
                    States = new Dictionary<string, StateDefinition>
                    {
                        ["MapItem"] = new StateDefinition
                        {
                            Type = StateType.Task,
                            Resource = innerState.Resource,
                            Comment = innerState.Comment,
                            Parameters = innerState.Parameters,
                            End = true
                        }
                    }
                },
                Next = innerState.Next,
                End = innerState.End
            };
        }

        // ── Parallel Branch Building ────────────────────────────────────────

        private HashSet<string> FindMergePoints(BpmnProcess process, string splitGatewayId, List<BpmnSequenceFlow> outgoingFlows)
        {
            // A merge point is a node that has multiple incoming flows from different branches
            var allTargets = new Dictionary<string, int>();
            foreach (var flow in outgoingFlows)
            {
                TracePathTargets(flow.TargetRef, process, new HashSet<string> { splitGatewayId }, allTargets);
            }
            return allTargets.Where(kvp => kvp.Value > 1).Select(kvp => kvp.Key).ToHashSet();
        }

        private void TracePathTargets(string nodeId, BpmnProcess process, HashSet<string> visited, Dictionary<string, int> targets)
        {
            if (visited.Contains(nodeId)) return;
            visited.Add(nodeId);

            var outgoing = process.SequenceFlows.Where(f => f.SourceRef == nodeId).ToList();
            if (outgoing.Count == 0)
            {
                targets[nodeId] = targets.GetValueOrDefault(nodeId) + 1;
                return;
            }

            foreach (var flow in outgoing)
            {
                targets[flow.TargetRef] = targets.GetValueOrDefault(flow.TargetRef) + 1;
                TracePathTargets(flow.TargetRef, process, visited, targets);
            }
        }

        private StateMachineDefinition BuildBranchChain(
            string startNodeId,
            HashSet<string> mergePoints,
            BpmnProcess process,
            Dictionary<string, BpmnFlowNodeInfo> allNodes,
            HashSet<string> gatewayNodes,
            HashSet<string> endEventIds)
        {
            var branchDef = new StateMachineDefinition
            {
                StartAt = startNodeId,
                States = new Dictionary<string, StateDefinition>()
            };

            var current = startNodeId;
            var visited = new HashSet<string>();

            while (!string.IsNullOrEmpty(current) && !visited.Contains(current) && !mergePoints.Contains(current))
            {
                visited.Add(current);

                if (!allNodes.TryGetValue(current, out var nodeInfo))
                {
                    // Unknown node, create pass-through
                    var branchNext = GetSingleOutgoingTarget(process, current);
                    branchDef.States[current] = new StateDefinition
                    {
                        Type = StateType.Pass,
                        Comment = $"Branch node: {current}",
                        Next = branchNext,
                        End = string.IsNullOrEmpty(branchNext) ? true : (bool?)null
                    };
                    current = branchNext;
                    continue;
                }

                var next = GetSingleOutgoingTarget(process, current);
                var isEnd = endEventIds.Contains(current);

                branchDef.States[current] = new StateDefinition
                {
                    Type = nodeInfo.NodeType switch
                    {
                        "ServiceTask" => StateType.Task,
                        "Task" => StateType.Pass,
                        "ScriptTask" => StateType.Pass,
                        "ReceiveTask" => StateType.Wait,
                        "SendTask" => StateType.Pass,
                        "EndEvent" => StateType.Succeed,
                        _ => StateType.Pass
                    },
                    Comment = $"Branch: {current}",
                    Result = new JObject { ["node"] = current },
                    Next = next,
                    End = (string.IsNullOrEmpty(next) || isEnd) ? true : (bool?)null
                };

                current = next;
            }

            return branchDef;
        }

        private string? FindConvergeTarget(BpmnProcess process, string splitGatewayId, List<BpmnSequenceFlow> outgoingFlows)
        {
            // Find the first parallel gateway that all branches lead to
            foreach (var pg in process.ParallelGateways ?? new())
            {
                if (pg.Id == splitGatewayId) continue;

                var incoming = process.SequenceFlows.Where(f => f.TargetRef == pg.Id).ToList();
                if (incoming.Count >= 2)
                {
                    // Check if this gateway is reachable from all branches
                    var reachableFromAll = outgoingFlows.All(OF =>
                        IsReachable(OF.TargetRef, pg.Id, process, new HashSet<string>()));
                    if (reachableFromAll) return GetSingleOutgoingTarget(process, pg.Id);
                }
            }

            // Fallback: find any node with multiple incoming from branch paths
            var incomingCounts = new Dictionary<string, int>();
            foreach (var flow in process.SequenceFlows)
            {
                incomingCounts[flow.TargetRef] = incomingCounts.GetValueOrDefault(flow.TargetRef) + 1;
            }

            var mergeNode = incomingCounts.FirstOrDefault(kvp => kvp.Value >= 2);
            return mergeNode.Key;
        }

        private bool IsReachable(string from, string to, BpmnProcess process, HashSet<string> visited)
        {
            if (from == to) return true;
            if (visited.Contains(from)) return false;
            visited.Add(from);

            var outgoing = process.SequenceFlows.Where(f => f.SourceRef == from).ToList();
            return outgoing.Any(f => IsReachable(f.TargetRef, to, process, visited));
        }

        // ── Resource Extraction Helpers ─────────────────────────────────────

        private string? ExtractResourceFromExtensions(BpmnExtensionElements? ext)
        {
            if (ext?.CamundaProperties?.Items != null)
            {
                var resourceProp = ext.CamundaProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("url", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("httpUrl", StringComparison.OrdinalIgnoreCase));
                if (resourceProp != null) return resourceProp.Value;
            }

            if (ext?.ZeebeProperties?.Items != null)
            {
                var resourceProp = ext.ZeebeProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("url", StringComparison.OrdinalIgnoreCase));
                if (resourceProp != null) return resourceProp.Value;
            }

            // Check nested extension elements
            if (ext?.NestedExtensionElements != null)
            {
                return ExtractResourceFromExtensions(ext.NestedExtensionElements);
            }

            return null;
        }

        private string? ExtractResourceFromProcessProperties(BpmnServiceTask task, BpmnProcess process)
        {
            if (process.ExtensionElements?.CamundaProperties?.Items != null)
            {
                var mapping = process.ExtensionElements.CamundaProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("resourceMapping", StringComparison.OrdinalIgnoreCase));
                if (mapping != null && mapping.Value.Contains(task.Id))
                {
                    var parts = mapping.Value.Split(':');
                    return parts.Length > 1 ? parts[1] : null;
                }
            }
            return null;
        }

        private string? ExtractHttpUrlFromExtensions(BpmnExtensionElements? ext)
        {
            if (ext?.CamundaProperties?.Items != null)
            {
                var urlProp = ext.CamundaProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("url", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("httpUrl", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("endpoint", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("uri", StringComparison.OrdinalIgnoreCase));
                if (urlProp != null) return urlProp.Value;
            }

            if (ext?.ZeebeProperties?.Items != null)
            {
                var urlProp = ext.ZeebeProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("url", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("httpUrl", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("endpoint", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("uri", StringComparison.OrdinalIgnoreCase));
                if (urlProp != null) return urlProp.Value;
            }

            if (ext?.NestedExtensionElements != null)
            {
                return ExtractHttpUrlFromExtensions(ext.NestedExtensionElements);
            }

            return null;
        }

        private string? ExtractDecisionId(BpmnExtensionElements? ext)
        {
            if (ext?.ZeebeProperties?.Items != null)
            {
                var prop = ext.ZeebeProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("decisionId", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("dmn", StringComparison.OrdinalIgnoreCase));
                if (prop != null) return prop.Value;
            }

            if (ext?.CamundaProperties?.Items != null)
            {
                var prop = ext.CamundaProperties.Items
                    .FirstOrDefault(p => p.Name.Equals("decisionId", StringComparison.OrdinalIgnoreCase));
                if (prop != null) return prop.Value;
            }

            return null;
        }

        private string? InferHttpUrl(BpmnServiceTask task)
        {
            // Try to guess URL from task name patterns
            var name = task.Name ?? task.Id;
            if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            return null;
        }
    }
}
