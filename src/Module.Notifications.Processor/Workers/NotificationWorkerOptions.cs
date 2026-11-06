using System.ComponentModel.DataAnnotations;

namespace VeloTime.Module.Notifications.Processor.Workers;

public sealed class NotificationWorkerOptions
{
    public const string Section = "Notifications:Worker";

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    [Range(0.25, 60)]
    public double PollIntervalSeconds { get; set; } = 0.25;

    [Range(5, 3600)]
    public int ClaimLeaseSeconds { get; set; } = 60;

    [Range(1, 300)]
    public int RetryInitialDelaySeconds { get; set; } = 2;

    [Range(1, 3600)]
    public int RetryMaximumDelaySeconds { get; set; } = 300;

    internal TimeSpan RetryDelay(int attemptCount)
    {
        var delay = RetryInitialDelaySeconds;
        for (var attempt = 1; attempt < attemptCount && delay < RetryMaximumDelaySeconds; attempt++)
        {
            delay = Math.Min(RetryMaximumDelaySeconds, delay * 2);
        }

        return TimeSpan.FromSeconds(Math.Min(delay, RetryMaximumDelaySeconds));
    }
}
