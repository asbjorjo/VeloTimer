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

            timingloops = _context.TimingLoops.Where(t => t.Track.Name == track.Name).ToList();

            var segment = new Segment 
            { 
                Label = "Lap", 
                Start = timingloops.Single(t => t.Distance == 250), 
                End = timingloops.Single(t => t.Distance == 250), 
                DisplayIntermediates = false,
                RequireIntermediates = true,
                MinTime = 5, 
                MaxTime = 300 
            };
            segment.Intermediates = timingloops.Except(timingloops.Where(t => t.Distance == 250)).Select(t => new Intermediate { Loop = t, Segment = segment}).ToList();
            AddNewSegment(segment);

            segment = new Segment
            {
                Label = "200m",
                Start = timingloops.Single(t => t.Distance == 50),
                End = timingloops.Single(t => t.Distance == 250),
                DisplayIntermediates = true,
                RequireIntermediates = true,
                MinTime = 9,
                MaxTime = 15
            };
            segment.Intermediates = timingloops.Where(t => (t.Distance == 150)).Select(t => new Intermediate { Loop = t, Segment = segment }).ToList();
            AddNewSegment(segment);

            segment = new Segment
            {
                Label = "Red pursuit",
                Start = timingloops.Single(t => t.Description == "Red"),
                End = timingloops.Single(t => t.Description == "Red"),
                DisplayIntermediates = true,
                RequireIntermediates = true,
                MinTime = 6,
                MaxTime = 30
            };
            segment.Intermediates = timingloops.Where(t => (t.Description == "Green")).Select(t => new Intermediate { Loop = t, Segment = segment }).ToList();
            AddNewSegment(segment);
            segment = new Segment
            {
                Label = "Green pursuit",
                Start = timingloops.Single(t => t.Description == "Green"),
                End = timingloops.Single(t => t.Description == "Green"),
                DisplayIntermediates = true,
                RequireIntermediates = true,
                MinTime = 6,
                MaxTime = 30
            };
            segment.Intermediates = timingloops.Where(t => (t.Description == "Red")).Select(t => new Intermediate { Loop = t, Segment = segment }).ToList();
            AddNewSegment(segment);

            AddNewTimingSystem(new TransponderType { System = TransponderType.TimingSystem.Mylaps_X2 });

            _context.SaveChanges();

            AddInitialPassing();
            
            _context.SaveChanges();
        }

        private void AddInitialPassing()
        {
            var passing = _context.Set<Passing>().SingleOrDefault(p => p.Source == "6140844fda0ce99d3ae9c9a3");
            
            if (passing == null) 
            {
                var loop = _context.Set<TimingLoop>().SingleOrDefault(l => l.LoopId == 1 && l.Track.Name == "Sola Arena");
                var transponder = _context.Set<Transponder>().SingleOrDefault(t => t.SystemId == "115509543" && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2)
                ?? new Transponder { SystemId = "115509543", TimingSystem = TransponderType.TimingSystem.Mylaps_X2 };

                _context.Add(new Passing { Transponder = transponder, Loop = loop, Source = "6140844fda0ce99d3ae9c9a3", Time = DateTimeOffset.MinValue });
            }
        }

        private Track AddNewTrack(Track track)
        {
            var existing = _context.Tracks.SingleOrDefault(t => t.Name == track.Name);
            if (existing == null)
            {
                _context.Add(track);
                existing = track;
            }
            return existing;
        }

        private void AddNewSegment(Segment segment)
        {
            if (_context.Segments.SingleOrDefault(s => s.Start == segment.Start && s.End == segment.End) == null)
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
