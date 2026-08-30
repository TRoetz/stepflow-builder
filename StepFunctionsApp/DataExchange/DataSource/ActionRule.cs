namespace StepFlow.DataModel.Entities.DataSource
{
    public class ActionRule
    {
        public int ActionRuleId { get; set; }

        // --- Relationships ---
        public int ActionId { get; set; }
        public Action Action { get; set; }

        public int RuleId { get; set; }
        public Rule Rule { get; set; }
        public string ParameterMappingsJson { get; set; }
        /// <summary>
        /// The ID of the Attribute to which the rule's output value will be assigned.
        /// This is nullable; if null, the rule acts only as a validator.
        /// </summary>
        public int? OutputTargetAttributeId { get; set; }

        /// <summary>
        /// The strategy for applying the output value to the data row.
        /// </summary>
        public MergeStrategy OutputMergeStrategy { get; set; } = MergeStrategy.OverwriteExisting;
        /// <summary>Name-based target attribute (preferred over OutputTargetAttributeId for file-based profiles).</summary>
        public string? OutputTargetAttributeName { get; set; }
    }
}
