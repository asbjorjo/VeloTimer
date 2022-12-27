using Slugify;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloTimerWeb.Api.Models.Timing;

namespace VeloTimerWeb.Api.Models.TrackSetup
{
    public class TrackLayout
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public Track Track { get; set; }
        public ICollection<TrackLayoutSector> Sectors { get; private set; } = new List<TrackLayoutSector>();
        public double Distance { get; set; }
        public string Slug { get; set; }
        public bool Active { get; set; } = false;

        public static TrackLayout Create(Track track, string name, IOrderedEnumerable<TrackSector> sectors)
        {
            var slugHelper = new SlugHelper();

            TrackLayout trackLayout = new()
            {
                Name = name,
                Track = track,
                Slug = slugHelper.GenerateSlug(name)
            };

            int order = 1;
            foreach (var sector in sectors)
            {
                var layoutSegment = TrackLayoutSector.Create(trackLayout, sector, order);
                trackLayout.Sectors.Add(layoutSegment);
                order++;
            }

            trackLayout.Distance = trackLayout.Sectors.Sum(x => x.Sector.Length);

            return trackLayout;
        }
    }

    public class TrackLayoutSector
    {
        public long Id { get; private set; }
        public TrackLayout Layout { get; private set; }
        public TrackSector Sector { get; private set; }
        public int Order { get; private set; }
        public bool Intermediate { get; private set; }

        public static TrackLayoutSector Create(TrackLayout layout, TrackSector sector, int order)
        {
            var layoutsector = new TrackLayoutSector
            {
                Layout = layout,
                Sector = sector,
                Order = order
            };

            return layoutsector;
        }
    }

    public class TrackLayoutPassing
    {
        public long Id { get; private set; }
        public TrackLayout TrackLayout { get; private set; }
        public Transponder Transponder { get; private set; }
        public ICollection<TrackSectorPassing> Passings { get; private set; } = new List<TrackSectorPassing>();

        public double Time { get; private set; }
        public double Speed { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }

        public static TrackLayoutPassing Create(TrackLayout layout, Transponder transponder, IEnumerable<TrackSectorPassing> passings)
        {
            var orderedPassings = passings.OrderBy(x => x.StartTime);

            var layoutPassing = new TrackLayoutPassing
            {
                TrackLayout = layout,
                Transponder = transponder,
                Passings = passings.ToList(),
                Time = orderedPassings.Sum(x => x.Time),
                StartTime = orderedPassings.First().StartTime,
                EndTime = orderedPassings.Last().EndTime
            };
            layoutPassing.Speed = layout.Distance / layoutPassing.Time;

            return layoutPassing;
        }
    }
}
