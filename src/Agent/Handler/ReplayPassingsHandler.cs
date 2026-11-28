using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlimMessageBus;
using VeloTime.Agent.Capabilities;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Replay;

namespace VeloTime.Agent.Handler;

/// <summary>
/// Accepts or rejects a replay and hands accepted ones to <see cref="PassingReplayWorker"/>, so the command is
/// acknowledged quickly and a long replay does not hold the Service Bus message lock.
/// </summary>
public class ReplayPassingsHandler(PassingReplayService replays, PassingReplayWorker worker, IOptions<AgentOptions> options,
    ILogger<ReplayPassingsHandler> logger) : IConsumer<ReplayPassingsCommandV1>
{
    public async Task OnHandle(ReplayPassingsCommandV1 message, CancellationToken cancellationToken)
    {
        if (!ControlCommandGuard.IsForThisAgent(message.AgentId, options.Value, logger, nameof(ReplayPassingsCommandV1)))
            return;
        if (await replays.AcceptAsync(message, cancellationToken) is { } accepted)
            worker.Enqueue(accepted);
    }
}
