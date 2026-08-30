using System.ComponentModel.DataAnnotations;



namespace StepFlow.DataModel.Entities.UiData
{
    public class UISession
    {
        [Key]
        public int UISessionId { get; set; }

        [Required]
        public string UserIdentifier { get; set; } // Could be username, email, or user ID

        [Required]
        public string UIContext { get; set; } // e.g., "WizardA", "DashboardB", "FormC"

        [Required]
        public DateTime SessionStart { get; set; }

        public DateTime? SessionEnd { get; set; }

        public ICollection<UISessionData> UISessionData { get; set; }


        public UISession()
        {
            UISessionData = new List<UISessionData>();
        }
    }

}
