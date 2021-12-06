﻿using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Util
{
    public class SolaArenaSeed
    {
        private readonly VeloTimerDbContext _context;
        private readonly IPassingService _passingService;

        public SolaArenaSeed(VeloTimerDbContext context, IPassingService passingService)
        {
            _context = context;
            _passingService = passingService;
        }

        public void SeedData()
        {
            ICollection<TimingLoop> timingloops = new List<TimingLoop>
            {
                new TimingLoop
                {
                    Description = "Finish",
                    Distance = 250,
                    LoopId = 0
                },
                new TimingLoop
                {
                    Description = "200m",
                    Distance = 50,
                    LoopId = 1
                },
                new TimingLoop
                {
                    Description = "100m",
                    Distance = 150,
                    LoopId = 2
                },
                new TimingLoop
                {
                    Description = "Red",
                    Distance = 235,
                    LoopId = 3
                },
                new TimingLoop
                {
                    Description = "Green",
                    Distance = 110,
                    LoopId = 4
                }
            };

            var track = new Track { Name = "Sola Arena", Length = 250, };
            track.TimingLoops.AddRange(timingloops);
            track = AddNewTrack(track);
            _context.SaveChanges();

            timingloops = _context.Set<TimingLoop>().Where(t => t.Track.Name == track.Name).ToList();

            var segment = new TrackSegment(Start: timingloops.Single(t => t.LoopId == 0), End: timingloops.Single(t => t.LoopId == 1));
            AddNewSegment(segment);
            segment = new TrackSegment(Start: timingloops.Single(t => t.LoopId == 1), End: timingloops.Single(t => t.LoopId == 4));
            AddNewSegment(segment);
            segment = new TrackSegment(Start: timingloops.Single(t => t.LoopId == 4), End: timingloops.Single(t => t.LoopId == 2));
            AddNewSegment(segment);
            segment = new TrackSegment(Start: timingloops.Single(t => t.LoopId == 2), End: timingloops.Single(t => t.LoopId == 3));
            AddNewSegment(segment);
            segment = new TrackSegment(Start: timingloops.Single(t => t.LoopId == 3), End: timingloops.Single(t => t.LoopId == 0 ));
            AddNewSegment(segment);
            
            AddNewTimingSystem(new TransponderType { System = TransponderType.TimingSystem.Mylaps_X2 });

            _context.SaveChanges();

            AddInitialPassing();
            
            _context.SaveChanges();


            ICollection<StatisticsItem> stats = new List<StatisticsItem> {
                new StatisticsItem
                {
                    Distance = 200,
                    Label = "200m"
                },
                new StatisticsItem
                {
                    Distance = 250,
                    Label = "250m"
                },
                new StatisticsItem
                {
                    Distance = 500,
                    Label = "500m"
                },
                new StatisticsItem
                {
                    Distance = 750,
                    Label = "750m"
                },
                new StatisticsItem
                {
                    Distance = 1000,
                    Label = "1000m"
                },
                new StatisticsItem
                {
                    Distance = 1500,
                    Label = "1500m"
                },
                new StatisticsItem
                {
                    Distance = 2000,
                    Label = "2000m"
                },
                new StatisticsItem
                {
                    Distance = 3000,
                    Label = "3000m"
                },
                new StatisticsItem
                {
                    Distance = 4000,
                    Label = "4000m"
                },
            };
            foreach (var item in stats)
            {
                AddNewStatsItem(item);
            }
            _context.SaveChanges();

            var segments = _context.Set<TrackSegment>().ToList();

            ICollection<TrackLayout> Layouts = new List<TrackLayout>
            {
                TrackLayout.Create(track, "200m", segments.Where(x => x.Start.Distance != 250.0).OrderBy(x => !(x.Start.Distance >= 50)).ThenBy(x => x.End.Distance)),
                TrackLayout.Create(track, "Runde", segments.OrderBy(x => !(x.Start.Distance > 0)).ThenBy(x => x.End.Distance)),
                TrackLayout.Create(track, "Pursuit bakside", segments.OrderBy(x => !(x.Start.Distance >= 110)).ThenBy(x => x.Start.Distance)),
                TrackLayout.Create(track, "Pursuit framside", segments.OrderBy(x => !(x.Start.Distance >= 235)).ThenBy(x => x.Start.Distance))
            };

            foreach (var layout in Layouts)
            {
                AddNewLayout(layout);
            }
            _context.SaveChanges();

            ICollection<TrackStatisticsItem> trackStatisticsItems = new List<TrackStatisticsItem>
            {
                TrackStatisticsItem.Create(_context.Set<StatisticsItem>().Single(s => s.Distance == 200.0), Layouts.Single(x => x.Name == "200m"), 1),
            };

            var laps = new int[] { 1, 2, 3, 4, 6, 8, 12, 16};
            foreach (var layout in Layouts.Where(x => x.Name != "200m"))
            {
                foreach (var lap in laps)
                {
                    var statsItem = _context.Set<StatisticsItem>().Single(x => x.Distance == 250.0 * lap);
                    trackStatisticsItems.Add(TrackStatisticsItem.Create(statsItem, layout, lap));
                }
            }

            foreach (var tsi in trackStatisticsItems)
            {
                AddNewTrackStatistics(tsi);
            }
            _context.SaveChanges();
        }

        private void AddNewTrackStatistics(TrackStatisticsItem tsi)
        {
            var existing = _context.Set<TrackStatisticsItem>().SingleOrDefault(x => x.Layout == tsi.Layout && x.StatisticsItem == tsi.StatisticsItem);
            if (existing == null)
            {
                _context.Add(tsi);
            }
        }

        private void AddNewLayout(TrackLayout layout)
        {
            var existing = _context.Set<TrackLayout>().SingleOrDefault(x => x.Track == layout.Track && x.Name == layout.Name);
            if (existing == null)
            {
                _context.Add(layout);
            } 
        }

        private void AddNewStatsItem(StatisticsItem item)
        {
            var statsitem = _context.Set<StatisticsItem>().SingleOrDefault(s => s.Distance == item.Distance);

            if (statsitem == null)
            {
                _context.Add(item);
            }
        }

        private void AddInitialPassing()
        {
            var passing = _context.Set<Passing>().SingleOrDefault(p => p.SourceId == "617ac814b7c51f1d4a6da859");
            
            if (passing == null) 
            {
                var loop = _context.Set<TimingLoop>().SingleOrDefault(l => l.LoopId == 1 && l.Track.Name == "Sola Arena");
                var transponder = _context.Set<Transponder>().SingleOrDefault(t => t.SystemId == "115458410" && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2)
                ?? new Transponder { SystemId = "115458410", TimingSystem = TransponderType.TimingSystem.Mylaps_X2 };

                _context.Add(new Passing { Transponder = transponder, Loop = loop, SourceId = "617ac814b7c51f1d4a6da859", Time = DateTime.MinValue });
            }
        }

        private Track AddNewTrack(Track track)
        {
            var existing = _context.Set<Track>().SingleOrDefault(t => t.Name == track.Name);
            if (existing == null)
            {
                _context.Add(track);
                existing = track;
            }
            return existing;
        }

        private void AddNewSegment(TrackSegment segment)
        {
            if (_context.Set<TrackSegment>().SingleOrDefault(s => s.Start == segment.Start && s.End == segment.End) == null)
            {
                _context.Add(segment);
            }
        }

        private void AddNewTimingSystem(TransponderType system)
        {
            if (_context.Set<TransponderType>().SingleOrDefault(t => t.System == system.System) == null)
            {
                _context.Add(system);
            }
        }
    }
}
