using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VeloTimer.AmmcLoad.Services
{
    public class RefreshPassingsService : IHostedService, IDisposable
    {
        private const long TimerInterval = 1000;
        private int _lock = 0;
        private Timer _timer;
        private string mostRecent;

        private readonly ILogger<RefreshPassingsService> _logger;
        private readonly AmmcPassingService _passingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private IApiService _apiService;
        private IMessagingService _messagingService;

        public RefreshPassingsService(IServiceScopeFactory servicesScopeFactory,
                                      AmmcPassingService passingService,
                                      IMessagingService messagingService,
                                      ILogger<RefreshPassingsService> logger)
        {
            _passingService = passingService;
            _messagingService = messagingService;
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

            var passing = await _apiService.GetMostRecentPassing();
            mostRecent = passing?.SourceId;
        }

        private async Task RefreshPassings()
        {
            var passings = await (mostRecent is null ? _passingService.GetAll() : _passingService.GetAfterEntry(mostRecent));

            if (!passings.Any())
            {
                _logger.LogInformation("Found no new passings");
                return;
            }

            _logger.LogInformation("Found {0} number of passings", passings.Count);

            using var scope = _serviceScopeFactory.CreateScope();
            _apiService = scope.ServiceProvider.GetRequiredService<IApiService>();

            await _messagingService.SubmitPassings(passings);

            mostRecent = passings.Last().Id;
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
