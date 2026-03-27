using VeloTime.Agent.Interface.Messages.Events;

namespace VeloTime.Agent.Model;

public static class EventMappers
{
    public static PassingEvent ToMessage(this Passing passing)
    {
        return new PassingEvent
        (
            Time: passing.Time,
            LoopId: passing.LoopId,
            TransponderId: passing.TransponderId,
            TransponderType: passing.TransponderType
        );
    }

    public static LoopStatusEvent ToMessage(this LoopStatus loopStatus)
    {
        return new LoopStatusEvent
        (
            Time: loopStatus.Time,
            LoopId: loopStatus.LoopId,
            Noise: loopStatus.Noise
        );
    }

    public static SystemConfigEvent ToMessage(this SystemConfig config)
    {
        return new SystemConfigEvent
        (
            SystemId: config.SystemId,
            Sport: config.Sport,
            Description: config.Description,
            IsActive: config.IsActive
        );
    }

    public static LoopConfigEvent ToMessage(this LoopConfig config)
    {
        return new LoopConfigEvent
        (
            LoopId: config.LoopId,
            Name: config.Name,
            Order: config.Order,
            SystemId: config.SystemId
        );
    }
    public static SegmentConfigEvent ToMessage(this SegmentConfig config)
    {
        return new SegmentConfigEvent
        (
            SegmentId: config.SegmentId,
            Name: config.Name,
            StartLoopId: config.FromLoopId,
            EndLoopId: config.ToLoopId,
            Length: config.Length,
            SystemId: config.SystemId
        );
    }

    public static InstallationLayoutEvent ToMessage(this InstallationConfig installation)
    {
        return new InstallationLayoutEvent
        (
            Systems: installation.Systems.Select(s => s.ToMessage()).ToList(),
            TimingLoops: installation.Systems.SelectMany(s => s.Loops.Select(l => l.ToMessage())).ToList(),
            Segments: installation.Systems.SelectMany(s => s.Segments.Select(seg => seg.ToMessage())).ToList()
        );
    }
}
