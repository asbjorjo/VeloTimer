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
    public static string Installation(Guid installationId) => $"velotime:statistics:config-history:installation:v2:{installationId:D}";
    public static string Ownership(Guid transponderId) => $"velotime:statistics:config-history:ownership:v1:{transponderId:D}";
    public static string FacilityCourse(Guid facilityId) => $"velotime:statistics:config-history:facility-course:v2:{facilityId:D}";
}

/// <summary>
/// Configuration history read through the shared HybridCache (plan §4.3): one entry per scope holding
/// every version, resolved locally per instant. Entries never fall back to stale values; a miss the owner
/// cannot serve fails the recompute, which is retried. Change-event handlers must call the eviction
/// methods before enqueueing a recompute (evict-before-enqueue; see CourseConfigChangedHandler).
/// </summary>
public sealed class StatisticsConfigurationHistory(HybridCache cache, IStatisticsConfigurationSource source,
    ICacheInvalidationOperations? invalidation = null, ILogger<StatisticsConfigurationHistory>? logger = null)
{
    // Bounded staleness is only a backstop: every history is evicted by its change event before the recompute it triggers.
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    // Facilities publishes CourseConfigChangedV1 for every edit, and its handler evicts this key before it
    // enqueues the recompute, so the expiration only bounds memory, not staleness.
    private static readonly HybridCacheEntryOptions FacilityCourseEntryOptions = new()
    {
        Expiration = TimeSpan.FromHours(12),
        LocalCacheExpiration = TimeSpan.FromHours(12)
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
            FacilityCourseEntryOptions, [StatisticsConfigurationCacheKeys.Tag], cancellationToken);

    /// <summary>
    /// A transponder's ownership periods, cached like the other histories. <paramref name="read"/> fetches them from
    /// Timing on a miss; <see cref="EvictOwnershipAsync"/> must run before an ownership change enqueues a recompute.
    /// </summary>
    public ValueTask<List<TOwner>> GetOwnershipAsync<TOwner>(Guid transponderId,
        Func<Guid, CancellationToken, Task<List<TOwner>>> read, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(StatisticsConfigurationCacheKeys.Ownership(transponderId),
            (read, transponderId), static (state, cancel) => new ValueTask<List<TOwner>>(state.read(state.transponderId, cancel)),
            EntryOptions, [StatisticsConfigurationCacheKeys.Tag], cancellationToken);

    /// <summary>Strictly evicts a transponder's cached ownership history; throws when eviction cannot be confirmed.</summary>
    public Task EvictOwnershipAsync(Guid transponderId) =>
        StatisticsCacheInvalidation.InvalidateAsync(cache, logger ?? (ILogger)NullLogger.Instance,
            [StatisticsConfigurationCacheKeys.Ownership(transponderId)], [], invalidation);

    /// <summary>Strictly evicts the given scopes; throws when eviction cannot be confirmed.</summary>
    public Task EvictAsync(IEnumerable<Guid> installationIds, IEnumerable<Guid> facilityIds) =>
        StatisticsCacheInvalidation.InvalidateAsync(cache, logger ?? (ILogger)NullLogger.Instance,
            installationIds.Select(StatisticsConfigurationCacheKeys.Installation)
                .Concat(facilityIds.Select(StatisticsConfigurationCacheKeys.FacilityCourse)), [], invalidation);
}
