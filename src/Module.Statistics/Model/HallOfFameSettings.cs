namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// The single persisted Hall of Fame document. Sections are stored as an ordered JSON array.
/// </summary>
public sealed class HallOfFameSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public long Version { get; set; }
    public string SectionsJson { get; set; } = "[]";
}
