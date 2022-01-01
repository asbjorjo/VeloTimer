using System.Collections.Generic;
using VeloTimer.Shared.Util;

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

        public TransponderWeb ToWeb()
        {
            return new TransponderWeb
            {
                Label = TransponderIdConverter.IdToCode(long.Parse(SystemId)),
                SystemId = SystemId,
                TimingSystem = TimingSystem.ToString()
            };
        }
    }
}