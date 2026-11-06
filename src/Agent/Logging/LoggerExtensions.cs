using Microsoft.Extensions.Logging;

namespace VeloTime.Agent.Logging;

internal static partial class LoggerExtensions
{
    public static void LogAgentStarted(this ILogger logger)
    {
        logger.LogInformation("VeloTime Agent started.");
    }

    public static void LogAgentStopped(this ILogger logger)
    {
        logger.LogInformation("VeloTime Agent stopped.");
    }

    public static void LogSendingEvents(this ILogger logger, string eventName, int count)
    {
        logger.LogInformation("Sending {Count} events of type {EventName}.", count, eventName);
    }
}
