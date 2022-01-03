using Slugify;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

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

        public TrackWeb ToWeb()
        {
            var webLayouts = Layouts?.Select(x => TrackLayoutWeb.Create(x.Name, x.Slug, x.Distance));

            var web = TrackWeb.Create(Name, Slug, Length, webLayouts);

            return web;
        }
    }
}
