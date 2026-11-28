using System.Text.RegularExpressions;

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

        if (hasSql && HasConstantBranch(rule.SqlFilter!))
            errors.Add($"{rulePath} sqlFilter has a branch that references no message property " +
                "(e.g. 1=1); such constant predicates match every message or none. Use an explicit filter.");

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

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "IS", "NULL", "TRUE", "FALSE", "IN", "LIKE", "ESCAPE", "EXISTS", "NEWID"
    };

    private static readonly Regex StringLiteral = new("'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"\[[^\]]+\]|(?<![\w.])[A-Za-z_][\w.]*", RegexOptions.Compiled);

    // A predicate (or OR-branch) that references no message property is constant: it matches every
    // message (1=1, (2 = 2), 'a'='a') or none. Rejecting these structurally prevents catch-all rules
    // regardless of spelling; e.g. "MessageName = 'X' OR 1=1" is rejected via its constant branch.
    internal static bool HasConstantBranch(string sqlFilter)
    {
        var withoutLiterals = StringLiteral.Replace(sqlFilter, "''");
        return Branches(withoutLiterals).Any(branch => !ReferencesProperty(branch));
    }

    private static bool ReferencesProperty(string expression) =>
        Identifier.Matches(expression).Any(match => !SqlKeywords.Contains(match.Value));

    private static IEnumerable<string> Branches(string expression)
    {
        expression = StripEnclosingParentheses(expression.Trim());
        var parts = SplitTopLevelOr(expression);
        if (parts.Count == 1)
            return parts;
        return parts.SelectMany(Branches);
    }

    private static List<string> SplitTopLevelOr(string expression)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && IsKeywordAt(expression, i, "OR"))
            {
                parts.Add(expression[start..i]);
                start = i + 2;
                i++;
            }
        }
        parts.Add(expression[start..]);
        return parts;
    }

    private static bool IsKeywordAt(string text, int index, string keyword) =>
        string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
        (index == 0 || !IsIdentifierChar(text[index - 1])) &&
        (index + keyword.Length == text.Length || !IsIdentifierChar(text[index + keyword.Length]));

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or ']' or '[';

    private static string StripEnclosingParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[^1] == ')' && ClosesAtEnd(expression))
            expression = expression[1..^1].Trim();
        return expression;
    }

    // True when the opening parenthesis at index 0 is matched by the final character.
    private static bool ClosesAtEnd(string expression)
    {
        var depth = 0;
        for (var i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '(') depth++;
            else if (expression[i] == ')' && --depth == 0)
                return i == expression.Length - 1;
        }
        return false;
    }

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
