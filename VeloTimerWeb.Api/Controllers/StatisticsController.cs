using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatisticsController : ControllerBase
    {
        private readonly IStatisticsService _service;
        private readonly ILogger<StatisticsController> _logger;

        public StatisticsController(IStatisticsService statisticsService, ILogger<StatisticsController> logger)
        {
            _service = statisticsService;
            _logger = logger;
        }
    }
}
