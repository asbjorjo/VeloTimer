using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing;

/// <summary>Marks passing replay requests the agent never answered as expired once their command has expired.</summary>
public sealed class PassingReplayExpiryWorker(IServiceScopeFactory scopes, ILogger<PassingReplayExpiryWorker> log,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider ?? TimeProvider.System);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var expired = await scope.ServiceProvider.GetRequiredService<PassingReplayRequestService>()
                        .ExpireUnansweredAsync(stoppingToken);
                    if (expired > 0) log.LogWarning("Marked {Count} unanswered passing replay requests as expired.", expired);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    log.LogWarning(exception, "Could not expire unanswered passing replay requests.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
