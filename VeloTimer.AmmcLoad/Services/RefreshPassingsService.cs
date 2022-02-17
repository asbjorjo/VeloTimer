using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloTime.Shared.Messaging;
using VeloTimer.AmmcLoad.Models;
using VeloTimer.PassingLoader.Services.Api;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.AmmcLoad.Services
{
    public class RefreshPassingsService : IHostedService, IDisposable
    {
        private TimeSpan TimerInterval = TimeSpan.FromSeconds(1);
        private int _lock = 0;
        private Timer _timer;
        private PassingWeb mostRecent;
        private PassingAmmc lastFromDb;

        private readonly ILogger<RefreshPassingsService> _logger;
        private readonly AmmcPassingService _passingService;
        private readonly IMapper _mapper;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private IApiService _apiService;
        private IMessagingService<PassingRegister> _messagingService;

        public RefreshPassingsService(IServiceScopeFactory servicesScopeFactory,
                                      AmmcPassingService passingService,
                                      IMapper mapper,
                                      IMessagingService<PassingRegister> messagingService,
                                      ILogger<RefreshPassingsService> logger)
        {
            _passingService = passingService;
            _mapper = mapper;
            _messagingService = messagingService;
            _logger = logger;
            _serviceScopeFactory = servicesScopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Passings Refresh started");

            _timer = new Timer(DoRefresh, null, TimeSpan.Zero, TimerInterval);

            await Task.CompletedTask;
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
                    _timer.Change(TimerInterval, TimerInterval);
                    _lock = 0;
                }
            }
        }

        private async Task LoadMostRecentPassing()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            _apiService = scope.ServiceProvider.GetRequiredService<IApiService>();

            var passing = await _apiService.GetMostRecentPassing();
            mostRecent = passing;
        }

        private async Task RefreshPassings()
        {
            if (mostRecent == null)
            {
                try
                {
                    await LoadMostRecentPassing();
                    TimerInterval = TimeSpan.FromSeconds(1);
                }
                catch
                {
                    TimerInterval = TimeSpan.FromSeconds(5);
                    return;
                }
            }

            List<PassingAmmc> passings;
            
            if (mostRecent == null && lastFromDb == null)
            {
                passings = await _passingService.GetAll();
            } 
            else if (mostRecent != null && lastFromDb == null)
            {
                passings = await _passingService.GetAfterTime(mostRecent.Time.ToUniversalTime() - TimeSpan.FromMinutes(15));
            } 
            else
            {
                passings = await _passingService.GetAfterTime(lastFromDb.UtcTime.DateTime);
            }

            if (!passings.Any())
            {
                _logger.LogInformation("Found no new passings");
                return;
            }

            _logger.LogInformation("Found {Count} number of passings", passings.Count);

            await _messagingService.SendMessages(_mapper.Map<List<PassingRegister>>(passings));

            lastFromDb = passings.Last();
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
