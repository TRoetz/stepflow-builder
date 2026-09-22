using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.MetaData;

public class AttributeDomainDataRowKey
{
    public int AttributeDomainDataRowKeyId { get; set; }
    public int AttributeDomainId { get; set; }
    public virtual AttributeDomain AttributeDomain { get; set; }

    // Navigation
    public virtual ICollection<AttributeValueData> Values { get; set; } = new List<AttributeValueData>();
    public int? EntityId { get; set; } // The LegalEntityId, OrchardId, etc.
    public string? EntityType { get; set; } // "LegalEntity", "Orchard", etc.

    public string CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime UpdatedOn { get; set; }
}