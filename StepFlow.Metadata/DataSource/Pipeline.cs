using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class Pipeline
    {
        public int PipelineId { get; set; }
        public string PipelineName { get; set; }
        public string Description { get; set; }

        // REMOVED: public string PipelineStagesCsv { get; set; } // <--- DROP THIS FIELD

        // NEW: Collection of PipelineStage entities
        public ICollection<PipelineStage> PipelineStages { get; set; } = new List<PipelineStage>();
    }
}
