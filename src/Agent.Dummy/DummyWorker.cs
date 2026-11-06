using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Events;

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
            var messages = new List<PassingEvent>();

            while (!cancellationToken.IsCancellationRequested)
            {
                for (int i = 0; i < random.Next(1, 6); i++)
                {
                    PassingEvent passing = new(
                        Time: DateTime.UtcNow,
                        TransponderType: "DummyType",
                        TransponderId: Guid.NewGuid().ToString(),
                        LoopId: Guid.NewGuid().ToString()
                    );
                    messages.Add(passing);
                }

                foreach (var message in messages)
                    await messagingService.Publish(message, cancellationToken: cancellationToken);
                messages.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Dummy Worker stopped");
            return Task.CompletedTask;
        }
    }
}
