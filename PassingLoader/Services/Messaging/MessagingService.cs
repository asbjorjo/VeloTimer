using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public class MessagingService : IMessagingService
    {
        private readonly MessageBusOptions _options;
        private readonly ILogger<MessagingService> _logger;
        private readonly ServiceBusClient _client;
        private readonly ServiceBusSender _sender;

        public MessagingService(MessageBusOptions options, ILogger<MessagingService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;

            _logger.LogInformation("Sending passings to {Queue}", options.QueueName);

            _client = new ServiceBusClient(_options.ConnectionString);
            _sender = _client.CreateSender(_options.QueueName);
        }

        private static ServiceBusMessage PrepareMessage(PassingRegister passing)
        {
            string messagePassing = JsonSerializer.Serialize(passing);
            string messageId = $"{passing.Track}_{passing.LoopId}_{passing.TransponderId}_{passing.Time.UtcDateTime.Ticks}";
            var message = new ServiceBusMessage(messagePassing)
            {
                SessionId = passing.TransponderId,
                MessageId = messageId
            };

            return message;
        }

        public async Task SubmitPassing(PassingRegister passing)
        {
            var message = PrepareMessage(passing);

            _logger.LogInformation("passing -- {Track} -  {Time} - {Transponder} - {Loop}", passing.Track, passing.Time, passing.TransponderId, passing.LoopId);

            await _sender.SendMessageAsync(message);
        }

        public async Task SubmitPassings(IEnumerable<PassingRegister> passings)
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
}
