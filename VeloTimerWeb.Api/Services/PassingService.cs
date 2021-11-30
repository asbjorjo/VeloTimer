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
                var statsquery = _context.Set<TrackStatisticsItem>()
                    .Where(t => t.Segments.OrderBy(s => s.Order).Last().Segment.End == passing.Loop)
                    .OrderByDescending(s => s.Segments.Count())
                    .Include(s => s.StatisticsItem)
                    .Include(s => s.Segments)
                        .ThenInclude(s => s.Segment);

                var statisticsItems = await statsquery.ToListAsync();

                if (statisticsItems.Count > 0)
                {
                    var transponderPassings = await _context.Set<TrackSegmentPassing>()
                        .Where(x => x.Transponder == passing.Transponder && x.EndTime <= passing.Time)
                        .Include(x => x.TrackSegment)
                        .OrderByDescending(x => x.EndTime)
                        .Take(statisticsItems.First().Segments.Count())
                        .OrderBy(x => x.EndTime)
                        .ToListAsync();

                    foreach (var statisticsItem in statisticsItems)
                    {
                        if (statisticsItem.Segments.Count() <= transponderPassings.Count)
                        {
                            var relevantPassings = transponderPassings.TakeLast(statisticsItem.Segments.Count());

                            var segments = statisticsItem.Segments.OrderBy(x => x.Order);

                            if (relevantPassings.Select(s => s.TrackSegment.Id).SequenceEqual(segments.Select(s => s.Segment.Id)))
                            {

                                var transponderStats = TransponderStatisticsItem.Create(statisticsItem, passing.Transponder, relevantPassings.ToList());
                                _context.Add(transponderStats);
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
