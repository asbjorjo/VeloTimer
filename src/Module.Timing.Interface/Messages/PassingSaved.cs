namespace VeloTime.Module.Timing.Interface.Messages;

public record PassingSaved(DateTime Time, Guid TransponderId, Guid TimingPoint);
