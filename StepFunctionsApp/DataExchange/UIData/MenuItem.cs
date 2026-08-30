
namespace StepFlow.DataModel.Entities.UiData
{
    public class MenuItem
    {
        public int MenuItemId { get; set; }
        public int MicrositeId { get; set; }
        public string Name { get; set; }
        public int? Order { get; set; }
        public int? ParentMenuItemId { get; set; }
        public string? Route { get; set; }
        public string? Icon { get; set; }
        public Microsite Microsite { get; internal set; }
    }
}
