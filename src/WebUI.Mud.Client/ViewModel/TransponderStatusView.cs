using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.ViewModel;

public sealed record TransponderStatusView(TransponderStatus Status,
    IReadOnlyDictionary<Guid, string> FacilityNames, string? FacilityError);
