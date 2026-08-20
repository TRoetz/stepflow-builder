using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;


namespace StepFlow.DataModel.Entities.DataSource
{
    public class SchemaMap : IConfigurationModel
    {
        /// <summary>
        /// Gets or sets the UUID for this SchemaMap - also the reference to the specific configuration object UUID
        /// </summary>
        public int SchemaMapId { get; set; }
        /// <summary>
        /// Gets or sets the friendly Name for this SchemaMap
        /// </summary>
        public string SchemaMapName { get; set; }
        /// <summary>
        /// Gets or sets the version of this SchemaMap
        /// </summary>
        public string Version { get; set; }
        public int ActionSchemaMapId { get; set; }
        public ActionSchemaMap ActionSchemaMap { get; set; }
        public int TargetAttributeId { get; set; }
        public Entities.MetaData.EntityAttribute TargetAttribute { get; set; }
        public ICollection<EntityAttribute> SourceAttributes { get; set; } = new List<EntityAttribute>();
        public TransformType TransformType { get; set; }
        public MergeStrategy MergeStrategy { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }
}