using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Handlers;

public class InstallationLayoutHandler(InstallationService installationService, TimingDbContext storage, ILogger<InstallationLayoutHandler> log, HybridCache cache, ICacheInvalidationOperations operations) : IConsumer<InstallationLayoutEvent>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    public async Task OnHandle(InstallationLayoutEvent message, CancellationToken cancellationToken)
    {
        object? agentIdObj = Context.GetTransportMessage().ApplicationProperties.GetValueOrDefault("AgentId", string.Empty);
        string AgentId = agentIdObj?.ToString() ?? string.Empty;

        Installation installationLookup =
            await installationService.GetInstallationForAgent(AgentId, cancellationToken: cancellationToken) ??
            await installationService.CreateInstallationForAgent(AgentId, cancellationToken: cancellationToken);


        IEnumerable<SystemConfigEvent> systems = message.Systems.Where(s => s.IsActive);

        if (systems.Count() > 1)
        {
            log.LogWarning("Multiple active systems found for AgentId {AgentId}. Only the first one will be used.", AgentId);
        }

        var system = systems.First();

        var installation = await installationService.GetWritableInstallationByIdAsync(installationLookup.Id, cancellationToken);

        // The cached installation need not have its points loaded. Include persisted
        // points so their point-to-installation cache entries are invalidated too.
        var existingPoints = await storage.Set<TimingPoint>()
            .AsNoTracking()
            .Where(point => point.InstallationId == installation.Id)
            .ToListAsync(cancellationToken);
        List<TimingInstallationCacheIdentity> oldIdentities =
            [TimingInstallationCacheIdentity.Capture(installation, existingPoints)];
        Dictionary<Guid, TimingPoint> touchedPoints = [];
        List<(TimingPoint Point, LoopConfigEvent Loop)> layoutPoints = [];

        // Complete every potentially committing create before any metadata is
        // changed or attached. Cached lookup results are used only for their IDs.
        foreach (LoopConfigEvent loop in message.TimingLoops.Where(l => l.SystemId == system.SystemId))
        {
            TimingPoint pointLookup =
                await installationService.GetTimingPoint(installation, loop.LoopId, cancellationToken: cancellationToken) ??
                await installationService.CreateTimingPoint(installation, loop.LoopId, cancellationToken: cancellationToken);

            var timingPoint = await installationService.GetWritableTimingPointByIdAsync(pointLookup.Id, cancellationToken);
            oldIdentities.Add(TimingInstallationCacheIdentity.Capture(installation, [timingPoint]));
            touchedPoints.TryAdd(timingPoint.Id, timingPoint);
            layoutPoints.Add((touchedPoints[timingPoint.Id], loop));
        }

        if (string.IsNullOrWhiteSpace(installation.Description))
        {
            installation.Description = system.Description;
            storage.Entry(installation).State = EntityState.Modified;
        }

        if (installation.TimingSystem == TimingSystem.Unknown)
        {
            installation.TimingSystem = TimingSystem.MyLaps_X2;
            storage.Entry(installation).State = EntityState.Modified;
        }

        // Reuse one writable instance per point and retain message order so the
        // first nonblank description still wins for duplicate loop entries.
        foreach (var (timingPoint, loop) in layoutPoints)
        {
            if (string.IsNullOrWhiteSpace(timingPoint.Description))
            {
                timingPoint.Description = loop.Name;
            }
            storage.Entry(timingPoint).State = EntityState.Modified;
        }

        await storage.SaveChangesAsync(cancellationToken: cancellationToken);

        var newIdentity = TimingInstallationCacheIdentity.Capture(installation, touchedPoints.Values.Concat(existingPoints));
        await TimingCacheInvalidation.InvalidateAsync(cache, log,
            oldIdentities.SelectMany(identity => identity.Keys).Concat(newIdentity.Keys),
            oldIdentities.SelectMany(identity => identity.Tags).Concat(newIdentity.Tags), operations);
    }
}