using SlimMessageBus.Host;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;

namespace VeloTime.Module.Statistics.Storage;

public static class StatisticsOutboxExtensions
{
    /// <summary>
    /// Registers Statistics-owned transactional persistence on a host's configured bus.
    /// The host owns producers, transport, explicit startup and worker scheduling.
    /// SlimMessageBus initializes statistics.smb_outbox; it is not an EF entity.
    /// </summary>
    public static MessageBusBuilder AddStatisticsOutbox(this MessageBusBuilder builder)
    {
        builder.UseOutbox();
        builder.AddOutboxUsingDbContext<StatisticsDbContext>(options =>
        {
            options.PostgreSqlSettings.DatabaseSchemaName = "statistics";
            options.MaintainSequence = true;
            options.PollBatchSize = 1;
            options.PollIdleSleep = TimeSpan.FromSeconds(5);
            options.MaxDeliveryAttempts = int.MaxValue;
            // Keep dispatched requests until a deployment owner approves replay/retention.
            options.MessageCleanup = new OutboxMessageCleanupSettings { Enabled = false };
        });
        return builder;
    }
}
