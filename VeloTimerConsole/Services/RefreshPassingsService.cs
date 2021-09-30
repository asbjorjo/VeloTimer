using Microsoft.AspNetCore.SignalR.Client;
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
using VeloTimer.Shared.Hub;
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
        private readonly HttpClient _httpClient;

        private HubConnection hubConnection;

        public RefreshPassingsService(IServiceScopeFactory servicesScopeFactory,
                                      AmmcPassingService passingService,
                                      ILogger<RefreshPassingsService> logger,
                                      HttpClient httpClient)
        {
            _passingService = passingService;
            _logger = logger;
            _serviceScopeFactory = servicesScopeFactory;
            _httpClient = httpClient;

            _logger.LogInformation("Created");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Passings Refresh started");

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"https://velotimer.azurewebsites.net/{Strings.hubUrl}")
                .WithAutomaticReconnect()
                .Build();

            hubConnection.StartAsync();

            _timer = new Timer(DoRefresh, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(TimerInterval));

            return Task.CompletedTask;
        }

        private async void DoRefresh(object state)
        {
            _logger.LogInformation("Refreshing");
            int sync = Interlocked.CompareExchange(ref _lock, 1, 0);

            if (sync == 0)
            {
                try
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);

                    _logger.LogInformation("Starting refresh");

                    await RefreshPassings();

                    _logger.LogInformation("Done refreshing");
                }

                finally
                {
                    _timer.Change(TimeSpan.FromMilliseconds(TimerInterval), TimeSpan.FromMilliseconds(TimerInterval));
                    _lock = 0;
                    _logger.LogInformation("Refreshed");
                }
            }
        }

        private async Task RefreshPassings()
        {
            var mostRecent = await _httpClient.GetFromJsonAsync<Passing>("/passings/mostrecent");
            
            await hubConnection.InvokeAsync("SendLastPassingToClients", mostRecent);

            _logger.LogInformation("Most recent passing found {0}", mostRecent.Source);

            var passings = await _passingService.GetAfterEntry(mostRecent.Source);

            if (!passings.Any())
            {
                _logger.LogInformation("Found no new passings");
                return;
            }

            _logger.LogInformation("Found {0} number of passings", passings.Count);

            var trackPassings = new List<Passing>();

            var transponderIds = passings.Select(p => p.TransponderId).Distinct();
            var loopIds = passings.Select(p => p.LoopId).Distinct();

            var transponders = await _httpClient.GetFromJsonAsync<IEnumerable<Transponder>>("transponders");
            var loops = await _httpClient.GetFromJsonAsync<IEnumerable<TimingLoop>>("timingloops");

            var knownTransponderIds = transponders.Where(t => transponderIds.Contains(t.Id)).Select(t => t.Id);
            var knownLoopIds = loops.Where(t => loopIds.Contains(t.LoopId)).Select(t => t.LoopId);

            foreach (var loopId in loopIds.Except(knownLoopIds))
            {
                await _httpClient.PostAsJsonAsync("timingloops", new TimingLoop { LoopId = loopId });
            }

            foreach (var transponderId in transponderIds.Except(knownTransponderIds))
            {
                await _httpClient.PostAsJsonAsync("transponders", new Transponder { Id = transponderId });
            }

            loops = await _httpClient.GetFromJsonAsync<IEnumerable<TimingLoop>>("timingloops");

            var loopdict = new Dictionary<long, long>();
            foreach (var loop in loops)
            {
                loopdict.Add(loop.LoopId, loop.Id);
            }

            foreach (var p in passings)
            {
                trackPassings.Add(new Passing
                { 
                    LoopId = loopdict.GetValueOrDefault(p.LoopId), 
                    TransponderId = p.TransponderId, 
                    Time = p.UtcTime,
                    Source = p.Id
                });
            }

            _logger.LogInformation("Converted {0} number of passings", trackPassings.Count);

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync("passings/createmany", trackPassings);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"{response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            }

            await hubConnection.InvokeAsync("NotifyClientsOfNewPassings");
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
