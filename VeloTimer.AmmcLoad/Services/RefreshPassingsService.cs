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

namespace VeloTimer.AmmcLoad.Services
{
    public class RefreshPassingsService : IHostedService, IDisposable
    {
        private const long TimerInterval = 1000;
        private int _lock = 0;
        private Timer _timer;

        private readonly ILogger<RefreshPassingsService> _logger;
        private readonly AmmcPassingService _passingService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private HttpClient _httpClient;

        private readonly HubConnection _hubConnection;

        public RefreshPassingsService(IServiceScopeFactory servicesScopeFactory,
                                      AmmcPassingService passingService,
                                      ILogger<RefreshPassingsService> logger,
                                      HubConnection hubConnection)
        {
            _passingService = passingService;
            _logger = logger;
            _serviceScopeFactory = servicesScopeFactory;
            _hubConnection = hubConnection;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Passings Refresh started");
            
            await _hubConnection.StartAsync(cancellationToken);

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

        private async Task RefreshPassings()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            _httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            Passing mostRecent;

            try
            {
                mostRecent = await _httpClient.GetFromJsonAsync<Passing>("/passings/mostrecent");
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

            var passings = await (mostRecent is null ? _passingService.GetAll() : _passingService.GetAfterEntry(mostRecent.Source));

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

            var tasks = new List<Task>();

            foreach (var trackpassing in trackPassings)
            {
                tasks.Add(PostPassing(trackpassing));
            }

            await Task.WhenAll(tasks);

            //HttpResponseMessage response = await _httpClient.PostAsJsonAsync("passings/createmany", trackPassings);

            //if (!response.IsSuccessStatusCode)
            //{
            //    _logger.LogError($"{response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            //} else
            //{
            //    await _hubConnection.InvokeAsync("NotifyLoopsOfNewPassings", trackPassings.Select(tp => tp.LoopId).Distinct());
            //}
        }

        private async Task PostPassing(Passing passing)
        {
            var posted = await _httpClient.PostAsJsonAsync("passings", passing);

            if (posted.IsSuccessStatusCode)
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                _hubConnection.InvokeAsync("NotifyLoopOfNewPassing", passing.LoopId);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            } 
            else
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
