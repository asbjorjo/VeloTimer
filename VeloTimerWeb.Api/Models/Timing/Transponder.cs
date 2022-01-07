using System.Collections.Generic;
using VeloTimer.Shared.Models.Timing;
using VeloTimerWeb.Api.Models.Riders;

namespace VeloTimerWeb.Api.Models.Timing
{
    public class Transponder
    {
        public long Id { get; set; }
        public string Label { get; set; }
        public string SystemId { get; set; }
        public TransponderType.TimingSystem TimingSystem { get; set; }
        public TransponderType TimingSystemRelation { get; set; }

        public ICollection<Passing> Passings { get; set; } = new List<Passing>();
        public ICollection<TransponderOwnership> Owners { get; set; } = new List<TransponderOwnership>();
    }
}