using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.ViewModel;

/// <summary>UI-only mutable draft. Keys preserve component identity during keyboard reordering.</summary>
public sealed class HallOfFameSectionDraft(HallOfFameSectionSettingsDTO settings)
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid StatisticsItemId { get; set; } = settings.StatisticsItemId;
    public HallOfFameRankingMode RankingMode { get; set; } = settings.RankingMode;
    public bool IsEnabled { get; set; } = settings.IsEnabled;
    public int? RowCount { get; set; } = settings.RowCount;
    public List<HallOfFameColumn> Columns { get; } = settings.Columns.ToList();

    public HallOfFameSectionSettingsDTO Snapshot() =>
        new(StatisticsItemId, RankingMode, IsEnabled, RowCount ?? 0, Columns.ToArray());
}
