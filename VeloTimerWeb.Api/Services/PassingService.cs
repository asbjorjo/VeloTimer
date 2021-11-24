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

            var segments = await _context.Set<TrackSegment>()
                .Where(s => s.End == passing.Loop)
                .ToListAsync();

            foreach (var segment in segments)
            {
                var startpassing = await _context.Set<Passing>()
                    .Where(p =>
                        p.TransponderId == passing.TransponderId
                        && p.Loop == segment.Start
                        && p.Time < passing.Time
                    )
                    .OrderByDescending(p => p.Time)
                    .FirstOrDefaultAsync();

                if (startpassing != null)
                {
                    //var segmentrun = new SegmentRun 
                    //{ 
                    //    Segment = segment, 
                    //    Start = startpassing, 
                    //    End = passing, 
                    //    Time = (passing.Time - startpassing.Time).TotalSeconds
                    //};

                    //_context.Add(segmentrun);

                    //await _hubContext.Clients.Group($"segment_{segment.Id}").NewSegmentRun(segmentrun);
                    //await _hubContext.Clients.Group($"segment_{segment.Id}_transponder_{passing.Transponder.Id}").NewSegmentRun(segmentrun, passing.Transponder);
                }
            }

            await _context.SaveChangesAsync();

            return passing;
        }
    }
}
