using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;

namespace VeloTime.Module.Statistics.Service;

public static class StatisticsCacheKeys
{
    public static string ConfigurationsByEndCoursePoint(Guid coursePointId) => $"velotime:statistics:configurations-by-end-course-point:v1:{coursePointId}";
    public static string MultiConfigurationsByParent(Guid parentConfigId) => $"velotime:statistics:multi-configurations-by-parent:v1:{parentConfigId}";

    public static HybridCacheEntryOptions NewEntryOptions() => new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };
}

public static class StatisticsCacheTags
{
    // Both cached query shapes embed StatisticsItem and configuration data.
    public const string ConfigurationDefinitions = "Statistics.ConfigurationDefinitions";
}

public readonly record struct SimpleStatisticsCacheIdentity(Guid ConfigId, Guid CoursePointEndId)
{
    public IEnumerable<string> Keys
    {
        get
        {
            yield return StatisticsCacheKeys.ConfigurationsByEndCoursePoint(CoursePointEndId);
            yield return StatisticsCacheKeys.MultiConfigurationsByParent(ConfigId);
        }
    }
}

public readonly record struct MultiStatisticsCacheIdentity(Guid ParentConfigId)
{
    public string Key => StatisticsCacheKeys.MultiConfigurationsByParent(ParentConfigId);
}

public static class StatisticsCacheInvalidation
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
            var failure = new AggregateException($"Failed to evict {failures.Count} of {distinctKeys.Length} Statistics cache key(s) and {distinctTags.Length} tag(s) after a durable write.", failures);
            logger.LogError(failure, "Statistics cache invalidation failed for {FailedCount} of {KeyCount} key(s) and {TagCount} tag(s).", failures.Count, distinctKeys.Length, distinctTags.Length);
            throw failure;
        }
    }
}
