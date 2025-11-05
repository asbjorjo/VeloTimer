using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Timing.Handlers;

public class PassingSavedHandler(ILogger<PassingSavedHandler> logger) : IConsumer<PassingSaved>
{
    public async Task OnHandle(PassingSaved message, CancellationToken token)
    {
        logger.LogInformation("Passing saved: Time={Time}, TransponderId={TransponderId}, TimingPointId={TimingPointId}",
            message.Time, message.TransponderId, message.TimingPoint);
        await Task.CompletedTask;
    }
}
