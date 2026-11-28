namespace VeloTime.Module.Timing.Interface.Messages;

/// <summary>
/// A passing fact that Timing has durably recorded. Published through the Timing outbox in the same transaction
/// as the passing row, exactly once per <paramref name="PassingId"/>. Consumers must treat redelivery as a no-op.
/// </summary>
public record PassingRecordedV1(
    Guid PassingId,
    Guid InstallationId,
    Guid TimingPointId,
    Guid TransponderId,
    DateTime Time,
    bool LowBattery,
    bool LowStrength,
    bool LowHits);
