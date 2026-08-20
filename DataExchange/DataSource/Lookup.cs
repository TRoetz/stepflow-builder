using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class Lookup
    {
        public int LookupId { get; set; }
        public string LookupName { get; set; }
        public string Version { get; set; }
        public string Label { get; set; }
        public string SchemaVersion { get; set; }
        public int LookupVersion { get; set; }
        public string LookupEndpoint { get; set; }
        public LookupType Type { get; set; }
        public string QueryOrBodyTemplate { get; set; }
        public string ValueFieldToReturn { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }

    }
}