using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Agent.Identity;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Events;

namespace VeloTime.Agent.Dummy
{
    /// <summary>
    /// Simulates a small group of riders circulating over a fixed set of loops. Every passing carries a stable
    /// SourcePassingId from PassingId.Create, and the worker occasionally redelivers an earlier passing so Timing's
    /// deduplication is exercised. Passings carry a numeric strength and hit count (occasionally flagged low), and
    /// every loop sends a status sample every <see cref="StatusInterval"/> with random-walk noise, so the loop health
    /// pages have data to graph in development.
    /// </summary>
    internal class DummyWorker : IHostedService
    {
        private static readonly string[] Loops = ["dummy-loop-1", "dummy-loop-2", "dummy-loop-3"];
        // Valid ProChip IDs (106187718 is printed HT-24422).
        private static readonly string[] Transponders = ["106187718", "106187719", "106187720", "106187721", "106187722"];
        private const double RedeliveryProbability = 0.1;
        private const double WeakReadProbability = 0.08;
        private const double NominalStrength = 120;
        private const double LowStrengthThreshold = 70;
        private const int NominalHits = 40;
        private const int LowHitsThreshold = 12;
        private const double NominalNoise = 20;
        private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(5);
        // 35 °C in kelvin; the contract uses SI units.
        private const double NominalTemperature = 308.15;

        private readonly ILogger<DummyWorker> logger;
        private readonly IMessageBus messagingService;
        private CancellationTokenSource? stopping;
        private Task? running;
        private Task? status;

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
            status = RunStatusAsync(stopping.Token);
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

                    var time = DateTime.UtcNow;
                    var passing = new PassingObservedV1(
                        SourcePassingId: PassingId.Create("Dummy", transponder, Loops[loop],
                            time.ToString("O", CultureInfo.InvariantCulture)),
                        Time: time,
                        TransponderType: TransponderTypes.MylapsProChip,
                        TransponderId: transponder,
                        LoopId: Loops[loop]);
                    passing = WithSignal(passing, random);
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

        private static PassingObservedV1 WithSignal(PassingObservedV1 passing, Random random)
        {
            var weak = random.NextDouble() < WeakReadProbability;
            var strength = weak
                ? LowStrengthThreshold * (0.6 + 0.4 * random.NextDouble())
                : NominalStrength + 20 * (random.NextDouble() - 0.5);
            var hits = weak && random.NextDouble() < 0.5
                ? random.Next(4, LowHitsThreshold)
                : NominalHits + random.Next(-8, 9);
            return passing with
            {
                Strength = Math.Round(strength, 1),
                Hits = hits,
                LowStrength = strength < LowStrengthThreshold,
                LowHits = hits < LowHitsThreshold,
                LowBattery = random.NextDouble() < 0.01
            };
        }

        private async Task RunStatusAsync(CancellationToken cancellationToken)
        {
            var random = new Random();
            var noise = Loops.ToDictionary(l => l, _ => NominalNoise);
            var temperature = NominalTemperature;
            try
            {
                using var timer = new PeriodicTimer(StatusInterval);
                do
                {
                    var time = DateTime.UtcNow;
                    temperature = Math.Clamp(temperature + (random.NextDouble() - 0.5) * 0.2, NominalTemperature - 10, NominalTemperature + 15);
                    foreach (var loop in Loops)
                    {
                        // Random walk pulled back towards the nominal level, with rare spikes of interference.
                        var next = noise[loop] + (random.NextDouble() - 0.5) * 4 + (NominalNoise - noise[loop]) * 0.05;
                        if (random.NextDouble() < 0.01) next += 30;
                        noise[loop] = Math.Clamp(next, 0, 100);
                        var sample = new LoopStatusEvent(time, loop, Math.Round(noise[loop], 1))
                        {
                            LoopTriggers = random.Next(0, 3),
                            DecoderTemperature = Math.Round(temperature, 2),
                            InputVoltage = Math.Round(12 + (random.NextDouble() - 0.5) * 0.4, 2),
                            TimeSynchronised = random.NextDouble() > 0.005,
                            SatellitesInUse = random.Next(6, 12),
                            DecoderId = "dummy-decoder",
                            NoiseSharesStrengthScale = true
                        };
                        await messagingService.Publish(sample, cancellationToken: cancellationToken);
                    }
                } while (await timer.WaitForNextTickAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            stopping?.Cancel();
            if (running is not null) await running.WaitAsync(cancellationToken);
            if (status is not null) await status.WaitAsync(cancellationToken);
            logger.LogInformation("Dummy Worker stopped");
        }
    }
}
