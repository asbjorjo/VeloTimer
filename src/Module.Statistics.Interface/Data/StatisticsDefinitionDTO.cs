namespace VeloTime.Module.Statistics.Interface.Data;

public sealed record StatisticsItemDTO(Guid Id, string Name, string Description, float Distance);

public sealed record SaveStatisticsItemRequest(string Name, string Description, float Distance);

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
    int? Repetitions);

public sealed record SaveStatisticsConfigurationRequest(
    Guid StatisticsItemId,
    StatisticsConfigurationType Type,
    Guid? CoursePointStart,
    Guid? CoursePointEnd,
    Guid? ParentConfigId,
    int? Repetitions);