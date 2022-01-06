using System;

namespace VeloTimer.Shared.Models
{
    public class TransponderOwnershipWeb
    {
        public DateTimeOffset OwnedFrom { get; set; }
        public DateTimeOffset OwnedUntil { get; set; }
        public TransponderWeb Transponder { get; set; }
        public RiderWeb Owner { get; set; }
        public bool ShowEdit { get; set; } = false;
    }
}
