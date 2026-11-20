namespace VeloTime.MessageBus.Topology;

/// <summary>
/// Declarative Service Bus topology. Unset optional properties are left at the
/// broker default on creation and are not reconciled on existing entities.
/// </summary>
public sealed class MessageBusTopologyOptions
{
    public const string Section = "MessageBusTopology";
    public const string ConnectionStringName = "MessageBusManagement";
    public const string TopologyFileKey = "MessageBusTopologyFile";

    public List<TopicDefinition> Topics { get; set; } = [];
}

public sealed class TopicDefinition
{
    public string Name { get; set; } = string.Empty;
    public long? MaxSizeInMegabytes { get; set; }
    public TimeSpan? DefaultMessageTimeToLive { get; set; }
    // Immutable after creation.
    public bool? RequiresDuplicateDetection { get; set; }
    public List<SubscriptionDefinition> Subscriptions { get; set; } = [];
}

public sealed class SubscriptionDefinition
{
    public string Name { get; set; } = string.Empty;
    // Immutable after creation.
    public bool RequiresSession { get; set; }
    public int? MaxDeliveryCount { get; set; }
    public TimeSpan? LockDuration { get; set; }
    public TimeSpan? DefaultMessageTimeToLive { get; set; }
    public bool? DeadLetteringOnMessageExpiration { get; set; }
    public List<RuleDefinition> Rules { get; set; } = [];
}

public sealed class RuleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? SqlFilter { get; set; }
    public CorrelationFilterDefinition? CorrelationFilter { get; set; }
    public string? SqlAction { get; set; }
}

public sealed class CorrelationFilterDefinition
{
    public string? CorrelationId { get; set; }
    public string? MessageId { get; set; }
    public string? To { get; set; }
    public string? ReplyTo { get; set; }
    public string? Subject { get; set; }
    public string? SessionId { get; set; }
    public string? ReplyToSessionId { get; set; }
    public string? ContentType { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
}
