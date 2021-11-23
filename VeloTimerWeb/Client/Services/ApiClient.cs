using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
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
            _client.BaseAddress = new Uri(new Uri(config["VELOTIMER_API_URL"]), "api/");
            _authStateProvider = authStateProvider;
            _logger = logger;
            _logger.LogDebug(_client.BaseAddress.ToString());
        }

        public async Task<int> GetActiveRiderCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using var response = await _client.GetAsync($"rider/activecount?fromtime={TimeFormatter(fromtime)}&totime={TimeFormatter(totime)}", HttpCompletionOption.ResponseHeadersRead);
            
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStreamAsync();

            var riders = await JsonSerializer.DeserializeAsync<int>(content);

            return riders;
        }

        public async Task<IEnumerable<Rider>> GetActiveRiders(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using var response = await _client.GetAsync($"rider/active?fromtime={TimeFormatter(fromtime)}&totime={TimeFormatter(totime)}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var riders = await response.Content.ReadFromJsonAsync<IEnumerable<Rider>>();

            return riders;
        }

        public async Task<int> GetActiveTransponderCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using var response = await _client.GetAsync($"transponders/activecount?fromtime={TimeFormatter(fromtime)}&totime={TimeFormatter(totime)}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStreamAsync();

            var count = await JsonSerializer.DeserializeAsync<int>(content);

            return count;
        }

        public async Task<IEnumerable<Transponder>> GetActiveTransponders(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            using var response = await _client.GetAsync($"transponders/active?fromtime={TimeFormatter(fromtime)}&totime={TimeFormatter(totime)}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var riders = await response.Content.ReadFromJsonAsync<IEnumerable<Transponder>>();

            return riders;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetBestTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count, long? RiderId, bool OnePerRider)
        {
            using var response = await _client.GetAsync($"segments/fastest?SegmentId={SegmentId}&FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}&RiderId={RiderId}&OnePerRider={OnePerRider}");
            response.EnsureSuccessStatusCode();

            var times = await response.Content.ReadFromJsonAsync<IEnumerable<SegmentTimeRider>>();

            return times;
        }

        public async Task<Passing> GetLastPassing()
        {
            using var response = await _client.GetAsync($"passings/mostrecent");
            response.EnsureSuccessStatusCode();

            var passing = await response.Content.ReadFromJsonAsync<Passing>();

            return passing;
        }

        public async Task<Rider> GetRiderByUserId(string userId)
        {
            using var response = await _client.GetAsync($"rider/user/{userId}");
            response.EnsureSuccessStatusCode();

            var rider = await response.Content.ReadFromJsonAsync<Rider>();

            return rider;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count, long? RiderId)
        {
            using var response = await _client.GetAsync($"segments/times?SegmentId={SegmentId}&FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}&RiderId={RiderId}");
            response.EnsureSuccessStatusCode();

            var times = await response.Content.ReadFromJsonAsync<IEnumerable<SegmentTimeRider>>();

            return times;
        }

        private static string TimeFormatter(DateTimeOffset? time)
        {
            return time?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        public async Task<IEnumerable<Segment>> GetSegments(long TrackId)
        {
            using var response = await _client.GetAsync($"segments");
            response.EnsureSuccessStatusCode();

            var segments = await response.Content.ReadFromJsonAsync<IEnumerable<Segment>>();

            return segments;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesForTransponder(long SegmentId, long TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            using var reponse = await _client.GetAsync($"transponders/times?SegmentId={SegmentId}&TransponderId{TransponderId}&FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}");
            reponse.EnsureSuccessStatusCode();

            var times = await reponse.Content.ReadFromJsonAsync<IEnumerable<SegmentTimeRider>>();

            return times;
        }

        public async Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesForTransponders(long SegmentId, IEnumerable<long> TransponderIds, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            using var reponse = await _client.GetAsync($"transponders/times?SegmentId={SegmentId}&TransponderId={string.Join("&TransponderIds=", TransponderIds)}&FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}");
            reponse.EnsureSuccessStatusCode();

            var times = await reponse.Content.ReadFromJsonAsync<IEnumerable<SegmentTimeRider>>();

            return times;
        }
    }
}
