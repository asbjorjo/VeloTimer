using System.Collections.Immutable;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Service;

public static class TimingCacheKeys
{
    public static string Installation(string agentId) => $"velotime:timing:installation:v1:{agentId}";
    public static string TimingPoint(Guid installationId, string systemId) => $"velotime:timing:timing-point:v1:{installationId}:{systemId}";

    // This existing key stores an Installation value, not a TimingPoint.
    public static string InstallationForTimingPoint(Guid timingPointId) => $"velotime:timing:installation-for-timing-point:v1:{timingPointId}";

    public static string Transponder(TimingSystem system, string systemId) => $"velotime:timing:transponder:v1:{system}:{systemId}";
    // v2 stores the installation-scoped latest passing; never interpret the legacy threshold-scoped value.
    public static string LastPassing(Guid installationId, Guid transponderId) => $"velotime:timing:last-passing:v2:{installationId}:{transponderId}";
    public static string LegacyLastPassing(Guid transponderId) => $"LastPassing_{transponderId}";

    public static HybridCacheEntryOptions NewEntryOptions() => new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };
}

public static class TimingCacheTags
{
    public static string InstallationAgent(string agentId) => $"Timing.Installation.Agent.{agentId}";
    public static string Installation(Guid installationId) => $"Timing.Installation.{installationId}";
    public static string TimingPointHardware(Guid installationId, string systemId) => $"Timing.TimingPoint.Hardware.{installationId}.{systemId}";
    public static string TimingPointId(Guid timingPointId) => $"Timing.TimingPoint.Id.{timingPointId}";
    public static string Transponder(TimingSystem system, string systemId) => $"Timing.Transponder.{system}.{systemId}";
    public static string LastPassing(Guid installationId, Guid transponderId) => $"Timing.LastPassing.{installationId}.{transponderId}";
}

public readonly record struct TimingPointCacheIdentity(Guid InstallationId, Guid TimingPointId, string SystemId)
{
    public string CompositeKey => TimingCacheKeys.TimingPoint(InstallationId, SystemId);
    public string InstallationKey => TimingCacheKeys.InstallationForTimingPoint(TimingPointId);
    public IEnumerable<string> Tags =>
    [
        TimingCacheTags.Installation(InstallationId),
        TimingCacheTags.TimingPointHardware(InstallationId, SystemId),
        TimingCacheTags.TimingPointId(TimingPointId)
    ];
}

public readonly record struct TimingTransponderCacheIdentity(TimingSystem System, string SystemId)
{
    public string Key => TimingCacheKeys.Transponder(System, SystemId);
    public string Tag => TimingCacheTags.Transponder(System, SystemId);
}

public sealed record TimingInstallationCacheIdentity(Guid InstallationId, string AgentId, ImmutableArray<TimingPointCacheIdentity> TimingPoints)
{
    public static TimingInstallationCacheIdentity Capture(Installation installation) =>
        Capture(installation, []);

    public static TimingInstallationCacheIdentity Capture(Installation installation, IEnumerable<TimingPoint> touchedPoints) =>
        new(installation.Id, installation.AgentId, touchedPoints
            .Concat(installation.TimingPoints)
            .DistinctBy(point => point.Id)
            .Select(point => new TimingPointCacheIdentity(installation.Id, point.Id, point.SystemId))
            .ToImmutableArray());

    public IEnumerable<string> Keys
    {
        get
        {
            yield return TimingCacheKeys.Installation(AgentId);
            foreach (var point in TimingPoints)
            {
                yield return point.CompositeKey;
                yield return point.InstallationKey;
            }
        }
    }

    public IEnumerable<string> Tags
    {
        get
        {
            yield return TimingCacheTags.InstallationAgent(AgentId);
            yield return TimingCacheTags.Installation(InstallationId);
            foreach (var point in TimingPoints)
                foreach (var tag in point.Tags)
                    yield return tag;
        }
    }
}

public static class TimingCacheInvalidation
{
    private static readonly TimeSpan PostCommitTimeout = TimeSpan.FromSeconds(5);

    public static async Task InvalidateAsync(HybridCache cache, ILogger logger, IEnumerable<string> keys, IEnumerable<string> tags,
        ICacheInvalidationOperations? operations = null)
    {
        var distinctKeys = keys.Distinct(StringComparer.Ordinal).ToArray();
        var distinctTags = tags.Distinct(StringComparer.Ordinal).ToArray();
        var useFusionOperations = operations?.Handles(cache) == true;
        if (!useFusionOperations && operations?.RequiresStrictHandling(cache) == true)
            throw new InvalidOperationException("The configured cache invalidation operations do not own this FusionCache adapter.");
        using var timeout = new CancellationTokenSource(PostCommitTimeout);
        List<Exception>? failures = null;

        foreach (var tag in distinctTags)
        {
            try
            {
                if (!useFusionOperations)
                    await cache.RemoveByTagAsync(tag, timeout.Token);
                else
                    await operations!.RemoveByTagAsync(tag, timeout.Token);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        foreach (var key in distinctKeys)
        {
            try
            {
                if (!useFusionOperations)
                    await cache.RemoveAsync(key, timeout.Token);
                else
                    await operations!.RemoveAsync(key, timeout.Token);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            var failure = new AggregateException($"Failed to evict {failures.Count} of {distinctKeys.Length} Timing cache key(s) and {distinctTags.Length} tag(s) after a durable write.", failures);
            logger.LogError(failure, "Timing cache invalidation failed for {FailedCount} of {KeyCount} key(s) and {TagCount} tag(s).", failures.Count, distinctKeys.Length, distinctTags.Length);
            throw failure;
        }
    }
}
