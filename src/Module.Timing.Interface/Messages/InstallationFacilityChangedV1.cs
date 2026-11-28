namespace VeloTime.Module.Timing.Interface.Messages;

/// <summary>
/// An installation's facility assignments changed over [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>)
/// (null bounds are unbounded). Published through the Timing outbox in the same transaction as the edit, once
/// per edit. Consumers re-read the installation's whole assignment history and recompute results for passings
/// in the interval; redelivery is harmless.
/// </summary>
public sealed record InstallationFacilityChangedV1(Guid EventId, Guid InstallationId, DateTime? FromUtc, DateTime? ToUtc);
