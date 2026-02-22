namespace VeloTime.Module.Statistics.Model;

public class StatsProfile
{
    public Guid UserId { get; init; }
    public required string ProfileName { get; init; }
    public bool IsPublic { get; init; } = true;
}
