﻿using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.PassingLoader.Services.Api
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

        public async Task<PassingWeb?> GetMostRecentPassing()
        {
            PassingWeb? passing;

            try
            {
                passing = await _httpClient.GetFromJsonAsync<PassingWeb>("passings/mostrecent");
                _logger.LogInformation("Most recent passing found {Passing}", passing?.SourceId);
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
                    _logger.LogError("{Excetion}", ex);
                    throw;
                }
            }

            return passing;
        }

        public async Task<PassingWeb?> GetMostRecentPassing(string Track)
        {
            PassingWeb? passing;

            try
            {
                passing = await _httpClient.GetFromJsonAsync<PassingWeb>($"passings/mostrecent/{Track}");
                _logger.LogInformation("Most recent passing found {Passing}", passing?.SourceId);
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
                    _logger.LogError("{Exception}", ex);
                    throw;
                }
            }

            return passing;
        }

        Task<bool> IApiService.RegisterPassing(PassingRegister passing)
        {
            throw new NotImplementedException();
        }
    }
}
