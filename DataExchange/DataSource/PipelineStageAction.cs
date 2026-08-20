using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class PipelineStageAction
    {
        public int PipelineStageActionId { get; set; }
        public int PipelineStageId { get; set; }
        public PipelineStage PipelineStage { get; set; } = null!;

        public int ActionId { get; set; }
        public Action Action { get; set; } = null!;
        public int ExecutionOrder { get; set; }
    }
}
