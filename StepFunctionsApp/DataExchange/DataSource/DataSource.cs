using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using StepFlow.DataModel.Entities.MetaData;

namespace StepFlow.DataModel.Entities.DataSource
{
    /// <summary>
    /// Represents a file that was used to import or export data
    /// </summary>
    public class DataSource
    {
        public int DataSourceId { get; set; }
        public string DataSourceName { get; set; }
        public DataSourceMediumType MediumType { get; set; }
        public string MediumConfigurationJson { get; set; }
        public int ImportSchemaId { get; set; }
        public AttributeDomain ImportSchema { get; set; }


    }
}