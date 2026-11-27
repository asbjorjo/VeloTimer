using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Common;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>
/// Reads configuration history from its owning service (Facilities, Timing). Implementations fetch the
/// whole history for one scope in one call and throw when the owner cannot answer.
/// </summary>
public interface IStatisticsConfigurationSource
{
    /// <summary>The installation's facility history; a missing installation has no facility.</summary>
    Task<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken);

    /// <summary>The facility's layouts and timing associations; a missing facility has none.</summary>
    Task<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken);
}

public static class StatisticsConfigurationCacheKeys
{
    public const string Tag = "Statistics.ConfigurationHistory";
    public static string Installation(Guid installationId) => $"velotime:statistics:config-history:installation:v1:{installationId:D}";
    public static string FacilityCourse(Guid facilityId) => $"velotime:statistics:config-history:facility-course:v1:{facilityId:D}";
}

/// <summary>
/// Configuration history read through the shared HybridCache (plan §4.3): one entry per scope holding
/// every version, resolved locally per instant. Entries never fall back to stale values; a miss the owner
/// cannot serve fails the recompute, which is retried. Change-event handlers must call the eviction
/// methods before enqueueing a recompute (evict-before-enqueue; consumed from Phase 3).
/// </summary>
public sealed class StatisticsConfigurationHistory(HybridCache cache, IStatisticsConfigurationSource source,
    ICacheInvalidationOperations? invalidation = null, ILogger<StatisticsConfigurationHistory>? logger = null)
{
    // Bounded staleness until Phase 3 delivers change events for Facilities config.
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public ValueTask<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(StatisticsConfigurationCacheKeys.Installation(installationId),
            (source, installationId), static (state, cancel) => new ValueTask<InstallationFacilityHistory>(
                state.source.GetInstallationAsync(state.installationId, cancel)),
            EntryOptions, [StatisticsConfigurationCacheKeys.Tag], cancellationToken);

    public ValueTask<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(StatisticsConfigurationCacheKeys.FacilityCourse(facilityId),
            (source, facilityId), static (state, cancel) => new ValueTask<FacilityCourseHistory>(
                state.source.GetFacilityCourseAsync(state.facilityId, cancel)),
            EntryOptions, [StatisticsConfigurationCacheKeys.Tag], cancellationToken);

    /// <summary>Strictly evicts the given scopes; throws when eviction cannot be confirmed.</summary>
    public Task EvictAsync(IEnumerable<Guid> installationIds, IEnumerable<Guid> facilityIds) =>
        StatisticsCacheInvalidation.InvalidateAsync(cache, logger ?? (ILogger)NullLogger.Instance,
            installationIds.Select(StatisticsConfigurationCacheKeys.Installation)
                .Concat(facilityIds.Select(StatisticsConfigurationCacheKeys.FacilityCourse)), [], invalidation);
}
