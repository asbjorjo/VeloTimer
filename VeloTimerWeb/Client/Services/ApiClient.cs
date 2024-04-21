using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Models.Riders;
using VeloTimer.Shared.Data.Models.Statistics;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Data.Models.TrackSetup;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Client.Components;

namespace VeloTimerWeb.Client.Services
{
    public class ApiClient : IApiClient
    {
        private readonly HttpClient _client;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly NavigationManager _navigation;
        private readonly ILogger<ApiClient> _logger;

        public ApiClient(
            HttpClient client,
            IConfiguration config,
            NavigationManager navigation,
            AuthenticationStateProvider authStateProvider,
            ILogger<ApiClient> logger)
        {
            _client = client;
            _authStateProvider = authStateProvider;
            _navigation = navigation;
            _client.BaseAddress = new Uri(new Uri(navigation.BaseUri), "api/");
            _logger = logger;
            _logger.LogDebug(_client.BaseAddress.ToString());
        }

        public async Task<int> GetActiveRiderCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var url = new StringBuilder();

            url.Append($"rider/activecount?fromtime={TimeFormatter(fromtime)}");

            if (totime.HasValue)
            {
                url.Append($"&totime={TimeFormatter(totime.Value)}");
            }

            using var response = await _client.GetAsync(url.ToString(), HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStreamAsync();

            var riders = await JsonSerializer.DeserializeAsync<int>(content);

            return riders;
        }

        public async Task<IEnumerable<RiderWeb>> GetActiveRiders(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var url = new StringBuilder();

            url.Append($"rider/active?fromtime={TimeFormatter(fromtime)}");

            if (totime.HasValue)
            {
                url.Append($"&totime={TimeFormatter(totime.Value)}");
            }

            using var response = await _client.GetAsync(url.ToString(), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var riders = await response.Content.ReadFromJsonAsync<IEnumerable<RiderWeb>>();

            return riders;
        }

        public async Task<int> GetActiveTransponderCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var url = new StringBuilder();

            url.Append($"transponders/activecount?fromtime={TimeFormatter(fromtime)}");

            if (totime.HasValue)
            {
                url.Append($"&totime={TimeFormatter(totime.Value)}");
            }

            using var response = await _client.GetAsync(url.ToString(), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStreamAsync();

            var count = await JsonSerializer.DeserializeAsync<int>(content);

            return count;
        }

        public async Task<IEnumerable<TransponderWeb>> GetActiveTransponders(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var url = new StringBuilder();

            url.Append($"transponders/active?fromtime={TimeFormatter(fromtime)}");

            if (totime.HasValue)
            {
                url.Append($"&totime={TimeFormatter(totime.Value)}");
            }

            using var response = await _client.GetAsync(url.ToString(), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var riders = await response.Content.ReadFromJsonAsync<IEnumerable<TransponderWeb>>();

            return riders;
        }

        public async Task<IEnumerable<SegmentTime>> GetBestTimes(string StatsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count, string Rider, bool OnePerRider)
        {
            var url = new StringBuilder();

            if (Rider != null)
            {
                url.Append($"rider/{Rider}");
            }
            else
            {
                url.Append($"track/sola-arena");
            }

            url.Append($"/fastest/{StatsItem}?Count={Count}");

            if (FromTime.HasValue)
            {
                url.Append($"&FromTime={TimeFormatter(FromTime.Value)}");
            }
            if (ToTime.HasValue)
            {
                url.Append($"&ToTime={TimeFormatter(ToTime.Value)}");
            }

            using var response = await _client.GetAsync(url.ToString());
            response.EnsureSuccessStatusCode();

            var times = await response.Content.ReadFromJsonAsync<IEnumerable<SegmentTime>>();

            return times;
        }

        public async Task<PassingWeb> GetLastPassing()
        {
            using var response = await _client.GetAsync($"passings/mostrecent");
            response.EnsureSuccessStatusCode();

            var passing = await response.Content.ReadFromJsonAsync<PassingWeb>();

            return passing;
        }

        public async Task<PaginatedResponse<RiderWeb>> GetRiders(PaginationParameters pagination)
        {
            using var response = await _client.GetAsync($"rider?{pagination.ToQueryString()}");
            response.EnsureSuccessStatusCode();

            var riders = new PaginatedResponse<RiderWeb>
            {
                Items = await response.Content.ReadFromJsonAsync<List<RiderWeb>>(),
                Pagination = JsonSerializer.Deserialize<Pagination>(response.Headers.GetValues("X-Pagination").First())
            };

            return riders;
        }

        public async Task<RiderWeb> GetRiderByUserId(string userId)
        {
            using var response = await _client.GetAsync($"rider/{userId}");
            response.EnsureSuccessStatusCode();

            var rider = await response.Content.ReadFromJsonAsync<RiderWeb>();

            return rider;
        }

        public async Task<PaginatedResponse<SegmentTime>> GetTimes(StatisticsParameters statisticsParameters, TimeParameters timeParameters, PaginationParameters pagingParameters, string Rider)
        {
            var url = new StringBuilder();

            if (Rider != null)
            {
                url.Append("rider/").Append(Rider);
                url.Append("/times/").Append(statisticsParameters.ToPathString());
            }
            else
            {
                url.Append("track/sola-arena/times/");
                url.Append(statisticsParameters.Label);
            }

            url.Append("?").Append(pagingParameters.ToQueryString()).Append("&").Append(timeParameters.ToQueryString());
            url.Append("&orderby=").Append(statisticsParameters.OrderBy);

            using var response = await _client.GetAsync(url.ToString());
            response.EnsureSuccessStatusCode();

            var times = new PaginatedResponse<SegmentTime>
            {
                Items = await response.Content.ReadFromJsonAsync<List<SegmentTime>>(),
                Pagination = JsonSerializer.Deserialize<Pagination>(response.Headers.GetValues("X-Pagination").First())
            };

            return times;
        }

        public async Task<IEnumerable<SegmentTime>> GetTimes(StatisticsParameters statisticsParameters, DateTimeOffset? FromTime, int Count, string Rider, bool IncludeIntermediate)
        {
            var url = new StringBuilder();

            if (Rider != null)
            {
                url.Append("rider/").Append(Rider);
                url.Append("/times/").Append(statisticsParameters.ToPathString());
                IncludeIntermediate = true;
            }
            else
            {
                url.Append("track/sola-arena/times/");
                url.Append(statisticsParameters.Label);
                IncludeIntermediate = false;
            }

            url.Append("?includeintermediate=").Append(IncludeIntermediate);
            url.Append("&orderby=").Append(statisticsParameters.OrderBy);
            url.Append("&count=").Append(Count);

            if (FromTime.HasValue)
            {
                url.Append($"&FromTime={TimeFormatter(FromTime.Value)}");
            }

            using var response = await _client.GetAsync(url.ToString());
            response.EnsureSuccessStatusCode();

            var times = await response.Content.ReadFromJsonAsync<IEnumerable<SegmentTime>>();

            return times;
        }

        public async Task<IEnumerable<SegmentTime>> GetTimesForTransponder(string StatsItem, long TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            using var reponse = await _client.GetAsync($"transponders/times?StatsItem={StatsItem}&TransponderId{TransponderId}&FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}");
            reponse.EnsureSuccessStatusCode();

            var times = await reponse.Content.ReadFromJsonAsync<IEnumerable<SegmentTime>>();

            return times;
        }

        public async Task<IEnumerable<SegmentTime>> GetTimesForTransponders(string StatsItem, IEnumerable<long> TransponderIds, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            using var reponse = await _client.GetAsync($"transponders/times?StatsItem={StatsItem}&TransponderId={string.Join("&TransponderIds=", TransponderIds)}&FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}");
            reponse.EnsureSuccessStatusCode();

            var times = await reponse.Content.ReadFromJsonAsync<IEnumerable<SegmentTime>>();

            return times;
        }

        public async Task<IEnumerable<TrackSegmentWeb>> GetTrackSegments(string Track)
        {
            using var response = await _client.GetAsync($"tracksegment/{Track}");
            response.EnsureSuccessStatusCode();

            var segments = await response.Content.ReadFromJsonAsync<IEnumerable<TrackSegmentWeb>>();

            return segments;
        }

        public async Task<TrackStatisticsItemWeb> GetStatisticsItem(string Label, string Track)
        {
            string url;

            if (Track != null)
            {
                url = $"statisticsitem/{Label}/track/{Track}";
            }
            else
            {
                url = $"statisticsitem/{Label}";
            }

            using var response = await _client.GetAsync(url);

            response.EnsureSuccessStatusCode();

            var items = await response.Content.ReadFromJsonAsync<IEnumerable<TrackStatisticsItemWeb>>();
            var item = items.First();

            return item;
        }

        public async Task<IEnumerable<TrackStatisticsItemWeb>> GetStatisticsItems(string Track)
        {
            string url;

            if (Track != null)
            {
                url = $"statisticsitem/track/{Track}";
            }
            else
            {
                url = $"statisticsitem";
            }

            using var response = await _client.GetAsync(url);

            response.EnsureSuccessStatusCode();

            var items = await response.Content.ReadFromJsonAsync<IEnumerable<TrackStatisticsItemWeb>>();

            return items;
        }

        public async Task<IEnumerable<SegmentDistance>> GetCount(string StatsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            var url = new StringBuilder();

            url.Append($"track/sola-arena/count/{StatsItem}?Count={Count}");

            if (FromTime.HasValue)
            {
                url.Append($"&FromTime={TimeFormatter(FromTime.Value)}");
            }
            if (ToTime.HasValue)
            {
                url.Append($"&ToTime={TimeFormatter(ToTime.Value)}");
            }

            using var response = await _client.GetAsync($"track/sola-arena/count/{StatsItem}?FromTime={TimeFormatter(FromTime)}&ToTime={TimeFormatter(ToTime)}&Count={Count}");
            response.EnsureSuccessStatusCode();

            var counts = await response.Content.ReadFromJsonAsync<IEnumerable<SegmentDistance>>();

            return counts;
        }

        public async Task<IEnumerable<TimingLoopWeb>> GetTimingPoints(string Track)
        {
            using var response = await _client.GetAsync($"timingloops/track/{Track}");
            response.EnsureSuccessStatusCode();

            var loops = await response.Content.ReadFromJsonAsync<IEnumerable<TimingLoopWeb>>();

            return loops;
        }

        private static string TimeFormatter(DateTimeOffset? time)
        {
            var timeString = time?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

            if (!string.IsNullOrEmpty(timeString) && timeString.EndsWith("999000Z"))
            {
                timeString = timeString.Replace("999000Z", "999Z");
            }

            return timeString;
        }

        public async Task RemoveTransponderRegistration(TransponderOwnershipWeb transponderOwnership)
        {
            using var response = await _client.DeleteAsync($"rider/{transponderOwnership.Owner.UserId}/transponder/{transponderOwnership.Transponder.SystemId}/{TimeFormatter(transponderOwnership.OwnedFrom)}/{TimeFormatter(transponderOwnership.OwnedUntil)}");
            response.EnsureSuccessStatusCode();
        }

        public async Task SaveRiderProfile(RiderWeb riderWeb)
        {
            var userid = riderWeb.UserId;
            using var response = await _client.PutAsJsonAsync($"rider/{userid}", riderWeb);
            response.EnsureSuccessStatusCode();
        }

        public async Task DeleteRiderProfile(string userid)
        {
            using var response = await _client.DeleteAsync($"rider/{userid}");
            response.EnsureSuccessStatusCode();
        }

        public async Task<PaginatedResponse<TransponderWeb>> GetTransponders(PaginationParameters pagination)
        {
            using var response = await _client.GetAsync($"transponders/?{pagination.ToQueryString()}");
            response.EnsureSuccessStatusCode();

            var transponders = new PaginatedResponse<TransponderWeb>
            {
                Items = await response.Content.ReadFromJsonAsync<List<TransponderWeb>>(),
                Pagination = JsonSerializer.Deserialize<Pagination>(response.Headers.GetValues("X-Pagination").First())
            };

            return transponders;
        }

        public async Task<PaginatedResponse<TransponderOwnershipWeb>> GetTransponderOwners(PaginationParameters pagination)
        {
            using var response = await _client.GetAsync($"transponders/ownerships?{pagination.ToQueryString()}");
            response.EnsureSuccessStatusCode();

            var transponders = new PaginatedResponse<TransponderOwnershipWeb>
            {
                Items = await response.Content.ReadFromJsonAsync<List<TransponderOwnershipWeb>>(),
                Pagination = JsonSerializer.Deserialize<Pagination>(response.Headers.GetValues("X-Pagination").First())
            };

            return transponders;
        }

        public async Task<AdminDashboardModel> GetAdminDashboardModel()
        {
            using var response = await _client.GetAsync("dashboards/admin");
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<AdminDashboardModel>();
        }

        public async Task ExtendRegistration(TransponderOwnershipWeb transponderOwnership, DateTimeOffset newEnd)
        {
            transponderOwnership.OwnedUntil = newEnd;

            using var response = await _client.PutAsJsonAsync($"rider/{transponderOwnership.Owner.UserId}/transponder/{transponderOwnership.Transponder.SystemId}", transponderOwnership);
            response.EnsureSuccessStatusCode();
        }
    }
}
