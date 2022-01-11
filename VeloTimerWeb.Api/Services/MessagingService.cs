using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTimer.Shared.Configuration;

namespace VeloTimerWeb.Api.Services
{
    public class MessagingService : IMessagingService
    {
        private readonly MessageBusOptions _settings;
        private readonly ILogger<MessagingService> _logger;

        public MessagingService(
            IOptions<MessageBusOptions> options,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<MessagingService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }
    }
}
