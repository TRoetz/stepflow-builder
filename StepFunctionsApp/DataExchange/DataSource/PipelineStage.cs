using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class PipelineStage
    {
        public int PipelineStageId { get; set; } // PK for PipelineStage

        // Foreign Key to the parent Pipeline
        public int PipelineId { get; set; }
        public Pipeline Pipeline { get; set; } = null!;

        // The actual stage type (e.g., DataTreatment, PreRouting, etc.)
        public ActionStage StageType { get; set; } // Uses the ActionStage enum

        // Defines the execution order of the stages within the Pipeline
        public int ExecutionOrder { get; set; }

        // Collection of Actions belonging ONLY to this stage
        public ICollection<PipelineStageAction> PipelineStageActions { get; set; } = new List<PipelineStageAction>();
    }
}
