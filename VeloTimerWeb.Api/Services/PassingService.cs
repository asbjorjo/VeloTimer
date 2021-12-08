using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Hub;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Hubs;
using static VeloTimer.Shared.Models.TransponderType;

namespace VeloTimerWeb.Api.Services
{
    public class PassingService : IPassingService
    {
        private readonly VeloTimerDbContext _context;
        private readonly IHubContext<PassingHub, IPassingClient> _hubContext;
        private readonly ILogger<PassingService> _logger;

        public PassingService(
            VeloTimerDbContext context,
            IHubContext<PassingHub, IPassingClient> hubContext,
            ILogger<PassingService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<Passing> RegisterNew(Passing passing, TimingSystem? timingSystem, string transponderId)
        {
            if (timingSystem.HasValue && transponderId != null)
            {
                var transponder = await _context.Set<Transponder>()
                    .Where(t => t.TimingSystem == timingSystem && t.SystemId == transponderId)
                    .SingleOrDefaultAsync();

                if (transponder == null)
                {
                    transponder = new Transponder
                    {
                        SystemId = transponderId,
                        TimingSystem = timingSystem.Value
                    };

                    _context.Add(transponder);
                }

                passing.Transponder = transponder;
            }

            _context.Add(passing);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group($"timingloop_{passing.Loop.Id}").NewPassings();
            await _hubContext.Clients.Group($"transponder_{passing.Transponder.Id}").NewPassings();

            var transponderPassing = await RegisterTrackSegmentPassing(passing);

            if (transponderPassing != null) {
                var layoutquery = _context.Set<TrackLayout>()
                    .Where(x => x.Segments.OrderByDescending(x => x.Order).First().Segment.End == passing.Loop)
                    .OrderByDescending(x => x.Segments.Count)
                    .Include(x => x.Segments)
                        .ThenInclude(x => x.Segment);

                var layouts = await layoutquery.ToListAsync();

                if (layouts.Any())
                {
                    var transponderPassings = await _context.Set<TrackSegmentPassing>()
                        .Where(x => x.Transponder == passing.Transponder && x.EndTime <= passing.Time)
                        .Include(x => x.TrackSegment)
                        .OrderByDescending(x => x.EndTime)
                        .Take(layouts.First().Segments.Count)
                        .OrderBy(x => x.EndTime)
                        .ToListAsync();

                    foreach (var layout in layouts)
                    {
                        if (layout.Segments.Count <= transponderPassings.Count)
                        {
                            var relevantPassings = transponderPassings.TakeLast(layout.Segments.Count);
                            var segments = layout.Segments.OrderBy(x => x.Order);

                            var continuouslayout = relevantPassings.Select(x => x.TrackSegment.Id).SequenceEqual(segments.Select(x => x.Segment.Id));
                            var previousrelevant = relevantPassings.First();

                            foreach (var relevantPassing in relevantPassings.Skip(1))
                            {
                                if (previousrelevant.EndTime != relevantPassing.StartTime)
                                {
                                    continuouslayout = false;
                                }
                                previousrelevant = relevantPassing;
                            }

                            if (continuouslayout)
                            {
                                var layoutPassing = TrackLayoutPassing.Create(layout, passing.Transponder, relevantPassings);
                                _context.Add(layoutPassing);

                                var statsItems = await _context.Set<TrackStatisticsItem>()
                                    .Where(x => x.Layout == layout)
                                    .OrderByDescending(x => x.Laps)
                                    .ToListAsync();

                                if (statsItems.Any())
                                {
                                    var layoutPassings = await _context.Set<TrackLayoutPassing>()
                                        .Where(x => 
                                            x.TrackLayout == layout 
                                            && x.Transponder == passing.Transponder 
                                            && x.EndTime < passing.Time)
                                        .OrderByDescending(x => x.EndTime)
                                        .Take(statsItems.First().Laps - 1)
                                        .OrderBy(x => x.EndTime)
                                        .ToListAsync();
                                    layoutPassings.Add(layoutPassing);

                                    foreach (var item in statsItems)
                                    {
                                        if (item.Laps <= layoutPassings.Count)
                                        {
                                            var laps = layoutPassings.TakeLast(item.Laps);
                                            bool continuouslap = true;

                                            if (item.Laps > 1)
                                            {
                                                var previouslappass = laps.First();
                                                foreach (var lap in laps.Skip(1))
                                                {
                                                    if (previouslappass.EndTime != lap.StartTime)
                                                    {
                                                        continuouslap = false;
                                                    }
                                                    previouslappass = lap;
                                                }
                                            }
                                            
                                            if (continuouslap)
                                            {
                                                var tsi = TransponderStatisticsItem.Create(item, passing.Transponder, laps);
                                                _context.Add(tsi);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                }
            }

            await _context.SaveChangesAsync();

            return passing;
        }

        public async Task<TrackSegmentPassing> RegisterTrackSegmentPassing(Passing passing)
        {
            var trackSegment = await _context.Set<TrackSegment>()
                .Include(s => s.Start)
                .Include(s => s.End)
                .SingleOrDefaultAsync(s => s.End == passing.Loop);

            if (trackSegment != null)
            {
                var previous = await _context.Set<Passing>()
                    .Where(p => p.Transponder == passing.Transponder)
                    .Where(p => p.Time < passing.Time)
                    .OrderByDescending(p => p.Time)
                    .Include(s => s.Loop)
                    .FirstOrDefaultAsync();

                if (previous != null && previous.Loop == trackSegment.Start)
                {
                    var transponderPassing = TrackSegmentPassing.Create(trackSegment, previous, passing);
                    _context.Add(transponderPassing);
                    await _context.SaveChangesAsync();
                    return transponderPassing;
                }
            }

            return null;
        }
    }
}
