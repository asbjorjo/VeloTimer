﻿using VeloTime.Storage.Models.Riders;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Parameters;

namespace VeloTime.Services
{
    public interface IRiderService
    {
        Task<Rider> GetRiderByUserId(string userId);
        Task<PaginatedList<Rider>> GetAll(PaginationParameters pagination);
        Task<bool> UpdateRider(Rider rider);
        Task DeleteRider(string userId);
        Task<IEnumerable<Rider>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime);
    }
}