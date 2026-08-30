using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.MetaData
{
    public class SchemaRelation
    {
        public int SchemaRelationId { get; set; }

        /// <summary>
        /// A user-friendly name describing the nature of the relationship.
        /// e.g., "Contains", "Owns", "IsA"
        /// </summary>
        public string SchemaRelationName { get; set; }
        public string Description { get; set; }

        // --- Link to the Parent Schema ---
        public int ParentSchemaId { get; set; }
        public SchemaDefinition ParentSchema { get; set; }

        // --- Link to the Child Schema ---
        public int ChildSchemaId { get; set; }
        public SchemaDefinition ChildSchema { get; set; }

        // --- Optional: Linking Attributes for Joins ---

        /// <summary>
        /// The ID of the attribute on the ParentSchema that forms the link.
        /// </summary>
        public int ParentAttributeId { get; set; }
        public Entities.MetaData.EntityAttribute ParentAttribute { get; set; }

        /// <summary>
        /// The ID of the attribute on the ChildSchema that forms the link.
        /// </summary>
        public int ChildAttributeId { get; set; }
        public Entities.MetaData.EntityAttribute ChildAttribute { get; set; }
    }
}
