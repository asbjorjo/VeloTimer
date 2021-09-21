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

namespace VeloTimerConsole.Services
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
            var mostRecent = dbContext.Set<Passing>().Select(p => p.Time).OrderByDescending(p => p).FirstOrDefault();
            await _hubContext.Clients.All.SendAsync("passing.latest", mostRecent);

            _logger.LogInformation("Most recent passing found {0}", mostRecent);

            var passings = await _passingService.GetAfterTime(mostRecent);

            if (!passings.Any())
            {
                _logger.LogInformation("Found no new passings");
                return;
            }

            _logger.LogInformation("Found {0} number of passings", passings.Count);
            
            var trackPassings = new List<Passing>();

            var transponderIds = passings.Select(p => p.TransponderId).Distinct();
            var loopIds = passings.Select(p => p.LoopId).Distinct();

            var knownTransponderIds = dbContext.Set<Transponder>().Where(t => transponderIds.Contains(t.Id)).Select(t => t.Id);
            var knownLoopIds = dbContext.Set<TimingLoop>().Where(t => loopIds.Contains(t.LoopId)).Select(t => t.LoopId);

            foreach (var loopId in loopIds.Except(knownLoopIds))
            {
                dbContext.Set<TimingLoop>().Add(new TimingLoop { LoopId = loopId });
            }

            foreach (var transponderId in transponderIds.Except(knownTransponderIds))
            {
                dbContext.Set<Transponder>().Add(new Transponder { Id = transponderId });
            }
            dbContext.SaveChanges();

            var loops = new Dictionary<int, long>();
            foreach (var loop in dbContext.Set<TimingLoop>())
            {
                loops.Add(loop.LoopId, loop.Id);
            }

            foreach (var p in passings)
            {
                trackPassings.Add(new Passing { LoopId = loops.GetValueOrDefault(p.LoopId), TransponderId = p.TransponderId, Time = p.UtcTime });
            }

            _logger.LogInformation("Converted {0} number of passings", trackPassings.Count);

            dbContext.AddRange(trackPassings);
 
            await _hubContext.Clients.All.SendAsync("passing.updated");
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
