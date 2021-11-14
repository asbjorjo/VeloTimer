using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TransponderType
    {
        public enum TimingSystem
        {
            Mylaps_X2
        }

        public TimingSystem System { get; set; }
    }
}
