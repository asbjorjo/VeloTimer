namespace VeloTime.WebUI.Mud.Client.ViewModel;

public sealed record BestStatisticsResultView(
    Guid Id,
    string StatisticsItemName,
    string ProfileName,
    string FacilityName,
    TimeSpan Duration,
    double Speed);
