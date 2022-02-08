﻿using System.Threading.Tasks;
using VeloTime.Storage.Models.Statistics;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Parameters;

namespace VeloTimerWeb.Api.Services
{
    public interface IActivityService
    {
        Task<PaginatedList<Activity>> GetActivities(PaginationParameters pagination);
        Task<PaginatedList<Activity>> GetActivitesForRider(string RiderId, PaginationParameters pagination);
    }
}
