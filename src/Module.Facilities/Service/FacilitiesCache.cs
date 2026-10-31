using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface;

namespace VeloTime.Module.Facilities.Service;

public static class FacilitiesCache
{
    public const string RelationshipsTag = FacilitiesCacheTags.Relationships;
    private static readonly TimeSpan PostCommitTimeout = TimeSpan.FromSeconds(5);

    public static string FacilityByCoursePoint(Guid coursePointId) => $"velotime:facilities:facility-by-course-point:v1:{coursePointId}";
    public static string CoursePointById(Guid coursePointId) => $"velotime:facilities:course-point-by-id:v1:{coursePointId}";
    public static string SegmentsBetweenCoursePoints(Guid startId, Guid endId) => $"velotime:facilities:segments-between-course-points:v1:{startId}:{endId}";
    public static string DistanceBetweenCoursePoints(Guid startId, Guid endId) => $"velotime:facilities:distance-between-course-points:v1:{startId}:{endId}";
    public static string CoursePointByTimingPoint(Guid timingPointId) => $"velotime:facilities:course-point-by-timing-point:v1:{timingPointId}";

    public static HybridCacheEntryOptions NewEntryOptions() => new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public static async Task InvalidateRelationshipsAsync(HybridCache cache, ILogger logger, ICacheInvalidationOperations? operations = null)
    {
        using var timeout = new CancellationTokenSource(PostCommitTimeout);
        try
        {
            var useFusionOperations = operations?.Handles(cache) == true;
            if (!useFusionOperations && operations?.RequiresStrictHandling(cache) == true)
                throw new InvalidOperationException("The configured cache invalidation operations do not own this FusionCache adapter.");

            if (!useFusionOperations)
                await cache.RemoveByTagAsync(RelationshipsTag, timeout.Token);
            else
                await operations!.RemoveByTagAsync(RelationshipsTag, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Facilities HybridCache invalidation failed for tag {CacheTag} after a durable write.", RelationshipsTag);
            throw;
        }
    }
}
