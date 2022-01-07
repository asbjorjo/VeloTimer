using System.Collections.Generic;

namespace VeloTimerWeb.Api.Models.Riders
{
    public class Rider
    {
        public long Id { get; private set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool IsPublic { get; set; }

        public string UserId { get; set; }
        public IEnumerable<TransponderOwnership> Transponders { get; set; } = new List<TransponderOwnership>();
    }
}
