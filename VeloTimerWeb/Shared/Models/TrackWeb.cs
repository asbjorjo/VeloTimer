using System.Collections.Generic;
using System.Linq;

namespace VeloTimer.Shared.Models
{
    public class TrackWeb
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public double Length { get; set; }
        public IEnumerable<TrackLayoutWeb> Layouts { get; set; } = Enumerable.Empty<TrackLayoutWeb>();
    }

    public class TrackLayoutWeb
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public double Length { get; set; }
        public TrackWeb Track { get; set; }
    }
}
