using AutoMapper;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Models;
using VeloTimer.Shared.Models.Timing;
using VeloTimer.Shared.Configuration;
using System;

namespace VeloTimer.AmmcLoad.Services
{
    public class MessagingService : IMessagingService
    {
        private readonly MessageBusOptions _options;
        private readonly ILogger<MessagingService> _logger;
        private readonly IMapper _mapper;
        private ServiceBusClient _client;
        private ServiceBusSender _sender;

        public MessagingService(MessageBusOptions options, ILogger<MessagingService> logger, IMapper mapper)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
            _mapper = mapper;
        }

        private void Connect()
        {
            if (_client is null || _sender is null)
            {
                _client = new ServiceBusClient(_options.ConnectionString);
                _sender = _client.CreateSender(_options.QueueName);
            }
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

        public async Task SubmitPassing(PassingAmmc passing)
        {
            Connect();

            var toRegister = _mapper.Map<PassingRegister>(passing);

            var message = PrepareMessage(toRegister);

            await _sender.SendMessageAsync(message);
        }

        public async Task SubmitPassings(IEnumerable<PassingAmmc> passings)
        {
            Connect();

            _logger.LogInformation($"Sending {passings.Count()} passings");

            var registerpassings = _mapper.Map<IEnumerable<PassingRegister>>(passings);
            Queue<ServiceBusMessage> messages = new();

            foreach (var passing in registerpassings)
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
