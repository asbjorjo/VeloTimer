using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VeloTimer.Shared.Configuration;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.PassingLoader.Services
{
    public class MessagingService : IMessagingService
    {
        private readonly MessageBusOptions _options;
        private readonly ILogger<MessagingService> _logger;
        private ServiceBusClient _client;
        private ServiceBusSender _sender;

        public MessagingService(MessageBusOptions options, ILogger<MessagingService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;

            _client = new ServiceBusClient(_options.ConnectionString);
            _sender = _client.CreateSender(_options.QueueName);
        }

        private ServiceBusMessage PrepareMessage(PassingRegister passing)
        {
            string messagePassing = JsonSerializer.Serialize(passing);
            var message = new ServiceBusMessage(messagePassing)
            {
                SessionId = passing.TransponderId,
                MessageId = passing.Source
            };

            return message;
        }

        public async Task SubmitPassing(PassingRegister passing)
        {
            var message = PrepareMessage(passing);

            await _sender.SendMessageAsync(message);
        }

        public async Task SubmitPassings(IEnumerable<PassingRegister> passings)
        {
            _logger.LogInformation($"Sending {passings.Count()} passings");

            Queue<ServiceBusMessage> messages = new();

            foreach (var passing in passings)
            {
                messages.Enqueue(PrepareMessage(passing));
            }

            _logger.LogInformation($"Enqueued {messages.Count} messages for sending");

            while (messages.Count > 0)
            {
                using ServiceBusMessageBatch batch = await _sender.CreateMessageBatchAsync();

                while (messages.Count > 0 && batch.TryAddMessage(messages.Peek()))
                {
                    messages.Dequeue();
                }

                _logger.LogInformation($"Sending - {messages.Count} remaining");

                await _sender.SendMessagesAsync(batch);
            }

            _logger.LogInformation("Sent all messages");
        }
    }
}
