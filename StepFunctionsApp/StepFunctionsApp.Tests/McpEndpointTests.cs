using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // MCP ENDPOINT TESTS
    // Exercises the real /mcp Streamable HTTP endpoint (MapMcp -> transport ->
    // FlowTools -> StepFunctionService) in-process via WebApplicationFactory,
    // speaking raw JSON-RPC 2.0 exactly as an external AI harness would.
    // Wire facts verified against the live server: responses are framed as SSE
    // ("event: message" / "data: <jsonrpc>"), requests need no session header.
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class McpEndpointTests : IClassFixture<McpEndpointTests.Factory>
    {
        public sealed class Factory : WebApplicationFactory<Program>
        {
            // Isolate this host's durable flow-state store in a fresh temp dir so startup recovery never loads
            // checkpoints from other runs (their embedded definitions carry stale ephemeral ports).
            private readonly string _flowStateDir = Path.Combine(Path.GetTempPath(), "stepflow-tests", Guid.NewGuid().ToString("N"));

            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [FlowStateOptions.SectionName + ":DiskPath"] = _flowStateDir,
                        [HumanTaskOptions.SectionName + ":DiskPath"] = Path.Combine(_flowStateDir, "human-tasks"), // keep task records out of the repo
                        ["Rules:Path"] = Path.Combine(_flowStateDir, "rules.json"), // keep the named-rule catalog out of the repo
                    }));

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(_flowStateDir, recursive: true); } catch { /* best effort */ }
            }
        }

        private readonly HttpClient _client;

        public McpEndpointTests(Factory factory) => _client = factory.CreateClient();

        // ── MCP JSON-RPC client (Streamable HTTP) ─────────────────────────────

        /// <summary>Sends one JSON-RPC request to /mcp and returns the parsed response object.</summary>
        private async Task<JObject> RpcAsync(string method, JObject? @params = null, int id = 1)
        {
            var request = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
            if (@params is not null) request["params"] = @params;

            var http = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            http.Headers.Accept.ParseAdd("application/json");
            http.Headers.Accept.ParseAdd("text/event-stream");

            var response = await _client.SendAsync(http);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                (int)response.StatusCode is >= 200 and < 300,
                $"MCP {method} -> HTTP {(int)response.StatusCode}: {body}");

            // Server frames responses as SSE; tolerate plain JSON too.
            var json = body.TrimStart().StartsWith('{') ? body : ExtractSseData(body);
            return JObject.Parse(json);
        }

        /// <summary>Joins the payloads of all "data:" lines in an SSE response.</summary>
        private static string ExtractSseData(string sse)
        {
            var data = new StringBuilder();
            foreach (var line in sse.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                    data.Append(trimmed["data:".Length..].TrimStart());
            }
            return data.ToString();
        }

        /// <summary>Invokes a tool and returns the parsed JSON object from result.content[0].text.</summary>
        private async Task<JObject> CallToolAsync(string name, JObject arguments)
        {
            var rpc = await RpcAsync("tools/call", new JObject
            {
                ["name"] = name,
                ["arguments"] = arguments
            });

            Assert.True(
                rpc.TryGetValue("result", out var result),
                $"Protocol error calling {name}: {rpc["error"]}");

            var text = (string)((JArray)result["content"]!)[0]!["text"]!;
            return JObject.Parse(text);
        }

        private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

        // ── 1. HANDSHAKE & DISCOVERY ───────────────────────────────────────────

        [Fact]
        public async Task Initialize_ReturnsProtocolVersionAndServerInfo()
        {
            var rpc = await RpcAsync("initialize", new JObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "mcp-endpoint-test", ["version"] = "1.0" }
            });

            var result = rpc["result"]!;
            Assert.False(string.IsNullOrEmpty((string)result["protocolVersion"]!));
            // Server name is the entry-assembly name ("testhost" under WebApplicationFactory, "StepFunctionsApp" live).
            Assert.False(string.IsNullOrEmpty((string)result["serverInfo"]!["name"]!));
            Assert.NotNull(result["capabilities"]!["tools"]);
        }

        [Fact]
        public async Task ToolsList_ExposesAllToolGroupsWithSchemas()
        {
            var rpc = await RpcAsync("tools/list");
            var tools = (JArray)rpc["result"]!["tools"]!;

            // Ten tool groups: flows (+ execution lifecycle), data-exchange profiles, metadata (attribute domains + schema definitions),
            // EAV rows/registry, dynamic APIs, solution packages, named rules, human tasks, form capture, form definitions.
            // Names are snake_case of the method names.
            var expected = new[]
            {
                "complete_human_task",
                "delete_attribute_domain", "delete_data_exchange_profile", "delete_dynamic_api",
                "delete_eav_entity", "delete_eav_row", "delete_execution", "delete_flow",
                "delete_form_definition", "delete_rule", "delete_schema_definition",
                "export_solution",
                "get_attribute_domain", "get_data_exchange_profile", "get_dynamic_api",
                "get_execution", "get_flow", "get_form_capture", "get_form_definition",
                "get_human_task",
                "get_rule", "get_schema_definition",
                "import_solution",
                "list_attribute_domains", "list_data_exchange_profiles", "list_dynamic_apis",
                "list_eav_domains", "list_eav_entities", "list_executions", "list_flows",
                "list_form_definitions", "list_human_tasks",
                "list_rules", "list_schema_definitions",
                "patch_eav_row", "read_eav_rows", "register_eav_entity", "resume_execution",
                "run_data_exchange_profile", "run_flow",
                "save_attribute_domain", "save_data_exchange_profile", "save_dynamic_api",
                "save_flow", "save_form_definition", "save_rule", "save_schema_definition",
                "stop_execution", "submit_form_capture",
                "update_eav_row", "write_eav_row"
            };
            var names = tools.Select(t => (string)t["name"]!).ToList();
            Assert.Equal(expected, names.OrderBy(n => n));

            foreach (var tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace((string)tool["description"]!), $"Tool {(string)tool["name"]!} has no description");
                Assert.Equal("object", (string)tool["inputSchema"]!["type"]!);
            }
        }

        // ── 2. SAVE / LIST / GET ROUND-TRIP ────────────────────────────────────

        [Fact]
        public async Task SaveFlow_ReturnsIdAndAppearsInListFlows()
        {
            var name = UniqueName("mcp-test");
            var saved = await CallToolAsync("save_flow", new JObject
            {
                ["name"] = name,
                ["statesJson"] = "{\"Only\":{\"type\":\"Succeed\"}}"
            });

            Assert.False(string.IsNullOrEmpty((string)saved["id"]!));
            Assert.Equal(name, (string)saved["name"]!);
            // Contract: updatedAt is an ISO-8601 round-trip string (verified against live endpoint).
            Assert.True(DateTime.Parse((string)saved["updatedAt"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) != default);

            var list = JArray.Parse(await ListFlowsTextAsync());
            Assert.Contains(list, f => (string)f["name"]! == name);
        }

        [Fact]
        public async Task GetFlow_ReturnsCamelCaseDefinitionPreservingStateNames()
        {
            // Mixed-case state names must survive the camelCase serializer:
            // property names are lower-cased, dictionary keys (state names) are not.
            var name = UniqueName("mcp-test");
            await CallToolAsync("save_flow", new JObject
            {
                ["name"] = name,
                ["statesJson"] = "{\"MyStart\":{\"type\":\"Pass\",\"next\":\"EchoStep\"},\"EchoStep\":{\"type\":\"Task\",\"resource\":\"internal://echo\",\"parameters\":{\"Note\":\"kept\"},\"next\":\"TheEnd\"},\"TheEnd\":{\"type\":\"Succeed\"}}"
            });

            var flow = await CallToolAsync("get_flow", new JObject { ["idOrName"] = name });
            var definition = (JObject)flow["definition"]!;

            Assert.Equal("MyStart", (string)definition["startAt"]!);
            Assert.NotNull(definition["states"]!["MyStart"]);   // key preserved, not "mystart"
            Assert.Equal("Task", (string)definition["states"]!["EchoStep"]!["type"]!);
            Assert.Equal("kept", (string)definition["states"]!["EchoStep"]!["parameters"]!["Note"]!);
        }

        [Fact]
        public async Task SaveFlow_UnknownStartAt_ReturnsErrorListingAvailableStates()
        {
            var result = await CallToolAsync("save_flow", new JObject
            {
                ["name"] = UniqueName("mcp-test"),
                ["statesJson"] = "{\"A\":{\"type\":\"Succeed\"}}",
                ["startAt"] = "B"
            });

            Assert.Contains("not a state", (string)result["error"]!);
            Assert.Contains("Available states: A", (string)result["error"]!);
        }

        [Fact]
        public async Task SaveFlow_InvalidStatesJson_ReturnsError()
        {
            var result = await CallToolAsync("save_flow", new JObject
            {
                ["name"] = UniqueName("mcp-test"),
                ["statesJson"] = "{not valid json"
            });

            Assert.Contains("not valid JSON", (string)result["error"]!);
        }

        // ── 2b. NAMED RULES ────────────────────────────────────────────────────

        [Fact]
        public async Task Rules_RoundTrip_SaveListGetDelete()
        {
            var ruleName = UniqueName("mcp-rule");

            // save_rule persists the artifact and returns it (camelCase).
            var saved = await CallToolAsync("save_rule", new JObject
            {
                ["ruleJson"] = new JObject
                {
                    ["name"] = ruleName,
                    ["kind"] = "choice",
                    ["description"] = "demo choice rule",
                    ["definition"] = new JObject { ["conditions"] = new JArray(new JObject { ["expression"] = "true", ["next"] = "ok" }) }
                }.ToString(Formatting.None)
            });
            Assert.Equal(ruleName, (string)saved["name"]!);
            Assert.Equal("choice", (string)saved["kind"]!);

            // list_rules returns the catalog as a JSON array.
            var rpc = await RpcAsync("tools/call", new JObject { ["name"] = "list_rules", ["arguments"] = new JObject() });
            var listText = (string)((JArray)rpc["result"]!["content"]!)[0]!["text"]!;
            Assert.Contains(JArray.Parse(listText), r => (string)r!["name"] == ruleName);

            // get_rule returns the full definition body.
            var got = await CallToolAsync("get_rule", new JObject { ["name"] = ruleName });
            Assert.Equal(1, ((JArray)got["definition"]!["conditions"]!).Count);

            // delete_rule removes it from the catalog and engine.
            var del = await CallToolAsync("delete_rule", new JObject { ["name"] = ruleName });
            Assert.Equal(ruleName, (string)del["deleted"]!);
            var missing = await CallToolAsync("get_rule", new JObject { ["name"] = ruleName });
            Assert.Contains("not found", (string)missing["error"]!);
        }

        // ── 3. EXECUTION ───────────────────────────────────────────────────────

        [Fact]
        public async Task RunFlow_EchoFlow_SucceedsWithOutputAndHistory()
        {
            var name = UniqueName("mcp-test");
            await CallToolAsync("save_flow", new JObject
            {
                ["name"] = name,
                ["statesJson"] = "{\"Start\":{\"type\":\"Pass\",\"next\":\"Echo\"},\"Echo\":{\"type\":\"Task\",\"resource\":\"internal://echo\",\"parameters\":{\"note\":\"hello from mcp test\"},\"next\":\"Done\"},\"Done\":{\"type\":\"Succeed\"}}"
            });

            var run = await CallToolAsync("run_flow", new JObject
            {
                ["idOrName"] = name,
                ["inputJson"] = "{\"orderId\":42}"
            });

            Assert.Equal("Succeeded", (string)run["status"]!);
            Assert.False(string.IsNullOrEmpty((string)run["executionId"]!));
            Assert.Equal("hello from mcp test", (string)run["output"]!["note"]!);

            var entered = ((JArray)run["history"]!)
                .Where(h => (string)h["type"]! == "StateEntered")
                .Select(h => (string)h["state"]!)
                .ToList();
            Assert.Equal(new[] { "Start", "Echo", "Done" }, entered);
        }

        [Fact]
        public async Task RunFlow_UnknownFlow_ReturnsToolError()
        {
            var result = await CallToolAsync("run_flow", new JObject
            {
                ["idOrName"] = $"no-such-flow-{Guid.NewGuid():N}"
            });

            Assert.Contains("not found", (string)result["error"]!);
        }

        [Fact]
        public async Task GetFlow_UnknownFlow_ReturnsToolError()
        {
            var result = await CallToolAsync("get_flow", new JObject
            {
                ["idOrName"] = $"no-such-flow-{Guid.NewGuid():N}"
            });

            Assert.Contains("not found", (string)result["error"]!);
        }

        [Fact]
        public async Task RunFlow_InvalidInputJson_ReturnsToolError()
        {
            var name = UniqueName("mcp-test");
            await CallToolAsync("save_flow", new JObject
            {
                ["name"] = name,
                ["statesJson"] = "{\"Only\":{\"type\":\"Succeed\"}}"
            });

            var result = await CallToolAsync("run_flow", new JObject
            {
                ["idOrName"] = name,
                ["inputJson"] = "{bad json"
            });

            Assert.Contains("not valid JSON", (string)result["error"]!);
        }

        // ── HUMAN-TASK LIFECYCLE (headless operation) ────────────────────────

        [Fact]
        public async Task HumanTask_SuspendCompleteResume_FullyViaMcp()
        {
            var name = UniqueName("mcp-ht");
            await CallToolAsync("save_flow", new JObject
            {
                ["name"] = name,
                ["startAt"] = "A",
                ["statesJson"] = "{\"A\":{\"type\":\"Pass\",\"next\":\"B\"}," +
                                 "\"B\":{\"type\":\"HumanTask\",\"task\":{\"title\":\"Approve order\",\"assignee\":\"alice\"},\"completion\":{\"Type\":\"api\"},\"next\":\"C\"}," +
                                 "\"C\":{\"type\":\"Task\",\"resource\":\"internal://echo\",\"end\":true}}"
            });

            // run_flow executes synchronously until the HumanTask state suspends it.
            var run = await CallToolAsync("run_flow", new JObject
            {
                ["idOrName"] = name,
                ["inputJson"] = "{\"orderId\":42}"
            });
            Assert.Equal("Suspended", (string)run["status"]!);
            var executionId = (string)run["executionId"]!;

            // list_human_tasks surfaces the pending task for this execution.
            var rpc = await RpcAsync("tools/call", new JObject
            {
                ["name"] = "list_human_tasks",
                ["arguments"] = new JObject { ["executionId"] = executionId }
            });
            var tasks = JArray.Parse((string)((JArray)rpc["result"]!["content"]!)[0]!["text"]!);
            Assert.Equal(1, tasks.Count);
            var taskId = (string)tasks[0]!["taskId"]!;
            Assert.Equal("Pending", (string)tasks[0]!["status"]!);
            Assert.Equal(name, (string)tasks[0]!["stateMachineName"]!);

            // get_human_task exposes the payload snapshot taken at suspension time.
            var task = await CallToolAsync("get_human_task", new JObject { ["taskId"] = taskId });
            Assert.Equal("Approve order", (string)task["title"]!);
            Assert.NotNull(task["payload"]);

            // complete_human_task resumes; the resumed run continues in the background.
            var done = await CallToolAsync("complete_human_task", new JObject
            {
                ["taskId"] = taskId,
                ["resultJson"] = "{\"approved\":true}"
            });
            Assert.Equal("Completed", (string)done["status"]!);
            Assert.Equal(executionId, (string)done["executionId"]!);

            // Poll get_execution until the resumed run finishes. Without a ResultPath on state B the
            // completion result replaces the input entirely, so the echo state returns it unchanged.
            var exec = await WaitForTerminalAsync(executionId);
            Assert.Equal("Succeeded", (string)exec["status"]!);
            Assert.True(JToken.DeepEquals(new JObject { ["approved"] = true }, exec["output"]));

            // Completing twice is rejected with a tool error.
            var again = await CallToolAsync("complete_human_task", new JObject { ["taskId"] = taskId });
            Assert.Contains("already", (string)again["error"]!);
        }

        private async Task<JObject> WaitForTerminalAsync(string executionId, int timeoutMs = 15000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            JObject exec = new();
            while (DateTime.UtcNow < deadline)
            {
                exec = await CallToolAsync("get_execution", new JObject { ["executionId"] = executionId });
                if (exec["error"] == null && IsTerminal((string)exec["status"])) return exec;
                await Task.Delay(100);
            }
            Assert.Fail($"Execution {executionId} did not reach a terminal state: {(string?)exec["status"]}");
            throw new InvalidOperationException(); // unreachable
        }

        private static bool IsTerminal(string status) =>
            status is "Succeeded" or "Failed" or "Aborted" or "TimedOut";

        // ── helpers ────────────────────────────────────────────────────────────

        private async Task<string> ListFlowsTextAsync()
        {
            var rpc = await RpcAsync("tools/call", new JObject
            {
                ["name"] = "list_flows",
                ["arguments"] = new JObject()
            });
            return (string)((JArray)rpc["result"]!["content"]!)![0]!["text"]!;
        }
    }
}
