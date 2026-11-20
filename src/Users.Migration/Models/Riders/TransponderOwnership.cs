using System;
using VeloTime.Users.Migration.Models.Timing;

namespace VeloTime.Users.Migration.Models.Riders
{
    public class TransponderOwnership
    {
        public long Id { get; set; }

        public Transponder Transponder { get; set; }
        public Rider Owner { get; set; }
        public DateTime OwnedFrom { get; set; }
        public DateTime? OwnedUntil { get; set; }
    }
}
