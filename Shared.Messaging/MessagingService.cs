using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace VeloTime.Shared.Messaging
{
    public class MessagingService : IMessagingService
    {
        protected readonly ILogger<MessagingService> _logger;
        private readonly MessageBusOptions _options;
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

        public async Task SendMessage(IMessage message)
        {
            var serviceBusMessage = ToServiceBusMessage(message);

            await _sender.SendMessageAsync(serviceBusMessage);
        }

        public async Task SendMessages(IEnumerable<IMessage> messages)
        {
            _logger.LogInformation("Sending {Count} passings", messages.Count());

            Queue<ServiceBusMessage> sbmessages = new();
            foreach (var message in messages)
            {
                sbmessages.Enqueue(ToServiceBusMessage(message));
            }

            _logger.LogInformation("Enqueued {Count} messages for sending", sbmessages.Count);

            while (sbmessages.Count > 0)
            {
                using ServiceBusMessageBatch batch = await _sender.CreateMessageBatchAsync();

                while (sbmessages.Count > 0 && batch.TryAddMessage(sbmessages.Peek()))
                {
                    sbmessages.Dequeue();
                }

                _logger.LogInformation("Sending - {Count} remaining", sbmessages.Count);

                await _sender.SendMessagesAsync(batch);
            }

            _logger.LogInformation("Sent all messages");
        }

        private static ServiceBusMessage ToServiceBusMessage(IMessage message)
        {
            var messageBody = JsonSerializer.Serialize(message.Content);

            var serviceBusMessage = new ServiceBusMessage(messageBody)
            {
                MessageId = message.Id,
                Subject = message.Topic,
                SessionId = message.Subject
            };

            return serviceBusMessage;
        }
    }
}
