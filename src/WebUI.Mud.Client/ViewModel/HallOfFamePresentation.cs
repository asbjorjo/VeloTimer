using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Client.ViewModel;

public static class HallOfFamePresentation
{
    public static string ModeKey(HallOfFameRankingMode mode) => mode switch
    {
        HallOfFameRankingMode.BestTime => "HallOfFameBestTime",
        HallOfFameRankingMode.MostPassings => "HallOfFameMostPassings",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static string ColumnKey(HallOfFameColumn column) => $"HallOfFame{column}";

    public static string Value(HallOfFameEntryDTO entry, HallOfFameColumn column, EffectiveDisplaySettings display) => column switch
    {
        HallOfFameColumn.BestTime => entry.BestTime is { } duration ? display.Duration(duration) : "-",
        HallOfFameColumn.SpeedOfBestResult => entry.SpeedOfBestResult is { } speed ? display.SpeedFromMetresPerSecond(speed) : "-",
        HallOfFameColumn.PassingCount => entry.PassingCount?.ToString("N0", display.NumberCulture) ?? "-",
        HallOfFameColumn.FacilityOfBestResult => entry.FacilityOfBestResult ?? "-",
        HallOfFameColumn.DateOfBestResult => display.Timestamp(entry.DateOfBestResult),
        _ => throw new ArgumentOutOfRangeException(nameof(column))
    };
}
