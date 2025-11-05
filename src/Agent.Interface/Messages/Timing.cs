namespace VeloTime.Agent.Interface.Messages;

public record PassingObserved(DateTime Time, string TransponderType, string TransponderId, string LoopId, bool LowBattery, bool LowStrength, bool LowHits);
