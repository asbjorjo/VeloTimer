using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.MessageBus.Topology;

namespace VeloTime.Workers.Test;

public sealed class MessageBusTopologyReconcilerTests
{
    [Fact]
    public async Task Empty_broker_creates_subscriptions_with_declared_first_rule_and_never_default_rule()
    {
        var broker = new FakeBroker();

        await Reconcile(broker, Topology(Topic("t1",
            Subscription("s1", requiresSession: true, Sql("A", "MessageName = 'A'"), Sql("B", "MessageName = 'B'")))));

        Assert.Equal(
        [
            "create-topic t1",
            "create-subscription t1/s1 session=True first-rule=A",
            "create-rule t1/s1/B",
        ], broker.Writes);
        Assert.Equal(["A", "B"], broker.RuleNames("t1", "s1"));
        Assert.DoesNotContain(broker.Operations, op => op.Contains(RuleProperties.DefaultRuleName));
        Assert.True(broker.Subscriptions[("t1", "s1")].RequiresSession);
    }

    [Fact]
    public async Task Existing_default_rule_is_removed_only_after_declared_rules_exist()
    {
        var broker = new FakeBroker();
        broker.SeedTopic("t1");
        broker.SeedSubscription("t1", "s1", requiresSession: false,
            Rule(RuleProperties.DefaultRuleName, new TrueRuleFilter()));

        await Reconcile(broker, Topology(Topic("t1", Subscription("s1", requiresSession: false, Sql("A", "MessageName = 'A'")))));

        Assert.Equal(
        [
            "create-rule t1/s1/A",
            $"delete-rule t1/s1/{RuleProperties.DefaultRuleName}",
        ], broker.Writes);
        Assert.Equal(["A"], broker.RuleNames("t1", "s1"));
    }

    [Fact]
    public async Task Changed_filters_are_updated_and_undeclared_rules_removed()
    {
        var broker = new FakeBroker();
        broker.SeedTopic("t1");
        broker.SeedSubscription("t1", "s1", requiresSession: false,
            Rule("A", new SqlRuleFilter("MessageName = 'Old'")),
            Rule("Legacy", new SqlRuleFilter("MessageName = 'Legacy'")));

        await Reconcile(broker, Topology(Topic("t1", Subscription("s1", requiresSession: false, Sql("A", "MessageName = 'A'")))));

        Assert.Equal(["update-rule t1/s1/A", "delete-rule t1/s1/Legacy"], broker.Writes);
        Assert.Equal(new SqlRuleFilter("MessageName = 'A'"), broker.Rules[("t1", "s1")].Single().Filter);
    }

    [Fact]
    public async Task Second_run_against_converged_broker_makes_no_changes()
    {
        var broker = new FakeBroker();
        var topology = Topology(Topic("t1",
            Subscription("s1", requiresSession: true, Sql("A", "MessageName = 'A'"), Correlation("B")),
            Subscription("s2", requiresSession: false, Sql("C", "MessageName = 'C'"))));

        await Reconcile(broker, topology);
        broker.Writes.Clear();
        await Reconcile(broker, topology);

        Assert.Empty(broker.Writes);
    }

    [Fact]
    public async Task Undeclared_subscriptions_and_topics_are_left_untouched()
    {
        var broker = new FakeBroker();
        broker.SeedTopic("t1");
        broker.SeedTopic("other");
        broker.SeedSubscription("t1", "agent-1", requiresSession: false,
            Rule(RuleProperties.DefaultRuleName, new TrueRuleFilter()));

        await Reconcile(broker, Topology(Topic("t1", Subscription("s1", requiresSession: false, Sql("A", "MessageName = 'A'")))));

        Assert.Equal([RuleProperties.DefaultRuleName], broker.RuleNames("t1", "agent-1"));
        Assert.DoesNotContain(broker.Writes, op => op.Contains("agent-1") || op.Contains("other"));
    }

    [Fact]
    public async Task Session_mismatch_fails_without_touching_rules()
    {
        var broker = new FakeBroker();
        broker.SeedTopic("t1");
        broker.SeedSubscription("t1", "s1", requiresSession: false,
            Rule(RuleProperties.DefaultRuleName, new TrueRuleFilter()));

        var error = await Assert.ThrowsAsync<MessageBusTopologyException>(() =>
            Reconcile(broker, Topology(Topic("t1", Subscription("s1", requiresSession: true, Sql("A", "MessageName = 'A'"))))));

        Assert.Contains("requiresSession", error.Message);
        Assert.Empty(broker.Writes);
    }

    [Fact]
    public async Task Mutable_subscription_properties_are_updated()
    {
        var broker = new FakeBroker();
        broker.SeedTopic("t1");
        broker.SeedSubscription("t1", "s1", requiresSession: false, Rule("A", new SqlRuleFilter("MessageName = 'A'")));
        var subscription = Subscription("s1", requiresSession: false, Sql("A", "MessageName = 'A'"));
        subscription.MaxDeliveryCount = 3;
        subscription.LockDuration = TimeSpan.FromSeconds(45);

        await Reconcile(broker, Topology(Topic("t1", subscription)));

        Assert.Equal(["update-subscription t1/s1"], broker.Writes);
        Assert.Equal(3, broker.Subscriptions[("t1", "s1")].MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromSeconds(45), broker.Subscriptions[("t1", "s1")].LockDuration);
    }

    [Fact]
    public async Task Creation_race_falls_back_to_reconciling_existing_subscription()
    {
        var broker = new FakeBroker { RaceOnSubscriptionCreate = true };
        broker.SeedTopic("t1");

        await Reconcile(broker, Topology(Topic("t1", Subscription("s1", requiresSession: false, Sql("A", "MessageName = 'A'")))));

        Assert.Equal(["A"], broker.RuleNames("t1", "s1"));
    }

    [Fact]
    public void Correlation_filter_maps_all_declared_conditions()
    {
        var filter = Assert.IsType<CorrelationRuleFilter>(MessageBusTopologyReconciler.ToFilter(Correlation("B")));

        Assert.Equal("subject-B", filter.Subject);
        Assert.Equal("B", filter.ApplicationProperties["MessageName"]);
    }

    [Theory]
    [InlineData("no-rules")]
    [InlineData("both-filters")]
    [InlineData("no-filter")]
    [InlineData("catch-all")]
    [InlineData("reserved-name")]
    [InlineData("duplicate-rule")]
    [InlineData("duplicate-topic")]
    [InlineData("empty-correlation")]
    [InlineData("no-topics")]
    public async Task Invalid_topology_is_rejected_before_any_broker_call(string scenario)
    {
        var rule = Sql("A", "MessageName = 'A'");
        var subscription = Subscription("s1", requiresSession: false, rule);
        var topology = Topology(Topic("t1", subscription));
        switch (scenario)
        {
            case "no-rules": subscription.Rules.Clear(); break;
            case "both-filters": rule.CorrelationFilter = new() { Subject = "x" }; break;
            case "no-filter": rule.SqlFilter = null; break;
            case "catch-all": rule.SqlFilter = " 1 = 1 "; break;
            case "reserved-name": rule.Name = "$Default"; break;
            case "duplicate-rule": subscription.Rules.Add(Sql("a", "MessageName = 'B'")); break;
            case "duplicate-topic": topology.Topics.Add(Topic("T1")); break;
            case "empty-correlation": rule.SqlFilter = null; rule.CorrelationFilter = new(); break;
            case "no-topics": topology.Topics.Clear(); break;
        }
        var broker = new FakeBroker();

        await Assert.ThrowsAsync<MessageBusTopologyException>(() => Reconcile(broker, topology));

        Assert.Empty(broker.Operations);
    }

    [Fact]
    public void Bundled_default_topology_binds_and_is_valid()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "topology.json"), optional: false)
            .Build();
        var topology = configuration.GetSection(MessageBusTopologyOptions.Section).Get<MessageBusTopologyOptions>()!;

        Assert.Empty(MessageBusTopologyValidator.Validate(topology));
        var timing = topology.Topics.Single(topic => topic.Name == "velotime-agent").Subscriptions.Single(s => s.Name == "timing");
        Assert.True(timing.RequiresSession);
        Assert.Equal("MessageName = 'PassingEvent'", timing.Rules.Single().SqlFilter);
        Assert.All(topology.Topics.SelectMany(topic => topic.Subscriptions), subscription => Assert.NotEmpty(subscription.Rules));
    }

    private static Task Reconcile(FakeBroker broker, MessageBusTopologyOptions topology) =>
        new MessageBusTopologyReconciler(broker, NullLogger<MessageBusTopologyReconciler>.Instance)
            .ReconcileAsync(topology, CancellationToken.None);

    private static MessageBusTopologyOptions Topology(params TopicDefinition[] topics) => new() { Topics = [.. topics] };

    private static TopicDefinition Topic(string name, params SubscriptionDefinition[] subscriptions) =>
        new() { Name = name, Subscriptions = [.. subscriptions] };

    private static SubscriptionDefinition Subscription(string name, bool requiresSession, params RuleDefinition[] rules) =>
        new() { Name = name, RequiresSession = requiresSession, Rules = [.. rules] };

    private static RuleDefinition Sql(string name, string filter) => new() { Name = name, SqlFilter = filter };

    private static RuleDefinition Correlation(string name) => new()
    {
        Name = name,
        CorrelationFilter = new() { Subject = $"subject-{name}", Properties = { ["MessageName"] = name } }
    };

    private static RuleProperties Rule(string name, RuleFilter filter) =>
        ServiceBusModelFactory.RuleProperties(name, filter, null);

    private sealed class FakeBroker : IServiceBusTopologyClient
    {
        public Dictionary<string, TopicProperties> Topics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string Topic, string Subscription), SubscriptionProperties> Subscriptions { get; } = [];
        public Dictionary<(string Topic, string Subscription), List<RuleProperties>> Rules { get; } = [];
        public List<string> Operations { get; } = [];
        public List<string> Writes { get; } = [];
        public bool RaceOnSubscriptionCreate { get; init; }

        public IReadOnlyList<string> RuleNames(string topic, string subscription) =>
            Rules[(topic, subscription)].Select(rule => rule.Name).ToList();

        public void SeedTopic(string name) =>
            Topics[name] = ServiceBusModelFactory.TopicProperties(name, 1024, false, TimeSpan.MaxValue, TimeSpan.MaxValue,
                TimeSpan.FromMinutes(10), true, EntityStatus.Active, false);

        public void SeedSubscription(string topic, string name, bool requiresSession, params RuleProperties[] rules)
        {
            Subscriptions[(topic, name)] = ServiceBusModelFactory.SubscriptionProperties(topic, name, TimeSpan.FromMinutes(1),
                requiresSession, TimeSpan.MaxValue, TimeSpan.MaxValue, false, 10, true, EntityStatus.Active, null, null, string.Empty);
            Rules[(topic, name)] = [.. rules];
        }

        public Task<TopicProperties?> GetTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            Operations.Add($"get-topic {topicName}");
            return Task.FromResult(Topics.GetValueOrDefault(topicName));
        }

        public Task CreateTopicAsync(CreateTopicOptions options, CancellationToken cancellationToken)
        {
            Write($"create-topic {options.Name}");
            SeedTopic(options.Name);
            return Task.CompletedTask;
        }

        public Task UpdateTopicAsync(TopicProperties topic, CancellationToken cancellationToken)
        {
            Write($"update-topic {topic.Name}");
            return Task.CompletedTask;
        }

        public Task<SubscriptionProperties?> GetSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            Operations.Add($"get-subscription {topicName}/{subscriptionName}");
            return Task.FromResult(Subscriptions.GetValueOrDefault((topicName, subscriptionName)));
        }

        public Task CreateSubscriptionAsync(CreateSubscriptionOptions options, CreateRuleOptions rule, CancellationToken cancellationToken)
        {
            if (RaceOnSubscriptionCreate)
            {
                // Another writer created it first, with the broker's default rule.
                SeedSubscription(options.TopicName, options.SubscriptionName, options.RequiresSession,
                    Rule(RuleProperties.DefaultRuleName, new TrueRuleFilter()));
                throw new ServiceBusException("exists", ServiceBusFailureReason.MessagingEntityAlreadyExists);
            }

            Write($"create-subscription {options.TopicName}/{options.SubscriptionName} session={options.RequiresSession} first-rule={rule.Name}");
            SeedSubscription(options.TopicName, options.SubscriptionName, options.RequiresSession,
                Rule(rule.Name, rule.Filter));
            return Task.CompletedTask;
        }

        public Task UpdateSubscriptionAsync(SubscriptionProperties subscription, CancellationToken cancellationToken)
        {
            Write($"update-subscription {subscription.TopicName}/{subscription.SubscriptionName}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RuleProperties>> GetRulesAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            Operations.Add($"get-rules {topicName}/{subscriptionName}");
            return Task.FromResult<IReadOnlyList<RuleProperties>>(Rules[(topicName, subscriptionName)].ToList());
        }

        public Task CreateRuleAsync(string topicName, string subscriptionName, CreateRuleOptions rule, CancellationToken cancellationToken)
        {
            Write($"create-rule {topicName}/{subscriptionName}/{rule.Name}");
            Rules[(topicName, subscriptionName)].Add(ServiceBusModelFactory.RuleProperties(rule.Name, rule.Filter, rule.Action));
            return Task.CompletedTask;
        }

        public Task UpdateRuleAsync(string topicName, string subscriptionName, RuleProperties rule, CancellationToken cancellationToken)
        {
            Write($"update-rule {topicName}/{subscriptionName}/{rule.Name}");
            return Task.CompletedTask;
        }

        public Task DeleteRuleAsync(string topicName, string subscriptionName, string ruleName, CancellationToken cancellationToken)
        {
            if (Rules[(topicName, subscriptionName)].Count == 1)
                throw new InvalidOperationException("Deleting the last rule would leave the subscription unable to receive.");
            Write($"delete-rule {topicName}/{subscriptionName}/{ruleName}");
            Rules[(topicName, subscriptionName)].RemoveAll(rule => rule.Name == ruleName);
            return Task.CompletedTask;
        }

        private void Write(string operation)
        {
            Operations.Add(operation);
            Writes.Add(operation);
        }
    }
}
