using SlimMessageBus.Host;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

public static class StatisticsProcessorOutbox
{
    public static MessageBusBuilder AddStatisticsProcessorOutbox(this MessageBusBuilder builder)
    {
        builder.UseOutbox(true, type => type == typeof(SampleComplete) || type == typeof(EntryCreated));
        builder.AddOutboxUsingDbContext<StatisticsDbContext>(options =>
        {
            options.PostgreSqlSettings.DatabaseSchemaName = "statistics_processor";
            options.MaintainSequence = true;
            options.PollBatchSize = 1;
            options.PollIdleSleep = TimeSpan.FromSeconds(1);
            options.MaxDeliveryAttempts = int.MaxValue;
            options.MessageCleanup = new OutboxMessageCleanupSettings { Enabled = false };
            options.MessageTypeResolver = new RequiredMessageTypeResolver(options.MessageTypeResolver);
        });
        return builder;
    }

    // SMB treats an unresolved type as aborted. Throw instead: retain and retry the
    // oldest row, making incompatible deployment/type removal an observable failure.
    private sealed class RequiredMessageTypeResolver(IMessageTypeResolver inner) : IMessageTypeResolver
    {
        public string ToName(Type messageType) => inner.ToName(messageType);
        public Type ToType(string name) => inner.ToType(name)
            ?? throw new InvalidOperationException($"Cannot resolve Statistics outbox type {name}; delivery remains blocked.");
    }
}
