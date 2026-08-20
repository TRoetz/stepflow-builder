using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class ActionPipeline
    {
        public int ActionPipelineId { get; set; } // Primary key for the junction table
        public string ActionPipelineName { get; set; } = string.Empty;

        public int ParentActionId { get; set; } 
        public Action ParentAction { get; set; } = null!;

        public int SubPipelineId { get; set; }
        public Pipeline SubPipeline { get; set; } = null!;

        // Any other properties specific to this relationship, e.g., mapping parameters
    }
}
