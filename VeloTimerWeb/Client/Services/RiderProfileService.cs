using Microsoft.AspNetCore.Components.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models.Riders;
using VeloTimerWeb.Client.Services;

namespace Client.Services
{
    public class RiderProfileService : IRiderProfileService
    {
        private readonly HttpClient _httpClient;
        private readonly IApiClient _apiClient;
        private readonly AuthenticationStateProvider _authenticationStateProvider;

        public RiderProfileService(HttpClient httpClient, IApiClient apiClient, AuthenticationStateProvider authenticationStateProvider)
        {
            _httpClient = httpClient;
            _apiClient = apiClient;
            _authenticationStateProvider = authenticationStateProvider;
        }

        public async Task<bool> HasActiveTransponder()
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var userId = authState.User.FindFirst(c => c.Type == "sub")?.Value;

            var transpondernames = await _httpClient.GetFromJsonAsync<IEnumerable<TransponderOwnershipWeb>>($"rider/{userId}/transponders");

            return transpondernames.Any() && transpondernames.Select(t => t.OwnedFrom < DateTime.UtcNow && t.OwnedUntil > DateTime.UtcNow).Any();
        }
    }
}
