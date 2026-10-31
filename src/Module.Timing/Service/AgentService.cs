using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public class AgentService(InstallationService installationService, HybridCache cache, TimingDbContext storage, Metrics metrics,
    ILogger<AgentService>? logger = null, ICacheInvalidationOperations? evictionOperations = null) : IAgentService
{
    private readonly ILogger _logger = logger ?? NullLogger<AgentService>.Instance;

    public Task<Passing> RegisterPassingAsync(string agentId, string transponderId, string loopId, DateTime time, string transponderType, CancellationToken cancellationToken = default) =>
        RegisterPassingAsync(agentId, transponderId, loopId, time, transponderType, false, false, false, cancellationToken);

    public async Task<Passing> RegisterPassingAsync(string agentId, string transponderId, string loopId, DateTime time,
        string transponderType, bool lowBattery, bool lowStrength, bool lowHits, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("RegisterPassing");

        var installation =
            await installationService.GetInstallationForAgent(agentId, cancellationToken: cancellationToken) ??
            await installationService.CreateInstallationForAgent(agentId, cancellationToken: cancellationToken);

        var timingPoint =
            await installationService.GetTimingPoint(installation, loopId, cancellationToken: cancellationToken) ??
            await installationService.CreateTimingPoint(installation, loopId, cancellationToken: cancellationToken);

        var transponder =
            await installationService.GetTransponder(transponderId, installation.TimingSystem, cancellationToken: cancellationToken) ??
            await installationService.RegisterTransponder(new MylapsX2Transponder() { SystemId = transponderId }, cancellationToken: cancellationToken);

        Passing? passing = await storage.Set<Passing>()
            .Include(p => p.TimingPoint)
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Time == time && p.TimingPointId == timingPoint.Id && p.TransponderId == transponder.Id, cancellationToken: cancellationToken);

        if (passing is not null)
        {
            activity?.SetTag("Duplicate", true);
            activity?.SetStatus(ActivityStatusCode.Ok, "Duplicate passing detected");
            metrics.PassingProcessed(agentId, activity, true);
        } else
        {
            passing = new()
            {
                Time = time, TimingPointId = timingPoint.Id, TransponderId = transponder.Id,
                LowBattery = lowBattery, LowStrength = lowStrength, LowHits = lowHits
            };
            
            await storage.AddAsync(passing, cancellationToken: cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
                [
                    TimingCacheKeys.LastPassing(timingPoint.InstallationId, transponder.Id),
                    TimingCacheKeys.LegacyLastPassing(transponder.Id)
                ], [TimingCacheTags.LastPassing(timingPoint.InstallationId, transponder.Id)], evictionOperations);
            metrics.PassingProcessed(agentId, activity, false);
        }

        return passing;
    }

    public async Task<Sample?> RegisterSampleAsync(Passing passing, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("RegisterSample");

        var previous = await GetPreviousPassing(passing, cancellationToken);

        Sample? sample = null;

        if (previous is null) return sample;

        var existing = await storage.Set<Sample>()
            .Include(s => s.Start)
            .Include(s => s.End)
            .AsNoTracking()
            .Where(s => s.StartId == previous.Id || s.EndId == passing.Id)
            .ToListAsync(cancellationToken);

        if (existing.Count > 1)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw new InvalidOperationException("Data integrity issue: Multiple samples found for the same passings.");
        }

        if (existing.Count == 1)
        {
            sample = existing[0];
            if (sample.StartId == previous.Id && sample.EndId == passing.Id)
            {
                activity?.SetTag("Duplicate", true);
                return sample; // Sample already exists for this pair of passings
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                throw new InvalidOperationException("Data integrity issue: Sample found with mismatched passings.");
            }
        }

        storage.Entry(passing).State = EntityState.Unchanged;
        storage.Entry(previous).State = EntityState.Unchanged;

        sample = new() { Start = previous, End = passing };

        await storage.AddAsync(sample, cancellationToken: cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);

        return sample;
    }

    private async Task<Passing?> GetPreviousPassing(Passing passing, CancellationToken cancellationToken = default)
    {
        var installation = await installationService.GetInstallionForTimingPoint(passing.TimingPointId, cancellationToken);
        var cacheKey = TimingCacheKeys.LastPassing(installation.Id, passing.TransponderId);

        var value = await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await storage
                    .Set<Passing>()
                    .Include(p => p.TimingPoint)
                    .AsNoTracking()
                    .OrderByDescending(p => p.Time)
                    .FirstOrDefaultAsync(p =>
                        p.TransponderId == passing.TransponderId
                        && p.TimingPoint.InstallationId == installation.Id, cancellationToken: cancel),
            tags: [TimingCacheTags.LastPassing(installation.Id, passing.TransponderId)],
            cancellationToken: cancellationToken);

        if (value is not null && value.Time >= passing.Time)
        {
            value = await storage.Set<Passing>()
                .Include(p => p.TimingPoint)
                .AsNoTracking()
                .OrderByDescending(p => p.Time)
                .FirstOrDefaultAsync(p =>
                    p.TransponderId == passing.TransponderId
                    && p.TimingPoint.InstallationId == installation.Id
                    && p.Time < passing.Time,
                    cancellationToken);
        }

        return value;
    }
}