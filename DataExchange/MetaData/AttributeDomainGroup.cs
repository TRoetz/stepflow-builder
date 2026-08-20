using System.ComponentModel.DataAnnotations;

namespace StepFlow.DataModel.Entities.MetaData;

public class AttributeDomainGroup
{
    public int AttributeDomainGroupId { get; set; }
    public string Version { get; set; }
    public string AttributeDomainGroupName { get; set; }

    // Navigation to the join table
    public virtual ICollection<AttributeDomainGroupAttributeDomain> DomainAssociations { get; set; } =
        new List<AttributeDomainGroupAttributeDomain>();
}