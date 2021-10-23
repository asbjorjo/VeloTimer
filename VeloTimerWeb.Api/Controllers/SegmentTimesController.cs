using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class SegmentTimesController : ControllerBase
    {
        private readonly ILogger<SegmentTimesController> _logger;
        private readonly ISegmentTimeService _segmentTimes;

        public SegmentTimesController(ISegmentTimeService segmentTimes, ILogger<SegmentTimesController> logger)
        {
            _logger = logger;
            _segmentTimes = segmentTimes;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> Get(long segmentId, long? transponderId)
        {
            var segmenttimess = await _segmentTimes.GetSegmentTimesAsync(segmentId, transponderId);

            return segmenttimess.ToList();
        }
    }
}
