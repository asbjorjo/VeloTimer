using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class Transponder
    {
        public long Id { get; set; }
        public string Label { get; set; }
        public string SystemId { get; set; }
        public TransponderType.TimingSystem TimingSystem { get; set; }
        public TransponderType TimingSystemRelation { get; set; }

        public IEnumerable<Passing> Passings { get; set; }
        public IEnumerable<TransponderOwnership> Owners { get; set; }
    }
}