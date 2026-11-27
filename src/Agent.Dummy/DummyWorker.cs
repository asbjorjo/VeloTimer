using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Events;

namespace VeloTime.Agent.Dummy
{
    /// <summary>
    /// Simulates a small group of riders circulating over a fixed set of loops. Every passing carries a stable
    /// SourcePassingId, and the worker occasionally redelivers an earlier passing so Timing's deduplication is exercised.
    /// </summary>
    internal class DummyWorker : IHostedService
    {
        private static readonly string[] Loops = ["dummy-loop-1", "dummy-loop-2", "dummy-loop-3"];
        private static readonly string[] Transponders = ["1000001", "1000002", "1000003", "1000004", "1000005"];
        private const double RedeliveryProbability = 0.1;

        private readonly ILogger<DummyWorker> logger;
        private readonly IMessageBus messagingService;
        private CancellationTokenSource? stopping;
        private Task? running;

        public DummyWorker(ILogger<DummyWorker> logger, IMessageBus messagingService)
        {
            this.logger = logger;
            this.messagingService = messagingService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Dummy Worker started");
            stopping = new CancellationTokenSource();
            running = RunAsync(stopping.Token);
            return Task.CompletedTask;
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var random = new Random();
            var nextLoop = Transponders.ToDictionary(t => t, _ => random.Next(Loops.Length));
            PassingObservedV1? previous = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var transponder = Transponders[random.Next(Transponders.Length)];
                    var loop = nextLoop[transponder];
                    nextLoop[transponder] = (loop + 1) % Loops.Length;

                    var passing = new PassingObservedV1(
                        SourcePassingId: Guid.NewGuid(),
                        Time: DateTime.UtcNow,
                        TransponderType: "DummyType",
                        TransponderId: transponder,
                        LoopId: Loops[loop]);
                    await messagingService.Publish(passing, cancellationToken: cancellationToken);

                    if (previous is not null && random.NextDouble() < RedeliveryProbability)
                        await messagingService.Publish(previous, cancellationToken: cancellationToken);
                    previous = passing;

                    await Task.Delay(TimeSpan.FromSeconds(random.Next(1, 6)), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            stopping?.Cancel();
            if (running is not null) await running.WaitAsync(cancellationToken);
            logger.LogInformation("Dummy Worker stopped");
        }
    }
}
