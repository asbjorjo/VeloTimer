using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Messaging;

public static class TimingOutboxExtensions
{
    public const string Topic = "velotime-timing";

    /// <summary>
    /// Registers every Timing-owned event producer and the shared <c>timing.smb_outbox</c>.
    /// Timing.Api and Timing.Processor both write to and dispatch from the same outbox table,
    /// so both hosts must be able to produce every message type that either of them enqueues.
    /// </summary>
    public static MessageBusBuilder AddTimingEventsWithOutbox(this MessageBusBuilder mbb)
    {
        mbb.Produce<TransponderOwnershipChanged>(producer => producer
            .DefaultTopic(Topic)
            .WithModifier((message, transportMessage) =>
            {
                transportMessage.SessionId = message.TransponderId.ToString();
                transportMessage.MessageId = message.EventId.ToString();
            }));
        mbb.Produce<PassingRecordedV1>(producer => producer
            .DefaultTopic(Topic)
            .WithModifier((message, transportMessage) =>
            {
                transportMessage.SessionId = message.TransponderId.ToString();
                transportMessage.MessageId = message.PassingId.ToString();
            }));
        mbb.UseOutbox();
        mbb.AddOutboxUsingDbContext<TimingDbContext>(options =>
        {
            options.PostgreSqlSettings.DatabaseSchemaName = "timing";
            options.MaintainSequence = true;
            // A failed oldest event must not be overtaken by a later event in the same batch.
            options.PollBatchSize = 1;
            options.PollIdleSleep = TimeSpan.FromSeconds(5);
            options.MaxDeliveryAttempts = int.MaxValue;
            options.MessageCleanup = new OutboxMessageCleanupSettings
            {
                Enabled = true,
                BatchSize = 100,
                Age = TimeSpan.FromDays(3),
                Interval = TimeSpan.FromHours(1)
            };
        });
        return mbb;
    }
}
