using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class Transponder : Entity
    {
        public string Label { get; set; }
        public string SystemId { get; set; }
        public TransponderType.TimingSystem TimingSystem { get; set; }
        public TransponderType TimingSystemRelation { get; set; }

        public ICollection<Passing> Passings { get; set; }
        public ICollection<TransponderName> Names { get; set; }
        public ICollection<TransponderOwnership> Owners { get; set; }
    }
}