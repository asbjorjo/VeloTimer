namespace VeloTime.Module.Statistics.Interface.Messages;

/// <summary>
/// Requests asynchronous revalidation using the latest stored definitions. A null
/// configuration ID scopes the change to the whole item; otherwise it scopes it to
/// that configuration. The consumer must also refresh dependent Multi results.
/// </summary>
public sealed record StatisticsValidityThresholdsChangedV1(
    Guid EventId,
    Guid StatisticsItemId,
    Guid? StatisticsItemConfigId);
