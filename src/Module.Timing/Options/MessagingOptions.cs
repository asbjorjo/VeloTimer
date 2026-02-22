namespace VeloTime.Module.Timing.Options;

public class MessagingOptions
{
    public static readonly string Section = "Messaging";
    public static readonly string ConnectionStringProperty = "MessageBus";

    public string NameSpace { get; set; } = "velotime";
    public string FullyQualifiedNameSpace => $"{NameSpace}.servicebus.windows.net";
    public string TopicName { get; set; } = "velotime-agent";
    public string SubscriptionName { get; set; } = "passings";
    public int MaxConcurrency { get; set; } = 5;
    public string ConnectionString { get; set; } = string.Empty;
}
