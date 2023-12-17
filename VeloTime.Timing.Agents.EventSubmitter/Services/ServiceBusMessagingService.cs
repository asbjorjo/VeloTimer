using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Agents.EventSubmitter.Services;

internal class ServiceBusMessagingService : IExternalMessagingService
{
    private readonly MessageBusOptions _options;
    private readonly Logger<ServiceBusMessagingService> _logger;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    public ServiceBusMessagingService(MessageBusOptions options, Logger<ServiceBusMessagingService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _client = new ServiceBusClient(_options.ConnectionString);
        _sender = _client.CreateSender(_options.QueueName);
    }

    private static ServiceBusMessage PrepareMessage(TrackPassing passing)
    {
        string messagePassing = JsonSerializer.Serialize(passing);
        string messageId = $"{passing.Track}_{passing.PassingPoint}_{passing.Transponder}_{passing.Time.UtcDateTime.Ticks}";

        var message = new ServiceBusMessage(messagePassing)
        {
            SessionId = passing.Transponder,
            MessageId = messageId
        };

        return message;
    }

    public Task SendStartLoadingFrom(StartLoadingFrom startLoadingFrom)
    {
        throw new NotImplementedException();
    }

    public async Task SendTrackPassing(TrackPassing passing)
    {
        var message = PrepareMessage(passing);

        _logger.LogInformation("passing -- {Track} -  {Time} - {Transponder} - {Loop}", passing.Track, passing.Time, passing.Transponder, passing.PassingPoint);

        await _sender.SendMessageAsync(message);
    }

    public async Task SendTrackPassings(IEnumerable<TrackPassing> passings)
    {
        _logger.LogInformation("Sending {Count} passings", passings.Count());

        Queue<ServiceBusMessage> messages = new();

        foreach (var passing in passings)
        {
            messages.Enqueue(PrepareMessage(passing));
        }

        _logger.LogInformation("Enqueued {Count} messages for sending", messages.Count);

        while (messages.Count > 0)
        {
            using ServiceBusMessageBatch batch = await _sender.CreateMessageBatchAsync();

            while (messages.Count > 0 && batch.TryAddMessage(messages.Peek()))
            {
                messages.Dequeue();
            }

            _logger.LogInformation("Sending - {Count} remaining", messages.Count);

            await _sender.SendMessagesAsync(batch);
        }

        _logger.LogInformation("Sent all messages");
    }
}
