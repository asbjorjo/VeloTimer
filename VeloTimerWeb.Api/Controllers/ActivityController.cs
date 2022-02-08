using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTime.Storage.Models.Statistics;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models.Statistics;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ActivityController : BaseController
    {
        private readonly IActivityService _activityService;

        public ActivityController(
            IActivityService activityService,
            ILogger<ActivityController> logger,
            IMapper mapper) : base(mapper, logger)
        {
            _activityService = activityService;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("")]
        [Route("user/{RiderId}")]
        public async Task<ActionResult<IEnumerable<ActivityWeb>>> GetActivities(
            [FromQuery] PaginationParameters pagination,
            string RiderId = "")
        {
            PaginatedList<Activity> activities;

            if (string.IsNullOrWhiteSpace(RiderId))
            {
                activities = await _activityService.GetActivities(pagination);
            } else
            {
                activities = await _activityService.GetActivitesForRider(RiderId, pagination);
            }

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(activities.Pagination));
            return _mapper.Map<List<ActivityWeb>>(activities);
        }
    }
}
