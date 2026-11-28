using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace VeloTime.MessageBus.Topology;

public sealed class MessageBusTopologyException(string message) : Exception(message);

/// <summary>
/// Converges the broker towards the declared topology without ever leaving a
/// declared subscription with the catch-all <c>$Default</c> (1=1) rule:
/// subscriptions are created together with their first declared rule, and on
/// existing subscriptions declared rules are applied before undeclared rules
/// (including <c>$Default</c>) are removed. Topics and subscriptions that are
/// not declared are never touched.
/// </summary>
public sealed class MessageBusTopologyReconciler(
    IServiceBusTopologyClient client,
    ILogger<MessageBusTopologyReconciler> logger)
{
    public async Task ReconcileAsync(MessageBusTopologyOptions topology, CancellationToken cancellationToken)
    {
        var errors = MessageBusTopologyValidator.Validate(topology);
        if (errors.Count > 0)
            throw new MessageBusTopologyException("Invalid message bus topology: " + string.Join(" ", errors));

        foreach (var topic in topology.Topics)
        {
            await ReconcileTopicAsync(topic, cancellationToken);
            foreach (var subscription in topic.Subscriptions)
            {
                await ReconcileSubscriptionAsync(topic.Name, subscription, cancellationToken);
                await ReconcileRulesAsync(topic.Name, subscription, cancellationToken);
            }
        }
    }

    private async Task ReconcileTopicAsync(TopicDefinition definition, CancellationToken cancellationToken)
    {
        var existing = await client.GetTopicAsync(definition.Name, cancellationToken);
        if (existing is null)
        {
            var options = new CreateTopicOptions(definition.Name);
            if (definition.MaxSizeInMegabytes is { } maxSize)
                options.MaxSizeInMegabytes = maxSize;
            if (definition.DefaultMessageTimeToLive is { } ttl)
                options.DefaultMessageTimeToLive = ttl;
            if (definition.RequiresDuplicateDetection is { } duplicateDetection)
                options.RequiresDuplicateDetection = duplicateDetection;

            if (await TryCreateAsync(() => client.CreateTopicAsync(options, cancellationToken)))
            {
                logger.LogInformation("Created topic {Topic}", definition.Name);
                return;
            }

            existing = await client.GetTopicAsync(definition.Name, cancellationToken)
                ?? throw new MessageBusTopologyException($"Topic '{definition.Name}' reported as existing but could not be read.");
        }

        if (definition.RequiresDuplicateDetection is { } requiresDuplicateDetection &&
            existing.RequiresDuplicateDetection != requiresDuplicateDetection)
        {
            throw new MessageBusTopologyException(
                $"Topic '{definition.Name}' has requiresDuplicateDetection={existing.RequiresDuplicateDetection}, " +
                $"declared {requiresDuplicateDetection}. This property cannot be changed; recreate the topic manually.");
        }

        var changed = false;
        if (definition.MaxSizeInMegabytes is { } size && existing.MaxSizeInMegabytes != size)
        {
            existing.MaxSizeInMegabytes = size;
            changed = true;
        }
        if (definition.DefaultMessageTimeToLive is { } timeToLive && existing.DefaultMessageTimeToLive != timeToLive)
        {
            existing.DefaultMessageTimeToLive = timeToLive;
            changed = true;
        }

        if (changed)
        {
            await client.UpdateTopicAsync(existing, cancellationToken);
            logger.LogInformation("Updated topic {Topic}", definition.Name);
        }
    }

    private async Task ReconcileSubscriptionAsync(string topicName, SubscriptionDefinition definition, CancellationToken cancellationToken)
    {
        var existing = await client.GetSubscriptionAsync(topicName, definition.Name, cancellationToken);
        if (existing is null)
        {
            var options = new CreateSubscriptionOptions(topicName, definition.Name)
            {
                RequiresSession = definition.RequiresSession
            };
            if (definition.MaxDeliveryCount is { } maxDeliveryCount)
                options.MaxDeliveryCount = maxDeliveryCount;
            if (definition.LockDuration is { } lockDuration)
                options.LockDuration = lockDuration;
            if (definition.DefaultMessageTimeToLive is { } ttl)
                options.DefaultMessageTimeToLive = ttl;
            if (definition.DeadLetteringOnMessageExpiration is { } deadLettering)
                options.DeadLetteringOnMessageExpiration = deadLettering;

            // Supplying the first rule at creation replaces the broker's $Default (1=1) rule.
            var firstRule = ToCreateRuleOptions(definition.Rules[0]);
            if (await TryCreateAsync(() => client.CreateSubscriptionAsync(options, firstRule, cancellationToken)))
            {
                logger.LogInformation("Created subscription {Topic}/{Subscription} with rule {Rule}",
                    topicName, definition.Name, firstRule.Name);
                return;
            }

            existing = await client.GetSubscriptionAsync(topicName, definition.Name, cancellationToken)
                ?? throw new MessageBusTopologyException(
                    $"Subscription '{topicName}/{definition.Name}' reported as existing but could not be read.");
        }

        if (existing.RequiresSession != definition.RequiresSession)
        {
            throw new MessageBusTopologyException(
                $"Subscription '{topicName}/{definition.Name}' has requiresSession={existing.RequiresSession}, " +
                $"declared {definition.RequiresSession}. This property cannot be changed; recreate the subscription manually.");
        }

        var changed = false;
        if (definition.MaxDeliveryCount is { } deliveryCount && existing.MaxDeliveryCount != deliveryCount)
        {
            existing.MaxDeliveryCount = deliveryCount;
            changed = true;
        }
        if (definition.LockDuration is { } lockTime && existing.LockDuration != lockTime)
        {
            existing.LockDuration = lockTime;
            changed = true;
        }
        if (definition.DefaultMessageTimeToLive is { } timeToLive && existing.DefaultMessageTimeToLive != timeToLive)
        {
            existing.DefaultMessageTimeToLive = timeToLive;
            changed = true;
        }
        if (definition.DeadLetteringOnMessageExpiration is { } expiry && existing.DeadLetteringOnMessageExpiration != expiry)
        {
            existing.DeadLetteringOnMessageExpiration = expiry;
            changed = true;
        }

        if (changed)
        {
            await client.UpdateSubscriptionAsync(existing, cancellationToken);
            logger.LogInformation("Updated subscription {Topic}/{Subscription}", topicName, definition.Name);
        }
    }

    private async Task ReconcileRulesAsync(string topicName, SubscriptionDefinition definition, CancellationToken cancellationToken)
    {
        var existingRules = (await client.GetRulesAsync(topicName, definition.Name, cancellationToken))
            .ToDictionary(rule => rule.Name, StringComparer.OrdinalIgnoreCase);

        // Add/update before deleting so the subscription never has zero rules or only $Default.
        foreach (var declared in definition.Rules)
        {
            var filter = ToFilter(declared);
            var action = ToAction(declared);

            if (!existingRules.TryGetValue(declared.Name, out var existing))
            {
                await client.CreateRuleAsync(topicName, definition.Name,
                    new CreateRuleOptions(declared.Name, filter) { Action = action }, cancellationToken);
                logger.LogInformation("Created rule {Rule} on {Topic}/{Subscription}", declared.Name, topicName, definition.Name);
                continue;
            }

            if (!Equals(existing.Filter, filter) || !SameAction(existing.Action, action))
            {
                existing.Filter = filter;
                existing.Action = action;
                await client.UpdateRuleAsync(topicName, definition.Name, existing, cancellationToken);
                logger.LogInformation("Updated rule {Rule} on {Topic}/{Subscription}", declared.Name, topicName, definition.Name);
            }
        }

        var declaredNames = definition.Rules.Select(rule => rule.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var undeclared in existingRules.Keys.Where(name => !declaredNames.Contains(name)))
        {
            await client.DeleteRuleAsync(topicName, definition.Name, undeclared, cancellationToken);
            logger.LogInformation("Deleted undeclared rule {Rule} from {Topic}/{Subscription}", undeclared, topicName, definition.Name);
        }
    }

    private static async Task<bool> TryCreateAsync(Func<Task> create)
    {
        try
        {
            await create();
            return true;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            // Lost a creation race; reconcile the existing entity instead.
            return false;
        }
    }

    // The broker may report "no action" as an empty action object rather than null.
    private static bool SameAction(RuleAction? existing, RuleAction? declared) =>
        declared is null ? existing is not SqlRuleAction : Equals(existing, declared);

    internal static CreateRuleOptions ToCreateRuleOptions(RuleDefinition rule) =>
        new(rule.Name, ToFilter(rule)) { Action = ToAction(rule) };

    internal static RuleFilter ToFilter(RuleDefinition rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.SqlFilter))
            return new SqlRuleFilter(rule.SqlFilter);

        var source = rule.CorrelationFilter
            ?? throw new MessageBusTopologyException($"Rule '{rule.Name}' declares no filter.");
        var filter = new CorrelationRuleFilter
        {
            CorrelationId = source.CorrelationId,
            MessageId = source.MessageId,
            To = source.To,
            ReplyTo = source.ReplyTo,
            Subject = source.Subject,
            SessionId = source.SessionId,
            ReplyToSessionId = source.ReplyToSessionId,
            ContentType = source.ContentType
        };
        foreach (var (key, value) in source.Properties)
            filter.ApplicationProperties[key] = value;
        return filter;
    }

    internal static RuleAction? ToAction(RuleDefinition rule) =>
        string.IsNullOrWhiteSpace(rule.SqlAction) ? null : new SqlRuleAction(rule.SqlAction);
}
