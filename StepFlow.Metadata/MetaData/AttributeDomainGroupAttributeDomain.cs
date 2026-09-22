using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.MetaData;

/// <summary>
/// This is the MANY-TO-MANY join table that "binds" an
/// AttributeDomain (e.g., "Data Sharing Domain")
/// to an AttributeDomainGroup (e.g., "CRM Passthrough Group").
/// </summary>
public class AttributeDomainGroupAttributeDomain
{
    public int AttributeDomainGroupAttributeDomainId { get; set; }

    // --- Foreign Key to the Group ---
    public int AttributeDomainGroupId { get; set; }
    public virtual AttributeDomainGroup AttributeDomainGroup { get; set; }

    // --- Foreign Key to the Domain ---
    public int AttributeDomainId { get; set; }
    public virtual AttributeDomain AttributeDomain { get; set; }

    /// <summary>
    /// This is the collection of all attributes that are
    /// part of this specific Group/Domain combination,
    /// along with their display order.
    /// </summary>
    public virtual ICollection<AttributeDomainGroupAttribute> OrderedAttributes { get; set; } = new List<AttributeDomainGroupAttribute>();
}