using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public enum StatisticsEntryPassingsStatus
{
    Available,
    Forbidden,
    WindowTooLarge,
    Unavailable
}

/// <summary>UTC timestamps and base SI quantities. Null passings means unavailable, never an empty match.</summary>
public sealed record StatisticsEntryContributionsDTO(
    Guid EntryId, Guid TransponderId, DateTimeOffset TimeStart, DateTimeOffset TimeEnd,
    IReadOnlyList<AdminStatisticsSampleDTO> Samples, bool SamplesTruncated,
    IReadOnlyList<PassingDTO>? Passings, bool PassingsTruncated,
    StatisticsEntryPassingsStatus PassingsStatus);
