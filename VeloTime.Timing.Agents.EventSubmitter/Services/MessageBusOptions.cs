namespace VeloTime.Timing.Agents.EventSubmitter.Services;

public class MessageBusOptions
{
    public static readonly string Section = "MessageBus";
    public static readonly string ConnectionStringProperty = "MessageBus";

    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "velo-passings";
    public int MaxConcurrency { get; set; } = 5;
}
