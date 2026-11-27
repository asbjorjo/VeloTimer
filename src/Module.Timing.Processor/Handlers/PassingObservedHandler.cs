using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Handlers;

public class PassingObservedHandler(IAgentService agentService, InstallationService installationService) : IConsumer<PassingObservedV1>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    public async Task OnHandle(PassingObservedV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle PassingObservedV1");

        activity?.SetTag("TransponderSystemId", message.TransponderId);
        activity?.SetTag("TransponderType", message.TransponderType);
        activity?.SetTag("LoopSystemId", message.LoopId);
        activity?.SetTag("SourcePassingId", message.SourcePassingId);

        Context.Headers.TryGetValue("AgentId", out object? agentIdObj);
        string AgentId = agentIdObj?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(AgentId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "AgentId is missing in message properties");
            throw new InvalidOperationException("AgentId is missing in message properties");
        }

        activity?.SetTag("AgentId", AgentId);

        // Stores the fact and enqueues PassingRecordedV1 atomically; duplicates and conflicts publish nothing.
        await agentService.RegisterPassingAsync(new PassingObservation(
            AgentId: AgentId,
            SourcePassingId: message.SourcePassingId,
            TransponderId: message.TransponderId,
            TransponderType: message.TransponderType,
            LoopId: message.LoopId,
            Time: message.Time,
            LowBattery: message.LowBattery,
            LowStrength: message.LowStrength,
            LowHits: message.LowHits), cancellationToken);

        var installation = await installationService.GetInstallationForAgent(AgentId, cancellationToken);
        if (installation is not null)
        {
            await installationService.UpdateLastSeenAsync(installation, message.Time, cancellationToken);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
