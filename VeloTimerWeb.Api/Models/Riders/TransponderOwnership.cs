using System;
using VeloTimerWeb.Api.Models.Timing;

namespace VeloTimerWeb.Api.Models.Riders
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
