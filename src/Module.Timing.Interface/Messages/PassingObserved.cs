namespace VeloTime.Module.Timing.Interface.Messages;

public record PassingObserved(DateTimeOffset Time, string TransponderType, string TransponderId, Guid Installation, string TimingPoint, bool LowBattery, bool LowStrength, bool LowHits);
