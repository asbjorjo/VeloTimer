using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class AdminDashboardModel
    {
        public List<DateTimeOffset> Labels { get; set; }
        public List<double> LapCounts { get; set; }
        public List<double> RiderCounts { get; set; }
    }
}
