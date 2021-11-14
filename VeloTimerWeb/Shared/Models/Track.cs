using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class Track : Entity
    {
        public string Name { get; set; }
        public double Length { get; set; }

        public IEnumerable<TimingLoop> TimingLoops { get; set; }
    }
}
