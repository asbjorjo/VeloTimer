using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Contracts;
using VeloTime.Timing.Services;

namespace VeloTime.Timing.Agents.EventSubmitter.Services
{
    public class StartupService : BackgroundService
    {
        private readonly IMessagingService _messagingService;

        public StartupService(IMessagingService messagingService) => _messagingService = messagingService;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var start = new StartLoadingFrom { StartTime = DateTime.UtcNow - TimeSpan.FromHours(-3) };
            await _messagingService.SendStartLoadingFrom(start);
        }
    }
}
