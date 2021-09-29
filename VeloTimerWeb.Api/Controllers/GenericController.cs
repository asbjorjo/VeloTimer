using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [ApiController]
    public abstract class GenericController<T> : ControllerBase where T : Entity
    {
        protected ApplicationDbContext _context;
        protected ILogger<GenericController<T>> _logger;

        public GenericController(ILogger<GenericController<T>> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<T>>> GetAll()
        {
            return await _context.Set<T>().ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<T>> Get(long id)
        {
            var value = await _context.Set<T>().FindAsync(id);

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, T value)
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
                if (!Exists(id))
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
        public async Task<ActionResult<Passing>> Create(T value)
        {
            await _context.Set<T>().AddAsync(value);
            await _context.SaveChangesAsync();

            return CreatedAtAction("Get", new { id = value.Id }, value);
        }

        [HttpPost]
        public async Task<ActionResult> Create([FromBody] IEnumerable<T> values)
        {
            await _context.Set<T>().AddRangeAsync(values);
            await _context.SaveChangesAsync();

            return Accepted();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var value = await _context.Set<T>().FindAsync(id);
            if (value == null)
            {
                return NotFound();
            }

            _context.Set<T>().Remove(value);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool Exists(long id)
        {
            return _context.Set<T>().Any(e => e.Id == id);
        }
    }
}
