using System.Diagnostics;
using SlimMessageBus;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Handlers;

/// <summary>Applies an agent's <see cref="PassingReplayStatusV1"/> to its replay request; stale and unknown reports are ignored.</summary>
public class PassingReplayStatusHandler(PassingReplayRequestService replays) : IConsumer<PassingReplayStatusV1>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    public async Task OnHandle(PassingReplayStatusV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle PassingReplayStatusV1");
        Context.Headers.TryGetValue(AgentControl.AgentIdHeader, out object? agentIdObj);
        var agentId = agentIdObj?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(agentId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "AgentId is missing in message properties");
            throw new InvalidOperationException("AgentId is missing in message properties");
        }
        activity?.SetTag("AgentId", agentId);
        activity?.SetTag("RequestId", message.RequestId);
        activity?.SetTag("State", message.State);
        await replays.ApplyStatusAsync(agentId, message, cancellationToken);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
