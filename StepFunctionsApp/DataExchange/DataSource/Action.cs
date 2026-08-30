using System.ComponentModel.DataAnnotations;

using StepFlow.DataModel.Entities.MetaData;

namespace StepFlow.DataModel.Entities.DataSource
{
    // Each treatment of an entity in the customer CSV file has rules that it needs to apply to match this treatment.
    // * ActionSchema <-- ActionSchemaMap <-- ImportSchema
    public class Action
    {
        /// <summary>
        /// Gets or sets the UUID for this Action
        /// </summary>
        public int ActionId { get; set; }

        // Actions could be 3rd party Actions -- For Example: 
        // Based on a ruleset we need to send these values in a ActionSchema to CRM or some downstream system
        // or via Orchastrator -- Camunda

        // Example: UpdateCRM = "Grower" & m_sState & "01"
        public string ActionName { get; set; } = "DEFAULT";

        public ActionType Type { get; set; }
        public string? OutputParameterName { get; set; }
        public int? LookupId { get; set; }
        public Lookup? Lookup { get; set; }

        public string? LookupParameterMappingJson { get; set; }

        //  How should the fields from the lookup's result be mapped back into the data row?
        //  This brings back your "replace" vs "add new" concept.
        //  JSON: { "ProductName": { "TargetAttributeId": "guid-for-our-product-name", "MergeStrategy": "OverwriteExisting" },
        //          "StockLevel": { "TargetAttributeId": "guid-for-our-stock-level", "MergeStrategy": "AddNewOnly" } }
        public string? LookupResultMappingJson { get; set; }


        // --- CONTEXT AND RULES ---

        // The schema of the data this action operates on.
        public int? InputSchemaId { get; set; }
        public AttributeDomain? InputSchema { get; set; }

        // The output schema 
        public int? ActionSchemaId { get; set; }
        public AttributeDomain? ActionSchema { get; set; }

        // The contextual business rules for this action.
        public ICollection<ActionRule> ActionRules { get; set; } = new List<ActionRule>();

        // --- TYPE-SPECIFIC CONFIGURATION ---

        // ONLY relevant if Type is 'Transformation'.
        public int? ActionSchemaMapId { get; set; }
        public ActionSchemaMap? SchemaMap { get; set; }

        // ONLY relevant if Type is 'EnrichmentLookup' or 'Dispatch'.
        public int? ActionEndpointId { get; set; }
        public ActionEndpoint? Endpoint { get; set; }

        // --- NAVIGATION PROPERTIES FOR JUNCTION TABLES ---

        // An Action can be part of many DataSources' pipelines
        public ICollection<PipelineStageAction> PipelineStageActions { get; set; } = new List<PipelineStageAction>();

        // An Action can trigger many SubPipelines (DataSources)
        public ICollection<ActionPipeline> SubPipelines { get; set; } = new List<ActionPipeline>();

        /// <summary>Free-form action options (e.g. Dispatch "Filter" SQL boolean, "Batch", "Method"; enrichment "Method").</summary>
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    }
}