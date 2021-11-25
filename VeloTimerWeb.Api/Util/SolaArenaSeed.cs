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

            var track = new Track { Name = "Sola Arena", Length = 250, TimingLoops = timingloops };
            track = AddNewTrack(track);
            _context.SaveChanges();

            timingloops = _context.Set<TimingLoop>().Where(t => t.Track.Name == track.Name).ToList();

            var segment = new TrackSegment
            {
                Start = timingloops.Single(t => t.LoopId == 0),
                End = timingloops.Single(t => t.LoopId == 1),
            };
            AddNewSegment(segment);

            segment = new TrackSegment
            {
                Start = timingloops.Single(t => t.LoopId == 1),
                End = timingloops.Single(t => t.LoopId == 4),
            };
            AddNewSegment(segment);

            segment = new TrackSegment
            {
                Start = timingloops.Single(t => t.LoopId == 4),
                End = timingloops.Single(t => t.LoopId == 2),
            };
            AddNewSegment(segment);
            segment = new TrackSegment
            {
                Start = timingloops.Single(t => t.LoopId == 2),
                End = timingloops.Single(t => t.LoopId == 3),
            };
            AddNewSegment(segment);
            segment = new TrackSegment
            {
                Start = timingloops.Single(t => t.LoopId == 3),
                End = timingloops.Single(t => t.LoopId == 0 ),
            };
            AddNewSegment(segment);
            
            AddNewTimingSystem(new TransponderType { System = TransponderType.TimingSystem.Mylaps_X2 });

            _context.SaveChanges();

            AddInitialPassing();
            
            _context.SaveChanges();
        }

        private void AddInitialPassing()
        {
            var passing = _context.Set<Passing>().SingleOrDefault(p => p.SourceId == "6140844fda0ce99d3ae9c9a3");
            
            if (passing == null) 
            {
                var loop = _context.Set<TimingLoop>().SingleOrDefault(l => l.LoopId == 1 && l.Track.Name == "Sola Arena");
                var transponder = _context.Set<Transponder>().SingleOrDefault(t => t.SystemId == "115509543" && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2)
                ?? new Transponder { SystemId = "115509543", TimingSystem = TransponderType.TimingSystem.Mylaps_X2 };

                _context.Add(new Passing { Transponder = transponder, Loop = loop, SourceId = "6140844fda0ce99d3ae9c9a3", Time = DateTime.MinValue });
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
