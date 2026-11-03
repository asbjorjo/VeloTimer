namespace VeloTime.Module.Statistics.Interface.Data;

public enum HallOfFameRankingMode
{
    BestTime,
    MostPassings
}

/// <summary>Optional columns. Rank and rider name are always present and are not configurable.</summary>
public enum HallOfFameColumn
{
    BestTime,
    SpeedOfBestResult,
    PassingCount,
    FacilityOfBestResult,
    DateOfBestResult
}

/// <summary>
/// Settings in display order. Disabled sections remain persisted but are omitted from public results.
/// The column list is in display order and contains optional columns only.
/// </summary>
public sealed record HallOfFameSectionSettingsDTO(
    Guid StatisticsItemId,
    HallOfFameRankingMode RankingMode,
    bool IsEnabled,
    int RowCount,
    IReadOnlyList<HallOfFameColumn> Columns);

/// <summary>
/// Singleton settings version. Version zero represents settings that have not yet been saved.
/// Sections are ordered as stored, including disabled sections.
/// </summary>
public sealed record HallOfFameSettingsDTO(
    long Version,
    IReadOnlyList<HallOfFameSectionSettingsDTO> Sections);

/// <summary>
/// Replace the complete ordered settings document if ExpectedVersion matches the current version.
/// Use expected version zero to create the initial settings document.
/// </summary>
public sealed record SaveHallOfFameSettingsRequest(
    long ExpectedVersion,
    IReadOnlyList<HallOfFameSectionSettingsDTO> Sections);

/// <summary>
/// One configured, enabled public section. Columns and rows retain their configured order.
/// </summary>
public sealed record HallOfFameSectionDTO(
    Guid StatisticsItemId,
    string StatisticsItemName,
    HallOfFameRankingMode RankingMode,
    int RowCount,
    IReadOnlyList<HallOfFameColumn> Columns,
    IReadOnlyList<HallOfFameEntryDTO> Entries);

/// <summary>
/// Public row. Nullable optional values are populated only when the corresponding column is enabled.
/// Speed is metres per second; DateOfBestResult is the UTC completion timestamp.
/// </summary>
public sealed record HallOfFameEntryDTO(
    int Rank,
    string ProfileName,
    TimeSpan? BestTime,
    double? SpeedOfBestResult,
    long? PassingCount,
    string? FacilityOfBestResult,
    DateTimeOffset? DateOfBestResult);

/// <summary>Public all-time results, ordered by configured enabled section order.</summary>
public sealed record HallOfFameDTO(IReadOnlyList<HallOfFameSectionDTO> Sections);
