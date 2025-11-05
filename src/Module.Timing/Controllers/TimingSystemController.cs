using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Controllers;

[ApiController]
[Route("api/timing/[controller]")]
public class TimingSystemController : ControllerBase
{
    private readonly ILogger<InstallationController> _logger;
    private readonly TimingDbContext _dbContext;

    public TimingSystemController(ILogger<InstallationController> logger, TimingDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<TimingSystemDto>> List()
    {
        var systems = await _dbContext.Set<TimingSystem>().ToListAsync();

        var systemsData = systems.Select(s => s.ToDto());

        return Ok(systemsData);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TimingSystemDto>> Get(Guid id)
    {
        var system = await _dbContext.Set<TimingSystem>().FindAsync(id);

        if (system == null)
        {
            return NotFound();
        }

        return Ok(system.ToDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TimingSystemDto systemData)
    {
        TimingSystem system = systemData.ToModel();

        if (_dbContext.Set<TimingSystem>().Any(s => s.Id == system.Id))
        {
            return Conflict("Timing system with the same ID already exists.");
        }

        await _dbContext.AddAsync(system);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = system.Id }, system.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TimingSystemDto systemData)
    {
        var system = await _dbContext.Set<TimingSystem>().FindAsync(id);

        if (system == null)
        {
            return NotFound();
        }

        system.Name = systemData.Name;

        _dbContext.Update(system);
        await _dbContext.SaveChangesAsync();

        return Ok(system.ToDto());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var system = await _dbContext.Set<TimingSystem>().Include(t => t.Installations).FirstOrDefaultAsync(t => t.Id == id);
        if (system == null)
        {
            return NotFound();
        }

        if (system.Installations.Any())
        {
            return BadRequest("Cannot delete timing system with existing installations.");
        }

        _dbContext.Remove(system);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }
}
