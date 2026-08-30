using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace StepFlow.DataModel.Entities.UiData
{
    public class Microsite
    {
        public int MicrositeId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ICollection<MenuItem> MenuItems { get; set; }

        public Microsite()
        {
            MenuItems = new List<MenuItem>();
        }
    }
}
