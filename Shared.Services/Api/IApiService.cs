﻿using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.Shared.Services.Api
{
    public interface IApiService
    {
        Task<PassingWeb> GetMostRecentPassing();
        Task<PassingWeb> GetMostRecentPassing(string Track);
        Task<bool> RegisterPassing(PassingRegister passing);
    }
}