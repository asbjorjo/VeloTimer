namespace VeloTime.Agent.Interface.Messages;

public record SystemConfig(DateTime Time, string SystemId, string Sport, string Description, bool IsActive);
public record TimingLoopConfig(DateTime Time, string LoopId, string Name, int Order, string SystemId);
public record SegmentConfig(DateTime Time, string SegmentId, string Name, string StartLoopId, string EndLoopId, double Length, string SystemId);
public record TimingLoopStatus(DateTime Time, string LoopId, double Noise);
