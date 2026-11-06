namespace VeloTime.Module.Timing.Interface.Data;

public record SampleDTO(Guid TransponderId, string TimingPointStart, string TimingPointEnd, DateTime Time, TimeSpan Duration);
