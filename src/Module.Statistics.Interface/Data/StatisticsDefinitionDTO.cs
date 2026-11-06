namespace VeloTime.Module.Statistics.Interface.Data;

public sealed record StatisticsItemDTO(
    Guid Id,
    string Name,
    string Description,
    float Distance,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null);

public sealed record SaveStatisticsItemRequest(
    string Name,
    string Description,
    float Distance,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null);

public enum StatisticsConfigurationType
{
    Simple,
    Multi
}

public sealed record StatisticsConfigurationDTO(
    Guid Id,
    Guid StatisticsItemId,
    StatisticsConfigurationType Type,
    Guid? CoursePointStart,
    Guid? CoursePointEnd,
    Guid? ParentConfigId,
    int? Repetitions,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null);


public sealed record SaveStatisticsConfigurationRequest(
    Guid StatisticsItemId,
    StatisticsConfigurationType Type,
    Guid? CoursePointStart,
    Guid? CoursePointEnd,
    Guid? ParentConfigId,
    int? Repetitions,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null);