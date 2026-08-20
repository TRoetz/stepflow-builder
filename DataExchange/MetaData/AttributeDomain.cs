using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.MetaData;

public class AttributeDomain
{
    
    public int AttributeDomainId { get; set; }
    public string Version { get; set; }
    public string AttributeDomainName { get; set; }
    public string Description { get; set; }

    // Foreign Key to SchemaDefinition
    public int SchemaDefinitionId { get; set; }
    public virtual SchemaDefinition SchemaDefinition { get; set; }

    /// <summary>
    /// If set, this version supersedes another version.
    /// Useful for tracking version history.
    /// </summary>
    public int? SupersedesAttributeDomainId { get; set; }
    public virtual AttributeDomain SupersedesAttributeDomain { get; set; }
    /// <summary>
    /// Whether this is the current active version.
    /// </summary>
    public bool IsCurrentVersion { get; set; } = true;

    // Navigation
    public virtual ICollection<EntityAttribute> Attributes { get; set; } = new List<EntityAttribute>();
    public virtual ICollection<AttributeDomainDataRowKey> Domain { get; set; } = new List<AttributeDomainDataRowKey>();
}