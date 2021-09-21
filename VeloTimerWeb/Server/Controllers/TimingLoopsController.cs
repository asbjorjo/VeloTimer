using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Server.Data;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TimingLoopsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TimingLoopsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/TimingLoops
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TimingLoop>>> GetTimingLoops()
        {
            return await _context.TimingLoops.ToListAsync();
        }

        // GET: api/TimingLoops/5
        [HttpGet("{id}")]
        public async Task<ActionResult<TimingLoop>> GetTimingLoop(long id)
        {
            var timingLoop = await _context.TimingLoops.FindAsync(id);

            if (timingLoop == null)
            {
                return NotFound();
            }

            return timingLoop;
        }

        // PUT: api/TimingLoops/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTimingLoop(long id, TimingLoop timingLoop)
        {
            if (id != timingLoop.Id)
            {
                return BadRequest();
            }

            _context.Entry(timingLoop).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TimingLoopExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/TimingLoops
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<TimingLoop>> PostTimingLoop(TimingLoop timingLoop)
        {
            _context.TimingLoops.Add(timingLoop);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetTimingLoop", new { id = timingLoop.Id }, timingLoop);
        }

        // DELETE: api/TimingLoops/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTimingLoop(long id)
        {
            var timingLoop = await _context.TimingLoops.FindAsync(id);
            if (timingLoop == null)
            {
                return NotFound();
            }

            _context.TimingLoops.Remove(timingLoop);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool TimingLoopExists(long id)
        {
            return _context.TimingLoops.Any(e => e.Id == id);
        }
    }
}
