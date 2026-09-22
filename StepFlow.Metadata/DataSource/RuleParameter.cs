using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class RuleParameter
    {
        public int RuleParameterId { get; set; }

        public int RuleId { get; set; } // Foreign key to Rule
        public Rule Rule { get; set; } // Navigation property

        public string Name { get; set; } // The name used in the placeholder, e.g., "param1"
        public AttributeDataType ExpectedDataType { get; set; }

        // Optional: Link to a list of allowed literal values.
        public int? LookupListId { get; set; }
        public LookupList? LookupList { get; set; }
    }
}
