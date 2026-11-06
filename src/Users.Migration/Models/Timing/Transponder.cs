using System.Collections.Generic;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTime.Users.Migration.Models.Riders;

namespace VeloTime.Users.Migration.Models.Timing
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
