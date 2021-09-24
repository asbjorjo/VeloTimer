using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Server.Data;
using VeloTimerWeb.Server.Hubs;
using VeloTimerWeb.Server.Services.Mylaps;

namespace VeloTimerWeb.Server.Services
{
    public class RefreshPassingsService : IHostedService, IDisposable
    {
        private const long TimerInterval = 1000;
        private int _lock = 0;
        private Timer _timer;

        private readonly ILogger<RefreshPassingsService> _logger;
        private readonly AmmcPassingService _passingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubContext<PassingHub> _hubContext;

        public RefreshPassingsService(IHubContext<PassingHub> hubContext, IServiceScopeFactory servicesScopeFactory, AmmcPassingService passingService,
                                      ILogger<RefreshPassingsService> logger)
        {
            _passingService = passingService;
            _logger = logger;
            _serviceScopeFactory = servicesScopeFactory;
            _hubContext = hubContext;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Passings Refresh started");

            _timer = new Timer(DoRefresh, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(TimerInterval));

            return Task.CompletedTask;
        }

        private async void DoRefresh(object state)
        {
            int sync = Interlocked.CompareExchange(ref _lock, 1, 0);

            if (sync == 0)
            {
                try
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);

                    await RefreshPassings();

                }

                finally
                {
                    _timer.Change(TimeSpan.FromMilliseconds(TimerInterval), TimeSpan.FromMilliseconds(TimerInterval));
                    _lock = 0;
                }
            }
        }

        private async Task RefreshPassings()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var track = dbContext.Set<Track>().FirstOrDefault();

                if (track == null)
                {
                    track = new Track { Length = 250, Name = "Sola Arena" };
                    await dbContext.AddAsync(track);

                    var timingLoops = new List<TimingLoop>()
                    {
                        new TimingLoop{ Track = track, LoopId = 1, Description = "Start/Finish", Distance = 0},
                        new TimingLoop{ Track = track, LoopId = 0, Description = "200m", Distance = 50},
                        new TimingLoop{ Track = track, LoopId = 2, Description = "100m", Distance = 150},
                        new TimingLoop{ Track = track, LoopId = 3, Description = "Green", Distance = 225},
                        new TimingLoop{ Track = track, LoopId = 4, Description = "Red", Distance = 100}
                    };

                    await dbContext.AddRangeAsync(timingLoops);

                    await dbContext.SaveChangesAsync();
                }

                var mostRecent = dbContext.Set<Passing>().Select(p => p.Source).OrderByDescending(p => p).FirstOrDefault();
                await _hubContext.Clients.All.SendAsync("passing.latest", mostRecent);

                _logger.LogInformation("Most recent passing found {0}", mostRecent);

                var passings = await _passingService.GetAfterEntry(mostRecent);

                if (!passings.Any())
                {
                    _logger.LogInformation("Found no new passings");
                    return;
                }

                _logger.LogInformation("Found {0} number of passings", passings.Count);

                var trackPassings = new List<Passing>();

                var transponderIds = passings.Select(p => p.TransponderId).Distinct();
                var knownTransponderIds = dbContext.Set<Transponder>().Where(t => transponderIds.Contains(t.Id)).Select(t => t.Id);

                _logger.LogInformation("Identified new transponders: {0} of {1}",
                    transponderIds.Except(knownTransponderIds).Count(),
                    transponderIds.Count());

                foreach (var transponderId in transponderIds.Except(knownTransponderIds))
                {
                    await dbContext.Set<Transponder>().AddAsync(new Transponder { Id = transponderId });
                }
                await dbContext.SaveChangesAsync();

                var loops = new Dictionary<long, long>();
                foreach (var loop in dbContext.Set<TimingLoop>())
                {
                    loops.Add(loop.LoopId, loop.Id);
                }

                foreach (var p in passings)
                {
                    //_logger.LogInformation("loop: {0} - transponder: {1}", p.LoopId, p.TransponderId);

                    trackPassings.Add(new Passing
                    {
                        LoopId = loops.GetValueOrDefault(p.LoopId),
                        TransponderId = p.TransponderId,
                        Time = p.UtcTime,
                        Source = p.Id
                    });
                }

                _logger.LogInformation("Converted {0} number of passings", trackPassings.Count);

                await dbContext.AddRangeAsync(trackPassings);
                await dbContext.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("passing.updated");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Passings Refresh ended");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
