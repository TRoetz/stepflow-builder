using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StepFunctionsApp.Converters
{
    /// <summary>
    /// Centralized resource registry for SSIS-to-StepFunctions conversions.
    /// Defines available services, APIs, and rule engines with schemas.
    /// </summary>
    public static class ResourceRegistry
    {
        private static Dictionary<string, ResourceDefinition> _registry { get; }
        = new();

        // Initialize with standard resources
        static ResourceRegistry()
        {
            RegisterStandardResources();
        }

        /// <summary>
        /// Register all standard resources used in conversions
        /// </summary>
        public static void RegisterStandardResources()
        {
            // Data transformation resources
            Register("transform://query", "DuckDB", 
                "Execute SQL queries for data transformations",
                new { operation = "string", table = "string", sql = "string" });
            
            Register("transform://insert", "DuckDB",
                "Insert transformed data into target tables",
                new { operation = "string", table = "string", data = "array" });
            
            Register("transform://update", "DuckDB",
                "Update records based on conditions",
                new { operation = "string", table = "string", where = "string", set = "object" });

            // Validation and rule engine resources
            Register("rule://validate", "RuleEngine",
                "Apply business validation rules to input data",
                new { rules = "array", input = "object", schema = "string" });
            
            Register("rule://match", "RuleEngine",
                "Match transactions against reference data",
                new { rules = "array", transaction = "object", refTable = "string" });
            
            Register("rule://correction", "RuleEngine",
                "Apply automatic corrections based on error patterns",
                new { rules = "array", errorType = "string", correctionData = "object" });

            // HTTP service resources
            Register("http://batch-service", "HTTP API",
                "Batch processing endpoint for bulk operations",
                new { method = "string", path = "string", body = "object" });
            
            Register("http://output-service", "HTTP API",
                "Output delivery endpoint (files, APIs, queues)",
                new { method = "string", path = "string", format = "string" });
            
            Register("http://validation-api", "HTTP API",
                "External validation service integration",
                new { url = "string", payload = "object" });

            // Sub-workflow resources
            Register("flow://validate-rates", "SubFlow",
                "Nested workflow for rate validation logic",
                new { flowId = "string", input = "object" });
            
            Register("flow://match-transactions", "SubFlow",
                "Nested workflow for transaction matching",
                new { flowId = "string", transactions = "array" });

            // Internal operation resources
            Register("internal://log", "Built-in",
                "Log execution events and metrics",
                new { level = "string", message = "string", data = "object" });
            
            Register("internal://metric", "Built-in",
                "Capture performance metrics",
                new { name = "string", value = "number", tags = "array" });
        }

        /// <summary>
        /// Register a custom resource
        /// </summary>
        public static void Register(
            string id,
            string type,
            string description,
            object schema)
        {
            _registry[id] = new ResourceDefinition
            {
                Id = id,
                Type = type,
                Description = description,
                Schema = JsonSerializer.Serialize(schema),
                IsActive = true
            };
        }

        /// <summary>
        /// Get resource by ID
        /// </summary>
        public static ResourceDefinition? Get(string id)
        {
            return _registry.TryGetValue(id, out var resource) ? resource : null;
        }

        /// <summary>
        /// List all registered resources
        /// </summary>
        public static List<ResourceDefinition> GetAll()
        {
            return _registry.Values.ToList();
        }

        /// <summary>
        /// Check if resource exists and is active
        /// </summary>
        public static bool Exists(string id)
        {
            return _registry.ContainsKey(id);
        }

        /// <summary>
        /// Save registry to file for persistence
        /// </summary>
        public static void SaveToFile(string path)
        {
            var jsonPath = Path.Combine(path, "resource_registry.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(_registry, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }

        /// <summary>
        /// Load registry from file
        /// </summary>
        public static void LoadFromFile(string path)
        {
            var jsonPath = Path.Combine(path, "resource_registry.json");
            if (File.Exists(jsonPath))
            {
                var content = File.ReadAllText(jsonPath);
                var loadedRegistry = JsonSerializer.Deserialize<Dictionary<string, ResourceDefinition>>(content);
                foreach (var kvp in loadedRegistry ?? new Dictionary<string, ResourceDefinition>())
                {
                    _registry[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    public class ResourceDefinition
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public string Schema { get; set; } = "";
        public bool IsActive { get; set; } = false;
    }
}