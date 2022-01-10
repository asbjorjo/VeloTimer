using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.AmmcLoad.Services
{
    public class RefreshPassingsService : IHostedService, IDisposable
    {
        private const long TimerInterval = 1000;
        private int _lock = 0;
        private Timer _timer;
        private PassingWeb mostRecent;

        private readonly ILogger<RefreshPassingsService> _logger;
        private readonly AmmcPassingService _passingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private IApiService _apiService;
        
        public RefreshPassingsService(IServiceScopeFactory servicesScopeFactory,
                                      AmmcPassingService passingService,
                                      ILogger<RefreshPassingsService> logger)
        {
            _passingService = passingService;
            _logger = logger;
            _serviceScopeFactory = servicesScopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Passings Refresh started");

            await LoadMostRecentPassing();

            _timer = new Timer(DoRefresh, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(TimerInterval));
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

        private async Task LoadMostRecentPassing()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            _apiService = scope.ServiceProvider.GetRequiredService<IApiService>();

            mostRecent = await _apiService.GetMostRecentPassing();
        }

        private async Task RefreshPassings()
        {
            var passings = await (mostRecent is null ? _passingService.GetAll() : _passingService.GetAfterEntry(mostRecent.SourceId));

            if (!passings.Any())
            {
                _logger.LogInformation("Found no new passings");
                return;
            }

            _logger.LogInformation("Found {0} number of passings", passings.Count);

            using var scope = _serviceScopeFactory.CreateScope();
            _apiService = scope.ServiceProvider.GetRequiredService<IApiService>();
            foreach (var passing in passings)
            {
                var attempts = 1;
                while (! await _apiService.RegisterPassing(passing))
                {
                    _logger.LogInformation($"Registering passing failed, attempt {attempts}");
                    await Task.Delay(TimeSpan.FromMilliseconds(500*attempts));
                    attempts++;
                }
            }

            mostRecent = await _apiService.GetMostRecentPassing();
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
