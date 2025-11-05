using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;

namespace VeloTime.Module.Timing.Controllers;

[ApiController]
[Route("api/timing/[controller]")]
public class InstallationController : ControllerBase
{
    private readonly ILogger<InstallationController> _logger;
    private readonly TimingDbContext _dbContext;

    public InstallationController(ILogger<InstallationController> logger, TimingDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<InstallationDto>> List()
    {
        var installations = await _dbContext.Set<Installation>().Include(i => i.TimingPoints).Include(i => i.TimingSystem).ToListAsync();
        var installationData = installations.Select(i => i.ToDto());

        return Ok(installationData);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<InstallationDto>> Get(Guid id)
    {
        var installation = await _dbContext.Set<Installation>()
            .Include(i => i.TimingPoints)
            .Include(i => i.TimingSystem)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (installation == null)
        {
            return NotFound();
        }

        InstallationDto installationData = installation.ToDto();

        return Ok(installationData);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] InstallationDto installationData)
    {
        var timingSystem = await _dbContext.Set<TimingSystem>().FindAsync(installationData.TimingSystem.Id);

        if (timingSystem == null)
        {
            timingSystem = new TimingSystem
            {
                Id = installationData.TimingSystem.Id,
                Name = installationData.TimingSystem.Name
            };
        }

        Installation installation = new()
        {
            Id = installationData.Id,
            Facility = installationData.Facility,
            Description = installationData.Description,
            TimingSystem = timingSystem
        };
        var timingPoints = installationData.TimingPoints.Select(tp => new TimingPoint
        {
            Id = tp.Id,
            SystemId = tp.SystemId,
            Installation = installation,
            Description = tp.Description,
        }).ToList();
        installation.TimingPoints.AddRange(timingPoints);

        await _dbContext.AddAsync(installation);
        
        await _dbContext.SaveChangesAsync();

        installationData = installation.ToDto();

        return CreatedAtAction(nameof(Get), new { id = installationData.Id }, installationData);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] InstallationDto installationData)
    {
        var timingSystem = await _dbContext.Set<TimingSystem>().FindAsync(installationData.TimingSystem);

        var installation = await _dbContext.Set<Installation>()
            .Include(i => i.TimingPoints)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (installation == null)
        {
            return NotFound();
        }

        installation.Description = installationData.Description;
 
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var installation = await _dbContext.Set<Installation>().FindAsync(id);
        if (installation == null)
        {
            return NotFound();
        }
        _dbContext.Set<Installation>().Remove(installation);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }
}
