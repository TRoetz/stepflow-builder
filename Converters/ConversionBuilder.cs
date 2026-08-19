using System;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StepFunctionsApp.Converters;

namespace StepFunctionsApp.Converters
{
    public class TaskDefinition
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public object Parameters { get; set; } = null!;
        public string Description { get; set; } = "";
    }

    public class StateDefinition
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Resource { get; set; } = "";
        public object Parameters { get; set; } = null!;
        public string? NextState { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class WorkflowResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? StackTrace { get; set; }
        public string? WorkflowJson { get; set; }
        public List<MappingInfo>? Mappings { get; set; }
    }

    public class MappingInfo
    {
        public string OriginalTaskName { get; set; } = "";
        public string ConvertedStateId { get; set; } = "";
        public string ResourceUsed { get; set; } = "";
        public string DataSchema { get; set; } = "";
        public bool RequiresApproval { get; set; }
        public string PatternUsed { get; set; } = "";
    }

    /// <summary>
    /// Builder for converting SSIS packages to StepFunctions workflows.
    /// </summary>
    public static class ConversionBuilder
    {
        private static string _basePath = AppDomain.CurrentDomain.BaseDirectory;

        static ConversionBuilder()
        {
            ResourceRegistry.RegisterStandardResources();
        }

        public static WorkflowResult Build(
            string ssisPackagePath,
            string outputFlowId)
        {
            try
            {
                var xmlContent = File.ReadAllText(ssisPackagePath);
                var tasks = ExtractTasksFromSsis(xmlContent);
                
                var states = new List<StateDefinition>();
                foreach (var task in tasks)
                {
                    var pattern = GetMatchingPattern(task.Name);
                    if (pattern != null)
                    {
                        var state = CreateStateFromPattern(pattern, task);
                        states.Add(state);
                    }
                }
                
                var workflow = GenerateWorkflowDefinition(states, outputFlowId);
                var manifest = CreateManifest(ssisPackagePath, outputFlowId, tasks, states);
                
                return new WorkflowResult
                {
                    Success = true,
                    WorkflowJson = workflow,
                    Mappings = ConvertMappings(tasks, states)
                };
            }
            catch (Exception ex)
            {
                return new WorkflowResult
                {
                    Success = false,
                    Error = ex.Message,
                    StackTrace = ex.StackTrace
                };
            }
        }

        private static List<TaskDefinition> ExtractTasksFromSsis(string xmlContent)
        {
            // Placeholder - would parse actual SSIS XML in production
            // For now, returns sample tasks based on common patterns
            return new List<TaskDefinition>
            {
                new TaskDefinition { Name = "LoadTransactionData", Type = "Task" },
                new TaskDefinition { Name = "ValidateReferenceData", Type = "Task" },
                new TaskDefinition { Name = "ApplyCorrections", Type = "Task" }
            };
        }

        private static PatternDefinition? GetMatchingPattern(string taskName)
        {
            foreach (var pattern in WorkflowPatterns.GetAll())
            {
                if (pattern.Template.Type == taskName || 
                    pattern.Name.Contains(taskName))
                {
                    return pattern;
                }
            }
            return null;
        }

        private static StateDefinition CreateStateFromPattern(
            PatternDefinition pattern, TaskDefinition task)
        {
            var state = new StateDefinition
            {
                Id = task.Name,
                Type = pattern.Template.Type,
                Resource = pattern.Template.Resource,
                Parameters = pattern.Template.Parameters,
                Metadata = new Dictionary<string, object>
                {
                    ["phase"] = pattern.Metadata.Phase,
                    ["direction"] = pattern.Metadata.Direction,
                    /// ["requires_approval"] = pattern.Metadata.RequiresApproval
                }
            };
            return state;
        }

        private static string GenerateWorkflowDefinition(
            List<StateDefinition> states, string flowId)
        {
            var workflowDict = new Dictionary<string, object>();
            workflowDict["FlowId"] = flowId;
            workflowDict["startAt"] = states.Count > 0 ? states[0].Id : "LoadTransactionData";
            workflowDict["states"] = JsonSerializer.Serialize(states);
            return JsonSerializer.Serialize(workflowDict, new JsonSerializerOptions { WriteIndented = true });
        }

        private static Manifest CreateManifest(
            string ssisPath, string flowId, 
            List<TaskDefinition> tasks, List<StateDefinition> states)
        {
            return new Manifest
            {
                FlowId = flowId,
                SourcePackageName = Path.GetFileName(ssisPath),
                Mappings = ConvertMappings(tasks, states),
                ConversionDate = DateTime.UtcNow.ToString("o")
            };
        }

        private static List<MappingInfo> ConvertMappings(
            List<TaskDefinition> tasks, List<StateDefinition> states)
        {
            var mappings = new List<MappingInfo>();
            for (int i = 0; i < Math.Min(tasks.Count, states.Count); i++)
            {
                mappings.Add(new MappingInfo
                {
                    OriginalTaskName = tasks[i].Name,
                    ConvertedStateId = states[i].Id,
                    ResourceUsed = states[i].Resource,
                    DataSchema = GetSchemaForPattern(states[i]),
                    RequiresApproval = states[i].Metadata.ContainsKey("requires_approval"),
                    PatternUsed = ""
                });
            }
            return mappings;
        }

        private static string GetSchemaForPattern(StateDefinition state)
        {
            // Placeholder - would extract actual schema from pattern
            return "operation:string, table:string";
        }
    }

    public class Manifest
    {
        public string FlowId { get; set; } = "";
        public string SourcePackageName { get; set; } = "";
        public List<MappingInfo>? Mappings { get; set; }
        public string ConversionDate { get; set; } = "";
    }
}