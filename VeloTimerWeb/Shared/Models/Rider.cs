using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class Rider
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public string UserId { get; set; }
        public IEnumerable<TransponderOwnership> Transponders { get; set; }
    }
}
