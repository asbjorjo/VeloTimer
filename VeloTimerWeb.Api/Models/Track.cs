using Slugify;
using System.Collections.Generic;

namespace VeloTimerWeb.Api.Models
{
    public class Track
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public double Length { get; set; }
        public string Slug { get; set; }

        public List<TrackLayout> Layouts { get; private set; } = new();
        public List<TimingLoop> TimingLoops { get; private set; } = new();

        public static Track Create(string name, double length)
        {
            var slugHelper = new SlugHelper();
            return new Track
            {
                Name = name,
                Length = length,
                Slug = slugHelper.GenerateSlug(name)
            };
        }
    }
}
