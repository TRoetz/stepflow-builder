using System.Data;

using StepFlow.DataModel.Entities.MetaData;

namespace StepFlow.DataModel.Entities.DataSource
{
    // Maps entities from ImportSchema to ActionSchema
    public class ActionSchemaMap
    {
        public int ActionSchemaMapId { get; set; }
        public string ActionSchemaMapName { get; set; }
        public string Version { get; set; }
        public int SourceSchemaId { get; set; }
        public AttributeDomain SourceSchema { get; set; }
        public int TargetSchemaId { get; set; }
        public AttributeDomain TargetSchema { get; set; }
        public ICollection<SchemaMap> AttributeMappings { get; set; } = new List<SchemaMap>(); 

    }
}