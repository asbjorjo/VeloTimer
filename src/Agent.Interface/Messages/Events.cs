namespace VeloTime.Agent.Interface.Messages.Events;

public record AgentStartedEvent(DateTime Time, string AgentId);

public record PassingEvent(DateTime Time, string TransponderType, string TransponderId, string LoopId);
public record TransponderStatusEvent(DateTime Time, string TransponderType, string TransponderId, bool LowBattery, bool LowStrength);
public record LoopStatusEvent(DateTime Time, string LoopId, double Noise);

public record SystemConfigEvent(string SystemId, string Sport, string Description, bool IsActive);
public record LoopConfigEvent(string LoopId, string Name, int Order, string SystemId);
public record SegmentConfigEvent(string SegmentId, string Name, string StartLoopId, string EndLoopId, double Length, string SystemId);

public record InstallationLayoutEvent(List<SystemConfigEvent> Systems, List<LoopConfigEvent> TimingLoops, List<SegmentConfigEvent> Segments);