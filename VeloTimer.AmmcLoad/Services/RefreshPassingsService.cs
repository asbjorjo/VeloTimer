using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimer.AmmcLoad.Services
{
    public class RefreshPassingsService : IHostedService, IDisposable
    {
        private const long TimerInterval = 1000;
        private int _lock = 0;
        private Timer _timer;
        private Passing mostRecent;

        private readonly ILogger<RefreshPassingsService> _logger;
        private readonly AmmcPassingService _passingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private HttpClient _httpClient;

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
            _httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            try
            {
                mostRecent = await _httpClient.GetFromJsonAsync<Passing>("passings/mostrecent");
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    mostRecent = null;
                }
                else
                {
                    throw;
                }
            }


            _logger.LogInformation("Most recent passing found {0}", mostRecent?.Source);
        }

        private async Task RefreshPassings()
        {
            var passings = await (mostRecent is null ? _passingService.GetAll() : _passingService.GetAfterEntry(mostRecent.Source));

            if (!passings.Any())
            {
                _logger.LogInformation("Found no new passings");
                return;
            }

            _logger.LogInformation("Found {0} number of passings", passings.Count);

            var tasks = new List<Task>();

            foreach (var passing in passings)
            {
                tasks.Add(PostPassing(new PassingRegister
                {
                    LoopId = passing.LoopId,
                    Source = passing.Id,
                    Time = passing.UtcTime,
                    TimingSystem = TransponderType.TimingSystem.Mylaps_X2,
                    TransponderId = passing.TransponderId.ToString()
                }));
            }

            using var scope = _serviceScopeFactory.CreateScope();
            _httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
            await Task.WhenAll(tasks);

            await LoadMostRecentPassing();
        }

        private async Task PostPassing(PassingRegister passing)
        {
            var posted = await _httpClient.PostAsJsonAsync("passings/register", passing);

            if (!posted.IsSuccessStatusCode)
            {
                _logger.LogError($"Could not post passing - {passing.Source} - {posted.StatusCode}");
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
