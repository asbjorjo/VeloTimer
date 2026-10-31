using VeloTime.Module.Facilities.Service;

namespace VeloTime.Module.Facilities.Test;

public sealed class FacilitiesCacheKeyTests
{
    [Fact]
    public void RelationshipKeys_AreNamespacedAndVersionedByCachedShape()
    {
        var startId = Guid.NewGuid();
        var endId = Guid.NewGuid();

        Assert.Equal($"velotime:facilities:facility-by-course-point:v1:{startId}", FacilitiesCache.FacilityByCoursePoint(startId));
        Assert.Equal($"velotime:facilities:course-point-by-id:v1:{startId}", FacilitiesCache.CoursePointById(startId));
        Assert.Equal($"velotime:facilities:segments-between-course-points:v1:{startId}:{endId}", FacilitiesCache.SegmentsBetweenCoursePoints(startId, endId));
        Assert.Equal($"velotime:facilities:distance-between-course-points:v1:{startId}:{endId}", FacilitiesCache.DistanceBetweenCoursePoints(startId, endId));
        Assert.Equal($"velotime:facilities:course-point-by-timing-point:v1:{startId}", FacilitiesCache.CoursePointByTimingPoint(startId));
    }

    [Fact]
    public void Entries_UseExplicitThirtySecondLocalAndDistributedLifetime()
    {
        var options = FacilitiesCache.NewEntryOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.Expiration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LocalCacheExpiration);
    }
}
