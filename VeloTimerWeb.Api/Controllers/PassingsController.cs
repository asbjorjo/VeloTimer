using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Api.Data;
using VeloTimer.Shared.Models;
using Microsoft.Extensions.Logging;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PassingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PassingsController> _logger;

        public PassingsController(ApplicationDbContext context, ILogger<PassingsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/TimingLoops
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Passing>>> GetPassings()
        {
            return await _context.Passings.ToListAsync();
        }

        // GET: api/TimingLoops/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Passing>> GetPassing(long id)
        {
            var timingLoop = await _context.Passings.FindAsync(id);

            if (timingLoop == null)
            {
                return NotFound();
            }

            return timingLoop;
        }

        // PUT: api/TimingLoops/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPassing(long id, Passing passing)
        {
            if (id != passing.Id)
            {
                return BadRequest();
            }

            _context.Entry(passing).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PassingExists(id))
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
        public async Task<ActionResult<Passing>> PostPassing(Passing passing)
        {
            _context.Passings.Add(passing);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetPassing", new { id = passing.Id }, passing);
        }

        // DELETE: api/TimingLoops/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePassing(long id)
        {
            var passing = await _context.Passings.FindAsync(id);
            if (passing == null)
            {
                return NotFound();
            }

            _context.Passings.Remove(passing);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool PassingExists(long id)
        {
            return _context.Passings.Any(e => e.Id == id);
        }
    }
}
