namespace VeloTime.MessageBus.Topology;

public static class MessageBusTopologyValidator
{
    private const int MaxTopicNameLength = 260;
    private const int MaxSubscriptionNameLength = 50;
    private const int MaxRuleNameLength = 50;

    public static IReadOnlyList<string> Validate(MessageBusTopologyOptions options)
    {
        var errors = new List<string>();

        if (options.Topics.Count == 0)
            errors.Add("At least one topic must be declared.");

        foreach (var duplicate in Duplicates(options.Topics.Select(topic => topic.Name)))
            errors.Add($"Topic '{duplicate}' is declared more than once.");

        for (var t = 0; t < options.Topics.Count; t++)
        {
            var topic = options.Topics[t];
            var topicPath = string.IsNullOrWhiteSpace(topic.Name) ? $"Topics[{t}]" : $"topic '{topic.Name}'";
            ValidateName(errors, topic.Name, MaxTopicNameLength, topicPath);

            foreach (var duplicate in Duplicates(topic.Subscriptions.Select(subscription => subscription.Name)))
                errors.Add($"Subscription '{duplicate}' is declared more than once on {topicPath}.");

            for (var s = 0; s < topic.Subscriptions.Count; s++)
            {
                var subscription = topic.Subscriptions[s];
                var subscriptionPath = string.IsNullOrWhiteSpace(subscription.Name)
                    ? $"{topicPath} Subscriptions[{s}]"
                    : $"subscription '{topic.Name}/{subscription.Name}'";
                ValidateName(errors, subscription.Name, MaxSubscriptionNameLength, subscriptionPath);

                // Creating a subscription without a rule makes Service Bus add the catch-all $Default (1=1) rule.
                if (subscription.Rules.Count == 0)
                    errors.Add($"{subscriptionPath} must declare at least one rule.");

                foreach (var duplicate in Duplicates(subscription.Rules.Select(rule => rule.Name)))
                    errors.Add($"Rule '{duplicate}' is declared more than once on {subscriptionPath}.");

                for (var r = 0; r < subscription.Rules.Count; r++)
                {
                    var rule = subscription.Rules[r];
                    var rulePath = string.IsNullOrWhiteSpace(rule.Name)
                        ? $"{subscriptionPath} Rules[{r}]"
                        : $"rule '{rule.Name}' on {subscriptionPath}";
                    ValidateName(errors, rule.Name, MaxRuleNameLength, rulePath);
                    if (rule.Name.StartsWith('$'))
                        errors.Add($"{rulePath} uses a reserved name; rule names must not start with '$'.");
                    ValidateFilter(errors, rule, rulePath);
                }
            }
        }

        return errors;
    }

    private static void ValidateFilter(List<string> errors, RuleDefinition rule, string rulePath)
    {
        var hasSql = !string.IsNullOrWhiteSpace(rule.SqlFilter);
        var hasCorrelation = rule.CorrelationFilter is not null;
        if (hasSql == hasCorrelation)
        {
            errors.Add($"{rulePath} must declare exactly one of sqlFilter or correlationFilter.");
            return;
        }

        if (hasSql && IsCatchAll(rule.SqlFilter!))
            errors.Add($"{rulePath} declares a catch-all filter; use an explicit filter instead.");

        if (rule.CorrelationFilter is { } correlation &&
            correlation.Properties.Count == 0 &&
            new[]
            {
                correlation.CorrelationId, correlation.MessageId, correlation.To, correlation.ReplyTo,
                correlation.Subject, correlation.SessionId, correlation.ReplyToSessionId, correlation.ContentType
            }.All(string.IsNullOrEmpty))
        {
            errors.Add($"{rulePath} correlationFilter must declare at least one condition.");
        }

        if (rule.SqlAction is not null && string.IsNullOrWhiteSpace(rule.SqlAction))
            errors.Add($"{rulePath} sqlAction must not be blank.");
    }

    private static bool IsCatchAll(string sqlFilter) =>
        string.Concat(sqlFilter.Where(character => !char.IsWhiteSpace(character)))
            .Equals("1=1", StringComparison.Ordinal) ||
        sqlFilter.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static void ValidateName(List<string> errors, string name, int maxLength, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors.Add($"{path} must have a name.");
        else if (name.Length > maxLength)
            errors.Add($"{path} name exceeds {maxLength} characters.");
    }

    // Service Bus entity and rule names are case-insensitive.
    private static IEnumerable<string> Duplicates(IEnumerable<string> names) =>
        names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
}
