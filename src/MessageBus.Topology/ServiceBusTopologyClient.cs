using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace VeloTime.MessageBus.Topology;

/// <summary>Narrow seam over <see cref="ServiceBusAdministrationClient"/> used by the reconciler.</summary>
public interface IServiceBusTopologyClient
{
    Task<TopicProperties?> GetTopicAsync(string topicName, CancellationToken cancellationToken);
    Task CreateTopicAsync(CreateTopicOptions options, CancellationToken cancellationToken);
    Task UpdateTopicAsync(TopicProperties topic, CancellationToken cancellationToken);

    Task<SubscriptionProperties?> GetSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken);
    Task CreateSubscriptionAsync(CreateSubscriptionOptions options, CreateRuleOptions rule, CancellationToken cancellationToken);
    Task UpdateSubscriptionAsync(SubscriptionProperties subscription, CancellationToken cancellationToken);

    Task<IReadOnlyList<RuleProperties>> GetRulesAsync(string topicName, string subscriptionName, CancellationToken cancellationToken);
    Task CreateRuleAsync(string topicName, string subscriptionName, CreateRuleOptions rule, CancellationToken cancellationToken);
    Task UpdateRuleAsync(string topicName, string subscriptionName, RuleProperties rule, CancellationToken cancellationToken);
    Task DeleteRuleAsync(string topicName, string subscriptionName, string ruleName, CancellationToken cancellationToken);
}

internal sealed class ServiceBusTopologyClient(ServiceBusAdministrationClient client) : IServiceBusTopologyClient
{
    public async Task<TopicProperties?> GetTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetTopicAsync(topicName, cancellationToken);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public Task CreateTopicAsync(CreateTopicOptions options, CancellationToken cancellationToken) =>
        client.CreateTopicAsync(options, cancellationToken);

    public Task UpdateTopicAsync(TopicProperties topic, CancellationToken cancellationToken) =>
        client.UpdateTopicAsync(topic, cancellationToken);

    public async Task<SubscriptionProperties?> GetSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public Task CreateSubscriptionAsync(CreateSubscriptionOptions options, CreateRuleOptions rule, CancellationToken cancellationToken) =>
        client.CreateSubscriptionAsync(options, rule, cancellationToken);

    public Task UpdateSubscriptionAsync(SubscriptionProperties subscription, CancellationToken cancellationToken) =>
        client.UpdateSubscriptionAsync(subscription, cancellationToken);

    public async Task<IReadOnlyList<RuleProperties>> GetRulesAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        var rules = new List<RuleProperties>();
        await foreach (var rule in client.GetRulesAsync(topicName, subscriptionName, cancellationToken))
            rules.Add(rule);
        return rules;
    }

    public Task CreateRuleAsync(string topicName, string subscriptionName, CreateRuleOptions rule, CancellationToken cancellationToken) =>
        client.CreateRuleAsync(topicName, subscriptionName, rule, cancellationToken);

    public Task UpdateRuleAsync(string topicName, string subscriptionName, RuleProperties rule, CancellationToken cancellationToken) =>
        client.UpdateRuleAsync(topicName, subscriptionName, rule, cancellationToken);

    public Task DeleteRuleAsync(string topicName, string subscriptionName, string ruleName, CancellationToken cancellationToken) =>
        client.DeleteRuleAsync(topicName, subscriptionName, ruleName, cancellationToken);

    // The administration client reports missing entities as ServiceBusException(MessagingEntityNotFound);
    // a raw 404 is kept as a fallback.
    internal static bool IsNotFound(Exception exception) => exception switch
    {
        ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityNotFound } => true,
        RequestFailedException { Status: 404 } => true,
        _ => false,
    };
}
