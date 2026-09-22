using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.DataSource
{

    public class DataExchangeProfile : IConfigurationModel
    {
        public int DataExchangeProfileId { get; set; }
        public string DataExchangeProfileName { get; set; }
        /// <summary>Stable file-based identity for this profile (used by dataexchange:// URIs). Defaults to a slug of the name.</summary>
        public string? ProfileId { get; set; }
        /// <summary>Optional StepFlow state-machine id that this profile is registered under (file monitor trigger).</summary>
        public string? FlowId { get; set; }
        public bool IsActive { get; set; } = true;
        public int DataExchangeId { get; set; }
        public DataExchange DataExchange { get; set; }
        public int DataSourceId { get; set; }
        public DataSource DataSource { get; set; }
        public int PipelineId { get; set; }
        public Pipeline Pipeline { get; set; }
    }
}