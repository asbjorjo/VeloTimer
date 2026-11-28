using VeloTime.Module.Statistics.Client;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

internal static class StatisticsRequiredPointsTransport
{
    internal static Contracts.StatisticsRequiredCoursePoints? Map(StatisticsConfigurationDTO configuration)
    {
        if (configuration.RequiredCoursePoints is not { } requirements) return null; // Older servers omit the additive property.
        var ids = requirements.CoursePointIds?.ToArray();
        if ((Contracts.StatisticsConfigurationType)configuration.Type != Contracts.StatisticsConfigurationType.Simple || ids is null ||
            (ids.Length == 0 ? requirements.CourseLayoutId is not null : requirements.CourseLayoutId.GetValueOrDefault() == Guid.Empty) ||
            ids.Any(id => id == Guid.Empty || id == configuration.CoursePointStart || id == configuration.CoursePointEnd) ||
            ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("The statistics requirements response is invalid.");
        return new(requirements.CourseLayoutId, ids);
    }
}
