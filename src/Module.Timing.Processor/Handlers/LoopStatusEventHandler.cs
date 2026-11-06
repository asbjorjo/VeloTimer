using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Handlers;

public class LoopStatusEventHandler(InstallationService installationService) : IConsumer<LoopStatusEvent>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    public async Task OnHandle(LoopStatusEvent message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle LoopStatusEvent");

        activity?.SetTag("LoopId", message.LoopId);

        Context.Headers.TryGetValue("AgentId", out object? agentIdObj);
        string AgentId = agentIdObj?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(AgentId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "AgentId is missing in message properties");
            throw new InvalidOperationException("AgentId is missing in message properties");
        }

        activity?.SetTag("AgentId", AgentId);

        var installation = await installationService.GetInstallationForAgent(AgentId, cancellationToken);
        
        if (installation is not null)
        {
            await installationService.UpdateLastSeenAsync(installation, message.Time, cancellationToken);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
