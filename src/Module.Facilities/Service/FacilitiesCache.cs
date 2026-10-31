using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface;

namespace VeloTime.Module.Facilities.Service;

public static class FacilitiesCache
{
    public const string RelationshipsTag = FacilitiesCacheTags.Relationships;
    private static readonly TimeSpan PostCommitTimeout = TimeSpan.FromSeconds(5);

    public static string FacilityByCoursePoint(Guid coursePointId) => $"FacilityByCoursePoint_{coursePointId}";
    public static string CoursePointById(Guid coursePointId) => $"CoursePointById_{coursePointId}";
    public static string SegmentsBetweenCoursePoints(Guid startId, Guid endId) => $"SegmentsBetweenCoursePoints_{startId}_{endId}";
    public static string DistanceBetweenCoursePoints(Guid startId, Guid endId) => $"DistanceBetweenCoursePoints_{startId}_{endId}";
    public static string CoursePointByTimingPoint(Guid timingPointId) => $"CoursePointByTimingPoint_{timingPointId}";

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
