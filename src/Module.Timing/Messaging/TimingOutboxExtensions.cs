using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Messaging;

public static class TimingOutboxExtensions
{
    public const string Topic = "velotime-timing";

    /// <summary>How long a command to an agent stays deliverable; an offline agent must not act on stale commands.</summary>
    public static readonly TimeSpan AgentCommandTimeToLive = TimeSpan.FromMinutes(15);

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
        mbb.Produce<InstallationFacilityChangedV1>(producer => producer
            .DefaultTopic(Topic)
            .WithModifier((message, transportMessage) =>
            {
                transportMessage.SessionId = message.InstallationId.ToString();
                transportMessage.MessageId = message.EventId.ToString();
            }));
        // Commands to agents go to the control topic. Each agent's subscription filters on the AgentId property.
        mbb.Produce<ReportCapabilitiesCommandV1>(producer => producer
            .DefaultTopic(AgentControl.Topic)
            .WithModifier((message, transportMessage) =>
            {
                transportMessage.ApplicationProperties[AgentControl.AgentIdHeader] = message.AgentId;
                transportMessage.MessageId = message.RequestId.ToString();
                transportMessage.TimeToLive = AgentCommandTimeToLive;
            }));
        mbb.Produce<ReplayPassingsCommandV1>(producer => producer
            .DefaultTopic(AgentControl.Topic)
            .WithModifier((message, transportMessage) =>
            {
                transportMessage.ApplicationProperties[AgentControl.AgentIdHeader] = message.AgentId;
                transportMessage.MessageId = message.RequestId.ToString();
                // Service Bus drops the command once it expires; the agent also rejects an expired one it receives.
                var remaining = message.ExpiresAtUtc - DateTime.UtcNow;
                transportMessage.TimeToLive = remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
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
