using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public abstract class GenericController<T> : ControllerBase where T : Entity
    {
        protected VeloTimerDbContext _context;
        protected ILogger<GenericController<T>> _logger;
        protected DbSet<T> _dbset;

        public GenericController(ILogger<GenericController<T>> logger, VeloTimerDbContext context)
        {
            _logger = logger;
            _context = context;
            _dbset = _context.Set<T>();
        }

        [HttpGet]
        public virtual async Task<ActionResult<IEnumerable<T>>> GetAll()
        {
            return await _dbset.AsNoTracking().ToListAsync();
        }

        [HttpGet("{id}")]
        public virtual async Task<ActionResult<T>> Get(long id)
        {
            var value = await _dbset.FindAsync(id);

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [HttpPut("{id}")]
        public virtual async Task<IActionResult> Update(long id, T value)
        {
            if (id != value.Id)
            {
                return BadRequest();
            }

            _context.Entry(value).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Exists(id))
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

        [HttpPost]
        public virtual async Task<ActionResult<T>> Create(T value)
        {
            await _dbset.AddAsync(value);
            await _context.SaveChangesAsync();

            return CreatedAtAction("Get", new { id = value.Id }, value);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var value = await _dbset.FindAsync(id);
            if (value == null)
            {
                return NotFound();
            }

            _dbset.Remove(value);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private Task<bool> Exists(long id)
        {
            return _dbset.AnyAsync(e => e.Id == id);
        }
    }
}
