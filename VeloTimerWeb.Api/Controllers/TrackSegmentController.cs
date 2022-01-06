using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrackSegmentController : ControllerBase
    {
        private readonly IMapper _mapper;
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<TrackSegmentController> _logger;

        public TrackSegmentController(IMapper mapper, VeloTimerDbContext context, ILogger<TrackSegmentController> logger)
        {
            _mapper = mapper;
            _context = context;
            _logger = logger;
        }

        public async Task<ActionResult<IEnumerable<TrackSegmentWeb>>> GetForTrack(string Track)
        {
            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));

            if (track == null)
            {
                return NotFound();
            }

            var segments = await _context.Set<TrackSegment>().Where(x => x.Start.Track == track).ToListAsync();

            return _mapper.Map<List<TrackSegmentWeb>>(segments);
        }
    }
}
