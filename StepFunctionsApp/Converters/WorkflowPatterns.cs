using System;
using System.Text.Json;
using System.Collections.Generic;

namespace StepFunctionsApp.Converters
{
    /// <summary>
    /// Workflow pattern definitions for SSIS-to-StepFunctions conversions.
    /// Provides reusable templates for common workflow patterns.
    /// </summary>
    public static class WorkflowPatterns
    {
        private static Dictionary<string, PatternDefinition> _patterns = new();

        static WorkflowPatterns()
        {
            LoadPatternsFromJson();
        }

        private static void LoadPatternsFromJson()
        {
            var jsonPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Converters", "WorkflowPatterns.json");

            if (File.Exists(jsonPath))
            {
                var content = File.ReadAllText(jsonPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, PatternDefinition>>(content);
                foreach (var kvp in loaded ?? new Dictionary<string, PatternDefinition>())
                {
                    _patterns[kvp.Key] = kvp.Value;
                }
            }
        }

        public static PatternDefinition? Get(string name)
        {
            return _patterns.TryGetValue(name, out var pattern) ? pattern : null;
        }

        public static List<PatternDefinition> GetAll()
        {
            return _patterns.Values.ToList();
        }

        public static void Register(string name, PatternDefinition definition)
        {
            _patterns[name] = definition;
        }
    }

    public class PatternDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public TemplateTemplate Template { get; set; } = new();
        public MetadataMetadata Metadata { get; set; } = new();
    }

    public class TemplateTemplate
    {
        public string Type { get; set; } = "";
        public string Resource { get; set; } = "";
        public object Parameters { get; set; } = null!;
    }

    public class MetadataMetadata
    {
        public string Phase { get; set; } = "";
        public string Direction { get; set; } = "";
    }
}