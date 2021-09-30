using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class LapTimesController : ControllerBase
    {
        private readonly ILogger<LapTimesController> _logger;
        private readonly ISegmentTimeService _segmentTimes;

        public LapTimesController(ISegmentTimeService segmentTimes, ILogger<LapTimesController> logger)
        {
            _logger = logger;
            _segmentTimes = segmentTimes;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<LapTime>>> Get(long? startLoop, long? endLoop, long? transponderId)
        {
            long start = startLoop ?? 1;
            long finish = endLoop ?? start;

            var segments = await _segmentTimes.GetSegmentTimesAsync(start, finish, transponderId);

            segments = segments.Where(s => s.Lapspeed > 5);

            return segments.ToList();
        }
    }
}
