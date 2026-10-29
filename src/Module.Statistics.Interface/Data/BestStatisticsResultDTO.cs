namespace VeloTime.Module.Statistics.Interface.Data;

public sealed record BestStatisticsResultDTO(
    Guid Id,
    Guid StatisticsItemId,
    string StatisticsItemName,
    string ProfileName,
    Guid FacilityId,
    TimeSpan Duration,
    double Speed);
