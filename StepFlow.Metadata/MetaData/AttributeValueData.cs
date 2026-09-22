using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.MetaData;

public class AttributeValueData
{
    public int AttributeValueDataId { get; set; }

    /// <summary>
    /// The actual data, stored as a string.
    /// (e.g., "true", "Dwight's Kiwis", "42.5")
    /// </summary>
    public string Data { get; set; }

    // Foreign Key to the "row"
    public int AttributeDomainDataRowKeyId { get; set; }
    public virtual AttributeDomainDataRowKey AttributeDomainDataRowKey { get; set; }

    // Foreign Key to the "question"
    public int EntityAttributeId { get; set; }
    public virtual EntityAttribute Attribute { get; set; }

    // Denormalized keys for easier querying
    public int AttributeDomainId { get; set; }
    public int? AttributeGroupKeyId { get; set; }
}