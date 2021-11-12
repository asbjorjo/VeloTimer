using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Client.Services
{
    public class ApiClient : IApiClient
    {
        private readonly HttpClient _client;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly ILogger<ApiClient> _logger;

        public ApiClient(
            HttpClient client, 
            IConfiguration config, 
            AuthenticationStateProvider authStateProvider,
            ILogger<ApiClient> logger)
        {
            _client = client;
            client.BaseAddress = new Uri(config["VELOTIMER_API_URL"]);
            _authStateProvider = authStateProvider;
            _logger = logger;
        }

        public async Task<int> GetActiveRiderCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using (var response = await _client.GetAsync($"rider/activecount?fromtime={fromtime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}&totime={totime?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();

                var riders = await JsonSerializer.DeserializeAsync<int>(content);

                return riders;
            }
        }

        public async Task<IEnumerable<Rider>> GetActiveRiders(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using (var response = await _client.GetAsync($"rider/active?fromtime={fromtime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}&totime={totime?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var riders = await response.Content.ReadFromJsonAsync<IEnumerable<Rider>>();

                return riders;
            }
        }

        public async Task<IEnumerable<Transponder>> GetActiveTransponders(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using (var response = await _client.GetAsync($"transponders/active?fromtime={fromtime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}&totime={totime?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync();

                var riders = await JsonSerializer.DeserializeAsync<IEnumerable<Transponder>>(content);

                return riders;
            }
        }

        public async Task<Rider> GetRiderByUserId(string userId)
        {
            using (var response = await _client.GetAsync($"rider/user/{userId}"))
            {
                response.EnsureSuccessStatusCode();

                var rider = await response.Content.ReadFromJsonAsync<Rider>();

                return rider;
            }
        }
    }
}
