using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using System.Diagnostics;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public class InstallationService (TimingDbContext storage, HybridCache cache)
{
    private static readonly SemaphoreSlim installationSemaphore = new(1, 1);
    private static readonly SemaphoreSlim timingPointSemaphore = new(1, 1);
    private static readonly SemaphoreSlim transponderSemaphore = new(1, 1);

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
            }

            await cache.SetAsync($"installation_{agentId}", installation, cancellationToken: cancellationToken);

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
            var timingPoint = await GetTimingPoint(installation, loopId, cancellationToken: cancellationToken);
            if (timingPoint == null)
            {
                timingPoint = new()
                {
                    SystemId = loopId,
                    Description = "",
                    Installation = installation
                };
                await storage.AddAsync(timingPoint, cancellationToken: cancellationToken);
                await storage.SaveChangesAsync(cancellationToken: cancellationToken);
            }

            await cache.SetAsync($"timingpoint_{installation.Id}_{timingPoint.SystemId}", timingPoint, cancellationToken: cancellationToken);

            activity?.SetTag("timingPointId", timingPoint.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return timingPoint;
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
            $"installation_{agentId}",
            async cancel =>  await storage.Set<Installation>()
                .SingleOrDefaultAsync(i => i.AgentId == agentId, cancellationToken: cancel),
            cancellationToken: cancellationToken
            );
    }

    public async Task<Installation> GetInstallionForTimingPoint(Guid timingPoint, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"timingpoint_{timingPoint}",
            async cancel => await storage.Set<TimingPoint>()
                .Where(tp => tp.Id == timingPoint)
                .Select(tp => tp.Installation)
                .SingleOrDefaultAsync(cancellationToken: cancel),
            cancellationToken: cancellationToken
        ) ?? throw new InvalidOperationException($"No installation found for timing point {timingPoint}");
    }

    public async Task<TimingPoint?> GetTimingPoint(Installation installation, string loopSystemId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"timingpoint_{installation.Id}_{loopSystemId}",
            async cancel => await storage.Set<TimingPoint>()
                .SingleOrDefaultAsync(tp => tp.Installation == installation && tp.SystemId == loopSystemId, cancellationToken: cancel),
            cancellationToken: cancellationToken
        );
    }

    public async Task<Transponder?> GetTransponder(string systemId, TimingSystem timingSystem, CancellationToken cancellationToken = default)
    {
        var activity = Instrumentation.Source.StartActivity("GetTransponder");
        activity?.SetTag("systemId", systemId);
        activity?.SetTag("timingSystem", timingSystem);

        return await cache.GetOrCreateAsync(
            $"transponder_{timingSystem}_{systemId}",
            async cancel => await storage.Set<Transponder>()
                .SingleOrDefaultAsync(t => t.System == timingSystem && t.SystemId == systemId, cancellationToken: cancel),
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
            }

            activity?.SetTag("transponderId", transponderEntry.Id);

            await cache.SetAsync($"transponder_{transponderEntry.System}_{transponderEntry.SystemId}", transponderEntry, cancellationToken: cancellationToken);

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
}
