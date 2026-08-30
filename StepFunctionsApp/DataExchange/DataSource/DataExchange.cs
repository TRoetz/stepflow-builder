using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class DataExchange : IConfigurationModel
    {
        public int DataExchangeId { get; set; }

        public string DataExchangeName { get; set; } 

        public int DataExchangeProviderId { get; set; }
        public DataExchangeProvider DataExchangeProvider { get; set; }
        // A DataExchange has many Profiles (versions)
        [NotMapped]
        public Dictionary<string, DataExchangeProfile> DataExchangeProfiles { get; set; } = new Dictionary<string, DataExchangeProfile>();
    }

}