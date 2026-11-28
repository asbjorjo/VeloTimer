namespace VeloTime.Agent.Messaging;

public class MessageBusOptions
{
    public static readonly string Section = "MessageBus";
    public static readonly string ConnectionStringProperty = "MessageBus";

    public string NameSpace { get; set; } = "velotime";
    public string FullyQualifiedNameSpace => $"{NameSpace}.servicebus.windows.net";
    public string TopicName { get; set; } = "velotime-agent";
    /// <summary>Topic the agent receives commands on; its subscription is named after the agent ID.</summary>
    public string ControlTopicName { get; set; } = VeloTime.Agent.Interface.AgentControl.Topic;
    public string QueueName { get; set; } = "velo-passings";
    public int MaxConcurrency { get; set; } = 5;
    public TimeSpan OutboxPollIdleSleep { get; set; } = TimeSpan.FromSeconds(5);
    public string ConnectionString { get; set; } = string.Empty;
}
