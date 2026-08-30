using System.ComponentModel.DataAnnotations;

using StepFlow.DataModel.Entities.MetaData;

public class SchemaDefinition
{
    public int SchemaDefinitionId { get; set; }
    public string SchemaDefinitionName { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Definition { get; set; }
    public string DefinitionFrom { get; set; }
    public bool AttributeDomain { get; set; }
    public string CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    // Navigation
    public virtual ICollection<AttributeDomain> AttributeDomains { get; set; } = new List<AttributeDomain>();
    // For SchemaRelation, if this SchemaDefinition is a Parent or a Child in a relation
    public ICollection<SchemaRelation> ParentRelations { get; set; } = new List<SchemaRelation>();
    public ICollection<SchemaRelation> ChildRelations { get; set; } = new List<SchemaRelation>();
}