using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public sealed class TimingAdministrationService(TimingDbContext storage, IAdministrationReferences references, HybridCache cache,
    ILogger<TimingAdministrationService>? logger = null, ICacheInvalidationOperations? evictionOperations = null,
    IMessageBus? messageBus = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<TimingAdministrationService>.Instance;

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
        // The facility edit and its InstallationFacilityChangedV1 outbox row commit together.
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        var installation = id.HasValue ? await storage.Set<Installation>().Include(installation => installation.TimingPoints)
            .SingleOrDefaultAsync(installation => installation.Id == id.Value, cancellationToken) ?? throw new KeyNotFoundException() : null;
        var oldIdentity = installation is null ? null : TimingInstallationCacheIdentity.Capture(installation);
        var system = await ValidateInstallationAsync(id, request, cancellationToken);
        var facilityId = request.FacilityId ?? Guid.Empty;
        if (installation is not null)
        {
            if (installation.LastSeen.HasValue && (installation.AgentId != request.AgentId.Trim() || installation.TimingSystem != system))
                throw new AdministrationConflictException("The identity of an installation already observed by an agent cannot be changed.");
        }
        else
        {
            installation = new Installation { Id = Guid.NewGuid(), AgentId = request.AgentId.Trim() };
            storage.Add(installation);
        }
        installation.AgentId = request.AgentId.Trim();
        installation.TimingSystem = system;
        installation.Description = request.Description?.Trim() ?? "";
        if (installation.Facility != facilityId || request.FacilityValidFromUtc is not null)
        {
            // A facility change is dated, so recorded passings keep the facility they were made at.
            var assigned = await storage.Set<InstallationFacilityAssignment>()
                .AnyAsync(assignment => assignment.InstallationId == installation.Id, cancellationToken);
            var from = request.FacilityValidFromUtc ?? (assigned ? DateTime.UtcNow : null);
            await InstallationFacilityService.AssignAsync(storage, messageBus, installation, facilityId, from, cancellationToken);
        }
        var newIdentity = TimingInstallationCacheIdentity.Capture(installation);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
            (oldIdentity?.Keys ?? []).Concat(newIdentity.Keys),
            (oldIdentity?.Tags ?? []).Concat(newIdentity.Tags), evictionOperations);
        return installation;
    }

    /// <summary>
    /// Asks the installation's agent for a fresh capability report through the Timing outbox. The report arrives
    /// asynchronously; the returned request ID is the command's <c>RequestId</c>.
    /// </summary>
    public async Task<Guid> RequestCapabilityReportAsync(Guid installationId, CancellationToken cancellationToken)
    {
        if (messageBus is null) throw new InvalidOperationException("Messaging is required to send agent commands.");
        var agentId = await storage.Set<Installation>().AsNoTracking()
            .Where(installation => installation.Id == installationId)
            .Select(installation => installation.AgentId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException();
        var requestId = Guid.NewGuid();
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await messageBus.Publish(new ReportCapabilitiesCommandV1(requestId, agentId), cancellationToken: cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return requestId;
    }

    public async Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken)
    {
        var installation = await storage.Set<Installation>().Include(item => item.TimingPoints)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var identity = TimingInstallationCacheIdentity.Capture(installation);
        var count = await storage.Set<TimingPoint>().CountAsync(point => point.InstallationId == id, cancellationToken);
        if (count > 0) throw new AdministrationConflictException($"This installation still has {count} timing point(s). Remove them first.");
        storage.Remove(installation);
        await storage.SaveChangesAsync(cancellationToken);
        await TimingCacheInvalidation.InvalidateAsync(cache, _logger, identity.Keys, identity.Tags, evictionOperations);
    }

    public async Task<TimingPoint> SavePointAsync(Guid installationId, Guid? id, SaveTimingPointRequest request, CancellationToken cancellationToken)
    {
        var installation = await storage.Set<Installation>().FindAsync([installationId], cancellationToken) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.SystemId)) throw new ArgumentException("Hardware system ID is required.");
        if (await storage.Set<TimingPoint>().AnyAsync(point => point.InstallationId == installationId && point.Id != id && point.SystemId == request.SystemId.Trim(), cancellationToken))
            throw new AdministrationConflictException("A timing point in this installation already uses that hardware system ID.");
        var point = id.HasValue ? await storage.Set<TimingPoint>().SingleOrDefaultAsync(point => point.Id == id && point.InstallationId == installationId, cancellationToken)
            ?? throw new KeyNotFoundException() : null;
        var oldPointIdentity = point is null
            ? (TimingPointCacheIdentity?)null
            : new TimingPointCacheIdentity(point.InstallationId, point.Id, point.SystemId);
        if (point is not null)
        {
            if (point.SystemId != request.SystemId.Trim() && await storage.Set<Passing>().AnyAsync(passing => passing.TimingPointId == point.Id, cancellationToken))
                throw new AdministrationConflictException("Recorded passings depend on this hardware system ID. Create a new timing point instead.");
        }
        else
        {
            point = new TimingPoint { Id = Guid.NewGuid(), InstallationId = installationId, Installation = installation, SystemId = request.SystemId.Trim(), Description = "" };
            storage.Add(point);
        }
        point.SystemId = request.SystemId.Trim();
        point.Description = request.Description?.Trim() ?? "";
        var newPointIdentity = new TimingPointCacheIdentity(point.InstallationId, point.Id, point.SystemId);
        var keys = (oldPointIdentity is { } oldPoint ? new[] { oldPoint.CompositeKey, oldPoint.InstallationKey } : [])
            .Concat([newPointIdentity.CompositeKey, newPointIdentity.InstallationKey]);
        await storage.SaveChangesAsync(cancellationToken);
        var tags = (oldPointIdentity is { } prior ? prior.Tags : [])
            .Concat(newPointIdentity.Tags);
        await TimingCacheInvalidation.InvalidateAsync(cache, _logger, keys, tags, evictionOperations);
        return point;
    }

    public async Task DeletePointAsync(Guid installationId, Guid id, CancellationToken cancellationToken)
    {
        var point = await storage.Set<TimingPoint>().Include(point => point.Installation)
            .SingleOrDefaultAsync(point => point.Id == id && point.InstallationId == installationId, cancellationToken) ?? throw new KeyNotFoundException();
        var identity = new TimingPointCacheIdentity(point.InstallationId, point.Id, point.SystemId);
        var count = await storage.Set<Passing>().CountAsync(passing => passing.TimingPointId == id, cancellationToken);
        if (count > 0) throw new AdministrationConflictException($"This timing point has {count} recorded passing(s).");
        var dependencies = await references.GetDependenciesAsync("facilities", "timingpoints", id, cancellationToken);
        if (dependencies.Count > 0) throw new AdministrationConflictException(dependencies.Description);
        storage.Remove(point);
        await storage.SaveChangesAsync(cancellationToken);
        await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
            [identity.CompositeKey, identity.InstallationKey], identity.Tags, evictionOperations);
    }
}