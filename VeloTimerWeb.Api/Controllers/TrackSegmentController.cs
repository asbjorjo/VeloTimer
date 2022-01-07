using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.TrackSetup;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.TrackSetup;

namespace VeloTimerWeb.Api.Controllers
{
    public class TrackSegmentController : BaseController
    {
        private readonly VeloTimerDbContext _context;
        
        public TrackSegmentController(IMapper mapper, VeloTimerDbContext context, ILogger<TrackSegmentController> logger) : base(mapper, logger)
        {
            _context = context;
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
