using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Data.Models
{
    public class AdminDashboardModel
    {
        public int RiderCount { get; set; }
        public int RiderPublicCount { get; set; }
        public int RiderWithTransponderCount { get; set; }
        public int TransponderCount { get; set; }
        public int TransponderWithOwnerCount { get; set; }
        public int LapCount { get; set; }
        public int PassingCount { get; set; }
        public List<DateTimeOffset> Labels { get; set; }
        public List<double> LapCounts { get; set; }
        public List<double> RiderCounts { get; set; }
    }
}
