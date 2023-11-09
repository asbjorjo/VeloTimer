using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Services.Api
{
    public class ApiService : IApiService
    {
        private readonly ILogger<ApiService> _logger;
        private readonly HttpClient _httpClient;

        public ApiService(ILogger<ApiService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<PassingWeb> GetMostRecentPassing()
        {
            return await GetMostRecentPassing(string.Empty);
        }

        public async Task<PassingWeb> GetMostRecentPassing(string Track)
        {
            PassingWeb passing = new PassingWeb();

            string requestUri = string.IsNullOrEmpty(Track) ? "passings/mostrecent" : $"passings/mostrecent/{Track}";

            try
            {
                PassingWeb? _passing = await _httpClient.GetFromJsonAsync<PassingWeb>(requestUri);
                if (_passing == null)
                {
                    _logger.LogError("Most recent passing not found");
                }
                else
                {
                    passing = _passing;
                    _logger.LogInformation("Most recent passing found {Passing}", passing.SourceId);
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Most recent passing not found");
                }
                else
                {
                    _logger.LogError("{Exception}", ex);
                    throw;
                }
            }

            return passing;
        }

        public async Task<bool> RegisterPassing(PassingRegister passing)
        {
            var posted = await _httpClient.PostAsJsonAsync("passings/register", passing);

            if (posted.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogError("Could not post passing - {Passing} - {StatusCode}",
                             passing.Source,
                             posted.StatusCode);
            return false;
        }
    }
}
