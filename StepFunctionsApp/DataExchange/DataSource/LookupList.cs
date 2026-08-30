using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class LookupList
    {
        public int LookupListId { get; set; }
        public string LookupListName { get; set; } // e.g., "Days of the Week"

        // Storing as a simple CSV string is efficient for the database.
        public string AllowedValuesCsv { get; set; }

        [NotMapped]
        public List<string> AllowedValues
        {
            get => !string.IsNullOrEmpty(AllowedValuesCsv) ? AllowedValuesCsv.Split(',').ToList() : new List<string>();
            set => AllowedValuesCsv = string.Join(",", value);
        }
    }
}
