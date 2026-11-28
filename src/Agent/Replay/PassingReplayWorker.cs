using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VeloTime.Agent.Model;

namespace VeloTime.Agent.Replay;

/// <summary>Runs accepted replays one at a time in the background, and resumes replays a restart interrupted.</summary>
public sealed class PassingReplayWorker(PassingReplayService replays, ILogger<PassingReplayWorker> log) : BackgroundService
{
    private readonly Channel<ReplayRequest> queue = Channel.CreateUnbounded<ReplayRequest>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Queues an accepted request (see <see cref="PassingReplayService.AcceptAsync"/>).</summary>
    public void Enqueue(ReplayRequest request) => queue.Writer.TryWrite(request);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            foreach (var interrupted in await replays.ReserveInterruptedAsync(stoppingToken))
            {
                log.LogInformation("Resuming replay {RequestId} interrupted by a restart.", interrupted.RequestId);
                Enqueue(interrupted);
            }
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Without the table (migrations not applied yet) there is nothing to resume.
            log.LogWarning(exception, "Could not look for interrupted replays.");
        }

        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await replays.RunAsync(request, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
