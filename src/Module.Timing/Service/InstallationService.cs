using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public class InstallationService (TimingDbContext storage, HybridCache cache, ILogger<InstallationService>? logger = null,
    ICacheInvalidationOperations? evictionOperations = null)
{
    private static readonly SemaphoreSlim installationSemaphore = new(1, 1);
    private static readonly SemaphoreSlim timingPointSemaphore = new(1, 1);
    private static readonly SemaphoreSlim transponderSemaphore = new(1, 1);
    private readonly ILogger _logger = logger ?? NullLogger<InstallationService>.Instance;

    public async Task<IEnumerable<Installation>> GetAllInstallations(CancellationToken cancellationToken = default)
    {
        return await storage.Set<Installation>().AsNoTracking().ToListAsync(cancellationToken: cancellationToken);
    }

    public async Task<Installation> CreateInstallationForAgent(string agentId, TimingSystem timingSystem = TimingSystem.Unknown, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("FindOrCreateInstallationForAgent");
        activity?.SetTag("agentId", agentId);
        await installationSemaphore.WaitAsync(cancellationToken: cancellationToken);

        try
        {
            var installation = await GetInstallationForAgent(agentId, cancellationToken);

            if (installation is null)
            {
                installation = new()
                {
                    AgentId = agentId,
                    TimingSystem = timingSystem
                };
                await storage.AddAsync(installation, cancellationToken: cancellationToken);
                await storage.SaveChangesAsync(cancellationToken: cancellationToken);
                await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
                    [TimingCacheKeys.Installation(agentId)], [TimingCacheTags.InstallationAgent(agentId)], evictionOperations);
            }

            activity?.SetTag("installationId", installation.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return installation;
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            installationSemaphore.Release();
        }
    }

    public async Task<TimingPoint> CreateTimingPoint(Installation installation, string loopId, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("FindOrCreateTimingPoint");
        activity?.SetTag("installationId", installation.Id);
        activity?.SetTag("loopId", loopId);

        await timingPointSemaphore.WaitAsync();
        
        try
        {
            var trackedInstallation = storage.Set<Installation>().Local.FirstOrDefault(item => item.Id == installation.Id);
            if (trackedInstallation is not null)
                storage.Entry(trackedInstallation).State = EntityState.Detached;

            var timingPoint = await GetTimingPoint(installation, loopId, cancellationToken: cancellationToken);
            if (timingPoint == null)
            {
                timingPoint = new()
                {
                    SystemId = loopId,
                    Description = "",
                    // The Installation supplied by callers can be a cached shared instance.
                    // Persist only the FK; never attach or mutate that instance through a nav.
                    Installation = null!,
                    InstallationId = installation.Id
                };
                await storage.AddAsync(timingPoint, cancellationToken: cancellationToken);
                await storage.SaveChangesAsync(cancellationToken: cancellationToken);
                var identity = new TimingPointCacheIdentity(installation.Id, timingPoint.Id, timingPoint.SystemId);
                await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
                [
                    TimingCacheKeys.Installation(installation.AgentId),
                    identity.CompositeKey,
                    identity.InstallationKey
                ], identity.Tags.Concat([TimingCacheTags.InstallationAgent(installation.AgentId)]), evictionOperations);
            }

            activity?.SetTag("timingPointId", timingPoint.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            // Return a persisted, independently writable object for both cache-hit and
            // create paths, not the cached query result or the instance used for insert.
            return await GetWritableTimingPointByIdAsync(timingPoint.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            timingPointSemaphore.Release();
        }
    }

    public async Task<Installation?> GetInstallationForAgent(string agentId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            TimingCacheKeys.Installation(agentId),
            async cancel =>  await storage.Set<Installation>()
                .AsNoTracking()
                .SingleOrDefaultAsync(i => i.AgentId == agentId, cancellationToken: cancel),
            tags: [TimingCacheTags.InstallationAgent(agentId)],
            cancellationToken: cancellationToken
            );
    }

    public async Task<Installation> GetInstallionForTimingPoint(Guid timingPoint, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            TimingCacheKeys.InstallationForTimingPoint(timingPoint),
            async cancel => await storage.Set<TimingPoint>()
                .AsNoTracking()
                .Where(tp => tp.Id == timingPoint)
                .Select(tp => tp.Installation)
                .SingleOrDefaultAsync(cancellationToken: cancel),
            tags: [TimingCacheTags.TimingPointId(timingPoint)],
            cancellationToken: cancellationToken
        ) ?? throw new InvalidOperationException($"No installation found for timing point {timingPoint}");
    }

    public async Task<TimingPoint?> GetTimingPoint(Installation installation, string loopSystemId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            TimingCacheKeys.TimingPoint(installation.Id, loopSystemId),
            async cancel => await storage.Set<TimingPoint>()
                .AsNoTracking()
                .SingleOrDefaultAsync(tp => tp.Installation == installation && tp.SystemId == loopSystemId, cancellationToken: cancel),
            tags:
            [
                TimingCacheTags.Installation(installation.Id),
                TimingCacheTags.TimingPointHardware(installation.Id, loopSystemId)
            ],
            cancellationToken: cancellationToken
        );
    }

    public async Task<Transponder?> GetTransponder(string systemId, TimingSystem timingSystem, CancellationToken cancellationToken = default)
    {
        var activity = Instrumentation.Source.StartActivity("GetTransponder");
        activity?.SetTag("systemId", systemId);
        activity?.SetTag("timingSystem", timingSystem);

        return await cache.GetOrCreateAsync(
            TimingCacheKeys.Transponder(timingSystem, systemId),
            async cancel => await storage.Set<Transponder>()
                .SingleOrDefaultAsync(t => t.System == timingSystem && t.SystemId == systemId, cancellationToken: cancel),
            tags: [TimingCacheTags.Transponder(timingSystem, systemId)],
            cancellationToken: cancellationToken
        );
    }

    public async Task<Transponder> RegisterTransponder(Transponder transponder, CancellationToken cancellationToken = default)
    {
        var activity = Instrumentation.Source.StartActivity("RegisterTransponder");

        await transponderSemaphore.WaitAsync(cancellationToken: cancellationToken);

        try
        {        
            var transponderEntry = await GetTransponder(transponder.SystemId, transponder.System, cancellationToken);

            if (transponderEntry is null)
            {
                transponderEntry = transponder;
                await storage.AddAsync(transponderEntry, cancellationToken: cancellationToken);
                await storage.SaveChangesAsync(cancellationToken: cancellationToken);
                await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
                    [TimingCacheKeys.Transponder(transponderEntry.System, transponderEntry.SystemId)],
                    [TimingCacheTags.Transponder(transponderEntry.System, transponderEntry.SystemId)], evictionOperations);
            }

            activity?.SetTag("transponderId", transponderEntry.Id);

            return transponderEntry;
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            transponderSemaphore.Release();
        }
    }

    public Task<Passing?> GetLastPassingForTransponder(Transponder transponder, Installation installation, CancellationToken cancellationToken = default)
    {
        return storage.Set<Passing>()
            .AsNoTracking()
            .OrderByDescending(p => p.Time)
            .FirstOrDefaultAsync(p =>
                p.Transponder == transponder
                && p.TimingPoint.Installation == installation, cancellationToken: cancellationToken);
    }

    public async Task UpdateLastSeenAsync(Installation installation, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        // Never mutate an object supplied by a caller: it may be the mutable entity
        // instance held by HybridCache. Load persisted state into a separate tracked entity.
        var installationToUpdate = await GetWritableInstallationByIdAsync(installation.Id, cancellationToken);

        if (installationToUpdate.LastSeen >= timestamp)
            return;
        var pointIdentities = await storage.Set<TimingPoint>()
            .Where(point => point.InstallationId == installationToUpdate.Id)
            .Select(point => new TimingPointCacheIdentity(point.InstallationId, point.Id, point.SystemId))
            .ToListAsync(cancellationToken);
        installationToUpdate.LastSeen = timestamp;
        storage.Attach(installationToUpdate);
        storage.Entry(installationToUpdate).Property(item => item.LastSeen).IsModified = true;
        var identity = new TimingInstallationCacheIdentity(installationToUpdate.Id, installationToUpdate.AgentId, pointIdentities.ToImmutableArray());
        await storage.SaveChangesAsync(cancellationToken);
        await TimingCacheInvalidation.InvalidateAsync(cache, _logger, identity.Keys, identity.Tags, evictionOperations);
    }

    /// <summary>
    /// Loads a detached writable Installation snapshot from persisted state, replacing
    /// any currently tracked instance with that ID (which may be a cache-owned entity).
    /// The caller can attach it explicitly for persistence; it has no loaded navigations.
    /// </summary>
    public async Task<Installation> GetWritableInstallationByIdAsync(Guid installationId, CancellationToken cancellationToken = default)
    {
        var tracked = storage.Set<Installation>().Local.FirstOrDefault(item => item.Id == installationId);
        if (tracked is not null)
            storage.Entry(tracked).State = EntityState.Detached;

        var installation = await storage.Set<Installation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == installationId, cancellationToken)
            ?? throw new InvalidOperationException($"Installation {installationId} no longer exists.");

        return installation;
    }

    /// <summary>
    /// Loads a detached writable TimingPoint snapshot from persisted state, replacing
    /// any currently tracked instance with that ID (which may be a cache-owned entity).
    /// Its navigation is not loaded from or aliased to a cached Installation. The caller
    /// can attach it explicitly for persistence.
    /// </summary>
    public async Task<TimingPoint> GetWritableTimingPointByIdAsync(Guid timingPointId, CancellationToken cancellationToken = default)
    {
        var tracked = storage.Set<TimingPoint>().Local.FirstOrDefault(item => item.Id == timingPointId);
        if (tracked is not null)
            storage.Entry(tracked).State = EntityState.Detached;

        var timingPoint = await storage.Set<TimingPoint>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == timingPointId, cancellationToken)
            ?? throw new InvalidOperationException($"Timing point {timingPointId} no longer exists.");

        return timingPoint;
    }
}
