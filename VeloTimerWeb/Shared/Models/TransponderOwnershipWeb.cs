using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TransponderOwnershipWeb
    {
        public DateTimeOffset OwnedFrom { get; set; }
        public DateTimeOffset OwnedUntil { get; set; }
        public string TransponderLabel { get; set; }
        public string Owner { get; set; }
        public bool ShowEdit { get; set; } = false;
    }
}
