using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StepFlow.DataModel.Entities.DataSource;

namespace StepFlow.DataModel.Entities.MetaData
{

    public class EntityAttribute
    {
        public int EntityAttributeId { get; set; }
        public bool PrimaryKey { get; set; }
        public string Version { get; set; }

        /// <summary>
        /// The internal key or OData field name.
        /// Example: "dsl_nzkgi", "SoilMoistureLevel"
        /// </summary>
        public string AttributeName { get; set; }

        /// <summary>
        /// The detailed description of the attribute's purpose.
        /// Example: "New Zealand Kiwifruit Growers..."
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The primitive data type for this attribute.
        /// This REPLACES the AttributeType table.
        /// </summary>
        public AttributeDataType DataType { get; set; }

        /// <summary>
        /// Stores complex validation rules (min, max, required, etc.)
        /// as a JSON string. This corresponds to the 'metadata' object.
        /// Example: "{\"required\": true, \"minimum\": 0.0, ...}"
        /// -- REFACTOR THIS IF REQUIRED GUYS!
        /// </summary>
        public string ValidationSchemaJson { get; set; }

        /// <summary>
        /// The friendly, user-facing display name for the attribute.
        /// Example: "NZKGI", "Trading as", "Soil Moisture Level"
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Placeholder text to show in an empty input field.
        /// Example: "Enter Soil Moisture Level", "YYYY-MM-DD"
        /// </summary>
        public string Placeholder { get; set; }

        /// <summary>
        /// Help text or a tooltip to display next to the field.
        /// Example: "Percentage value of soil moisture (0-100%)"
        /// </summary>
        public string HelpText { get; set; }

        /// <summary>
        /// Whether the field should be visible in the UI.
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Whether the field is read-only in the UI.
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        // --- End of UiSchema properties ---

        // Foreign Key to AttributeDomain
        public int AttributeDomainId { get; set; }

        public virtual AttributeDomain AttributeDomain { get; set; }

        // Navigation
        public virtual ICollection<AttributeValueData> AttributeValueData { get; set; } = new List<AttributeValueData>();

        public ICollection<Rule> Rules { get; set; } = new List<Rule>();
    }
}