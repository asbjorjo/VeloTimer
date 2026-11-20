namespace VeloTime.Module.Statistics.Model;

public class StatsProfile
{
    public Guid UserId { get; init; }
    public required string ProfileName { get; set; }
    public bool IsPublic { get; set; } = true;
}
