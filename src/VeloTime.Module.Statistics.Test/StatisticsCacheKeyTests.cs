using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsCacheKeyTests
{
    [Fact]
    public void ConfigurationKeys_AreNamespacedAndVersionedByCachedShape()
    {
        var coursePointId = Guid.NewGuid();
        var parentConfigId = Guid.NewGuid();

        Assert.Equal($"velotime:statistics:configurations-by-end-course-point:v1:{coursePointId}", StatisticsCacheKeys.ConfigurationsByEndCoursePoint(coursePointId));
        Assert.Equal($"velotime:statistics:multi-configurations-by-parent:v1:{parentConfigId}", StatisticsCacheKeys.MultiConfigurationsByParent(parentConfigId));
    }

    [Fact]
    public void Entries_UseExplicitThirtySecondLocalAndDistributedLifetime()
    {
        var options = StatisticsCacheKeys.NewEntryOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.Expiration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LocalCacheExpiration);
    }
}
