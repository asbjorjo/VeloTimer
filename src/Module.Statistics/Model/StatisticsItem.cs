namespace VeloTime.Module.Statistics.Model;

public class StatisticsItem
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float Distance { get; set; }
}
