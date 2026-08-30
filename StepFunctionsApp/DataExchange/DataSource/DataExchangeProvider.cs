using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class DataExchangeProvider
    {
        public int DataExchangeProviderId { get; set; }
        public string DataExchangeProviderName { get; set; } // e.g., "LabCorp", "AgriSource"
        public string Description { get; set; }

        // A Provider has many types of DataExchanges
        public ICollection<DataExchange> DataExchanges { get; set; } = new List<DataExchange>();
    }
}
