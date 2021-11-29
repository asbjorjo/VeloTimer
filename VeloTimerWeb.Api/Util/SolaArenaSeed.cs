using Microsoft.EntityFrameworkCore;
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
                    Label = "1800m"
                },
                new StatisticsItem
                {
                    Distance = 2000,
                    Label = "2080m"
                },
                new StatisticsItem
                {
                    Distance = 3000,
                    Label = "3800m"
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

            ICollection<TrackStatisticsItem> elements = new List<TrackStatisticsItem>
            {
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 200.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        }
                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 250.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        }
                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 500.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 6
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 7
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 8
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 9
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 10
                        }
                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 750.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 6
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 7
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 8
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 9
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 10
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 11
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 12
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 13
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 14
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 15
                        }
                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 1000.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 6
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 7
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 8
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 9
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 10
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 11
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 12
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 13
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 14
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 15
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 16
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 17
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 18
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 19
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 20
                        }
                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 2000.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 6
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 7
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 8
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 9
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 10
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 11
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 12
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 13
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 14
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 15
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 16
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 17
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 18
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 19
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 20
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 21
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 22
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 23
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 24
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 25
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 26
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 27
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 28
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 29
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 30
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 31
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 32
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 33
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 34
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 35
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 36
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 37
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 38
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 39
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 40
                        }

                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 3000.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 6
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 7
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 8
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 9
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 10
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 11
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 12
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 13
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 14
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 15
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 16
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 17
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 18
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 19
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 20
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 21
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 22
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 23
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 24
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 25
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 26
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 27
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 28
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 29
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 30
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 31
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 32
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 33
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 34
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 35
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 36
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 37
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 38
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 39
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 40
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 41
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 42
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 43
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 44
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 45
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 46
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 47
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 48
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 49
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 50
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 51
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 52
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 53
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 54
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 55
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 56
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 57
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 58
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 59
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 60
                        }
                    }
                },
                new TrackStatisticsItem
                {
                    StatisticsItem = _context.Set<StatisticsItem>().Single(s => s.Distance == 4000.0),
                    Segments = new List<TrackStatisticsSegment>
                    {
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 1
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 2
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 3
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 4
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 5
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 6
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 7
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 8
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 9
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 10
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 11
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 12
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 13
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 14
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 15
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 16
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 17
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 18
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 19
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 20
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 21
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 22
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 23
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 24
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 25
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 26
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 27
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 28
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 29
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 30
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 31
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 32
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 33
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 34
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 35
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 36
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 37
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 38
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 39
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 40
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 41
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 42
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 43
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 44
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 45
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 46
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 47
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 48
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 49
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 50
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 51
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 52
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 53
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 54
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 55
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 56
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 57
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 58
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 59
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 60
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 61
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 62
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 63
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 64
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 65
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 66
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 67
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 68
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 69
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 70
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 71
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 72
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 73
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 74
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 75
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Finish"),
                            Order = 76
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "200m"),
                            Order = 77
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Green"),
                            Order = 78
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "100m"),
                            Order = 79
                        },
                        new TrackStatisticsSegment
                        {
                            Segment = segments.Single(s => s.Start.Description == "Red"),
                            Order = 80
                        }
                    }
                },
            };
            foreach (var item in elements)
            {
                AddNewStatsElement(item);
            }
            _context.SaveChanges();
        }

        private void AddNewStatsElement(TrackStatisticsItem item)
        {
            var statselement = _context.Set<TrackStatisticsItem>().Include(s => s.Segments).SingleOrDefault(s => s.StatisticsItem.Distance == item.StatisticsItem.Distance);

            if (statselement == null)
            {
                _context.Add(item);
            } else if (statselement.Segments.Count() != item.Segments.Count())
            {
                statselement.Segments = item.Segments;
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
