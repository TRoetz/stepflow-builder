using System.ComponentModel.DataAnnotations;
using StepFlow.DataModel.Entities.MetaData;

namespace StepFlow.DataModel.Entities.UiData
{
    public class UISessionData
    {
        [Key]
        public int UISessionDataId { get; set; }

        [Required]
        public int UISessionId { get; set; }

        [Required]
        public string ElementId { get; set; } // Identifier for the UI element (e.g., "step1", "chart2")

        [Required]
        public string Data { get; set; } // JSON string representing the UI state

        public UISession UISession { get; set; }

        public int AttributeDomainId { get; set; } // Link to AttributeDomain
        public AttributeDomain UIMetaData { get; set; } // Navigation property
        
        [Required]
        public DateTime UIMetaDataLifeTime { get; set; } // Set this date in the future
        public string State { get; set; } // JSON string representing the UI state
    }

}
