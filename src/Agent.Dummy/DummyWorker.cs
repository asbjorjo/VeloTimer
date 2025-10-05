using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Agent.Dummy
{
    internal class DummyWorker : IHostedService
    {
        private readonly ILogger<DummyWorker> logger;
        private readonly IMessageBus messagingService;

        public DummyWorker(ILogger<DummyWorker> logger, IMessageBus messagingService)
        {
            this.logger = logger;
            this.messagingService = messagingService;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var random = new Random();
            logger.LogInformation("Dummy Worker started");
            var messages = new List<PassingObserved>();

            while (!cancellationToken.IsCancellationRequested)
            {
                for (int i = 0; i < random.Next(1, 6); i++)
                {
                    var passing = new VeloTime.Module.Timing.Interface.Messages.PassingObserved(
                        DateTimeOffset.UtcNow,
                        "DummyType",
                        Guid.NewGuid().ToString(),
                        Guid.NewGuid(),
                        Guid.NewGuid().ToString(),
                        false,
                        false,
                        false);
                    messages.Add(passing);
                }

                await messagingService.Publish(messages, cancellationToken: cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Dummy Worker stopped");
            return Task.CompletedTask;
        }
    }
}
