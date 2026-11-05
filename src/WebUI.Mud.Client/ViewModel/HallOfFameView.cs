using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.ViewModel;

public sealed record HallOfFameTimeView(string ProfileName, TimeSpan Duration, double SpeedMetresPerSecond, string FacilityName);

public sealed record HallOfFameView(bool IsAvailable, IReadOnlyList<HallOfFameTimeView> Times, IReadOnlyList<HallOfFameLapsDTO> Laps);
