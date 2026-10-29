using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public sealed class TimingAdministrationService(TimingDbContext storage, IAdministrationReferences references, HybridCache cache)
{
    public async Task<TimingSystem> ValidateInstallationAsync(Guid? id, SaveInstallationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId)) throw new ArgumentException("Agent ID is required.");
        if (!Enum.TryParse<TimingSystem>(request.TimingSystem, out var system) || !Enum.IsDefined(system)) throw new ArgumentException("Select a supported timing system.");
        if (await storage.Set<Installation>().AnyAsync(installation => installation.Id != id && installation.AgentId == request.AgentId.Trim(), cancellationToken))
            throw new AdministrationConflictException("An installation already uses this agent ID.");
        if (request.FacilityId is { } facilityId && (facilityId == Guid.Empty || await references.GetAsync("facilities", "facilities", facilityId, cancellationToken) is null))
            throw new ArgumentException("Select an existing facility, or leave the installation unassigned.");
        return system;
    }

    public async Task<Installation> SaveInstallationAsync(Guid? id, SaveInstallationRequest request, CancellationToken cancellationToken)
    {
        var installation = id.HasValue ? await storage.Set<Installation>().Include(installation => installation.TimingPoints)
            .SingleOrDefaultAsync(installation => installation.Id == id.Value, cancellationToken) ?? throw new KeyNotFoundException() : null;
        var system = await ValidateInstallationAsync(id, request, cancellationToken);
        var facilityId = request.FacilityId ?? Guid.Empty;
        if (installation is not null)
        {
            if (installation.LastSeen.HasValue && (installation.AgentId != request.AgentId.Trim() || installation.TimingSystem != system))
                throw new AdministrationConflictException("The identity of an installation already observed by an agent cannot be changed.");
            if (installation.Facility != facilityId)
            {
                if (await storage.Set<Passing>().AnyAsync(passing => passing.TimingPoint.InstallationId == installation.Id, cancellationToken))
                    throw new AdministrationConflictException("Recorded passings depend on this installation's facility. Create a new installation instead.");
                foreach (var point in installation.TimingPoints)
                {
                    var dependencies = await references.GetDependenciesAsync("facilities", "timingpoints", point.Id, cancellationToken);
                    if (dependencies.Count > 0) throw new AdministrationConflictException(dependencies.Description);
                }
            }
            await InvalidateInstallation(installation, cancellationToken);
        }
        else
        {
            installation = new Installation { Id = Guid.NewGuid(), AgentId = request.AgentId.Trim() };
            storage.Add(installation);
        }
        installation.AgentId = request.AgentId.Trim();
        installation.TimingSystem = system;
        installation.Facility = facilityId;
        installation.Description = request.Description?.Trim() ?? "";
        await storage.SaveChangesAsync(cancellationToken);
        await InvalidateInstallation(installation, cancellationToken);
        return installation;
    }

    public async Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken)
    {
        var installation = await storage.Set<Installation>().FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        var count = await storage.Set<TimingPoint>().CountAsync(point => point.InstallationId == id, cancellationToken);
        if (count > 0) throw new AdministrationConflictException($"This installation still has {count} timing point(s). Remove them first.");
        storage.Remove(installation);
        await storage.SaveChangesAsync(cancellationToken);
        await InvalidateInstallation(installation, cancellationToken);
    }

    public async Task<TimingPoint> SavePointAsync(Guid installationId, Guid? id, SaveTimingPointRequest request, CancellationToken cancellationToken)
    {
        var installation = await storage.Set<Installation>().FindAsync([installationId], cancellationToken) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.SystemId)) throw new ArgumentException("Hardware system ID is required.");
        if (await storage.Set<TimingPoint>().AnyAsync(point => point.InstallationId == installationId && point.Id != id && point.SystemId == request.SystemId.Trim(), cancellationToken))
            throw new AdministrationConflictException("A timing point in this installation already uses that hardware system ID.");
        var point = id.HasValue ? await storage.Set<TimingPoint>().SingleOrDefaultAsync(point => point.Id == id && point.InstallationId == installationId, cancellationToken)
            ?? throw new KeyNotFoundException() : null;
        if (point is not null)
        {
            if (point.SystemId != request.SystemId.Trim() && await storage.Set<Passing>().AnyAsync(passing => passing.TimingPointId == point.Id, cancellationToken))
                throw new AdministrationConflictException("Recorded passings depend on this hardware system ID. Create a new timing point instead.");
            await cache.RemoveAsync($"timingpoint_{installationId}_{point.SystemId}", cancellationToken);
        }
        else
        {
            point = new TimingPoint { Id = Guid.NewGuid(), InstallationId = installationId, Installation = installation, SystemId = request.SystemId.Trim(), Description = "" };
            storage.Add(point);
        }
        point.SystemId = request.SystemId.Trim();
        point.Description = request.Description?.Trim() ?? "";
        await storage.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"timingpoint_{installationId}_{point.SystemId}", cancellationToken);
        await InvalidateInstallation(installation, cancellationToken);
        return point;
    }

    public async Task DeletePointAsync(Guid installationId, Guid id, CancellationToken cancellationToken)
    {
        var point = await storage.Set<TimingPoint>().Include(point => point.Installation)
            .SingleOrDefaultAsync(point => point.Id == id && point.InstallationId == installationId, cancellationToken) ?? throw new KeyNotFoundException();
        var count = await storage.Set<Passing>().CountAsync(passing => passing.TimingPointId == id, cancellationToken);
        if (count > 0) throw new AdministrationConflictException($"This timing point has {count} recorded passing(s).");
        var dependencies = await references.GetDependenciesAsync("facilities", "timingpoints", id, cancellationToken);
        if (dependencies.Count > 0) throw new AdministrationConflictException(dependencies.Description);
        storage.Remove(point);
        await storage.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"timingpoint_{installationId}_{point.SystemId}", cancellationToken);
        await cache.RemoveAsync($"timingpoint_{id}", cancellationToken);
        await InvalidateInstallation(point.Installation, cancellationToken);
    }

    private async Task InvalidateInstallation(Installation installation, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync($"installation_{installation.AgentId}", cancellationToken);
        foreach (var point in installation.TimingPoints)
            await cache.RemoveAsync($"timingpoint_{point.Id}", cancellationToken);
    }
}