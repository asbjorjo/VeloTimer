namespace VeloTime.WebUI.Mud.Client.Components;

public sealed record FacilityStatusViewModel(
    string FacilityName,
    bool IsOnline,
    int PeopleCount);