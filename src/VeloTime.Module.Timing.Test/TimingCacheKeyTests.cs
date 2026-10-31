using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Test;

public sealed class TimingCacheKeyTests
{
    [Fact]
    public void Keys_AreNamespacedAndVersionedByCachedShape()
    {
        var installationId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();

        Assert.Equal("velotime:timing:installation:v1:agent-1", TimingCacheKeys.Installation("agent-1"));
        Assert.Equal($"velotime:timing:timing-point:v1:{installationId}:loop-1", TimingCacheKeys.TimingPoint(installationId, "loop-1"));
        Assert.Equal($"velotime:timing:installation-for-timing-point:v1:{transponderId}", TimingCacheKeys.InstallationForTimingPoint(transponderId));
        Assert.Equal($"velotime:timing:transponder:v1:{TimingSystem.Unknown}:device-1", TimingCacheKeys.Transponder(TimingSystem.Unknown, "device-1"));
        Assert.Equal($"velotime:timing:last-passing:v2:{installationId}:{transponderId}", TimingCacheKeys.LastPassing(installationId, transponderId));
    }

    [Fact]
    public void Entries_UseExplicitThirtySecondLocalAndDistributedLifetime()
    {
        var options = TimingCacheKeys.NewEntryOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.Expiration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LocalCacheExpiration);
    }
}
