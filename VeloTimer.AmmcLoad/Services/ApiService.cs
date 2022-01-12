using AutoMapper;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Models;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.AmmcLoad.Services
{
    public class ApiService : IApiService
    {
        private readonly ILogger<ApiService> _logger;
        private readonly IMapper _mapper;
        private readonly HttpClient _httpClient;

        public ApiService(ILogger<ApiService> logger, IMapper mapper, HttpClient httpClient)
        {
            _logger = logger;
            _mapper = mapper;
            _httpClient = httpClient;
        }

        public async Task<PassingWeb> GetMostRecentPassing()
        {
            PassingWeb passing;

            try
            {
                passing = await _httpClient.GetFromJsonAsync<PassingWeb>("passings/mostrecent");
                _logger.LogInformation("Most recent passing found {0}", passing?.SourceId);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Most recent passing not found");
                    passing = null;
                }
                else
                {
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

            _logger.LogError($"Could not post passing - {passing.Source} - {posted.StatusCode}");
            return false;
        }

        public async Task<bool> RegisterPassing(PassingAmmc passing)
        {
            var toRegister = _mapper.Map<PassingRegister>(passing);
            return await RegisterPassing(toRegister);
        }
    }
}
