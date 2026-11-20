namespace VeloTime.Module.Timing.Interface.Messages;

public record TimingSampleComplete(DateTime TimeStart, DateTime TimeEnd, Guid TimingPointStart, Guid TimingPointEnd, Guid TransponderId);
