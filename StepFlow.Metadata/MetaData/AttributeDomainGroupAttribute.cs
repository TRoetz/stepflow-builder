using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.MetaData;

public class AttributeDomainGroupAttribute
{
    public int AttributeDomainGroupAttributeId { get; set; }

    // Foreign Key to Group
    public int AttributeDomainGroupId { get; set; }
    public virtual AttributeDomainGroup AttributeDomainGroup { get; set; }

    // Foreign Key to Attribute
    public int EntityAttributeId { get; set; }
    public virtual EntityAttribute EntitytAttribute { get; set; }

    // Foreign Key to AttributeDomainGroupAttributeDomain
    public int? AttributeDomainGroupAttributeDomainId { get; set; }
    public virtual AttributeDomainGroupAttributeDomain? AttributeDomainGroupAttributeDomain { get; set; }

    // Metadata for the attribute *within* this group
    public int DisplayOrder { get; set; }
    public bool Required { get; set; }
}