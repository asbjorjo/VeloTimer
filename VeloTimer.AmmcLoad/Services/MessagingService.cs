using AutoMapper;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Data;
using VeloTimer.AmmcLoad.Models;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.AmmcLoad.Services
{
    public class MessagingService : IMessagingService
    {
        private readonly MessageBusSettings _options;
        private readonly ILogger<MessagingService> _logger;
        private readonly IMapper _mapper;
        private ServiceBusClient _client;
        private ServiceBusSender _sender;

        public MessagingService(IOptions<MessageBusSettings> options, ILogger<MessagingService> logger, IMapper mapper)
        {
            _options = options.Value;
            _logger = logger;
            _mapper = mapper;
        }

        private void Connect()
        {
            _client = new ServiceBusClient(_options.ConnectionString);
            _sender = _client.CreateSender(_options.QueueName);
        }

        public async Task SubmitPassing(PassingAmmc passing)
        {
            if (_client is null || _sender is null)
            {
                Connect();
            }

            var toRegister = _mapper.Map<PassingRegister>(passing);

            string messagePassing = JsonSerializer.Serialize(toRegister);
            var message = new ServiceBusMessage(messagePassing);

            await _sender.SendMessageAsync(message);
        }
    }
}
