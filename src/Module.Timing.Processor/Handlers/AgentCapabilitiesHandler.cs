using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Handlers;

/// <summary>
/// Stores an agent's latest <see cref="AgentCapabilitiesV1"/> on its installation, creating the installation when the
/// agent is new (as a layout report does). Reports older than the stored one are ignored, so redelivery and
/// out-of-order delivery are harmless.
/// </summary>
public class AgentCapabilitiesHandler(InstallationService installationService, ILogger<AgentCapabilitiesHandler> log)
    : IConsumer<AgentCapabilitiesV1>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    public async Task OnHandle(AgentCapabilitiesV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle AgentCapabilitiesV1");

        Context.Headers.TryGetValue(AgentControl.AgentIdHeader, out object? agentIdObj);
        string agentId = agentIdObj?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(agentId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "AgentId is missing in message properties");
            throw new InvalidOperationException("AgentId is missing in message properties");
        }
        activity?.SetTag("AgentId", agentId);

        var installation =
            await installationService.GetInstallationForAgent(agentId, cancellationToken) ??
            await installationService.CreateInstallationForAgent(agentId, cancellationToken: cancellationToken);

        var capabilities = (message.Capabilities ?? []).Select(capability => new AgentCapabilityEntry
        {
            Name = capability.Name,
            PassingReplay = capability.PassingReplay is not { } replay ? null : new PassingReplayCapabilityEntry
            {
                Mode = replay.Mode,
                OldestAvailableUtc = replay.OldestAvailableUtc,
                MaxRangeSeconds = replay.MaxRange?.TotalSeconds,
                SupportsLoopFilter = replay.SupportsLoopFilter
            }
        }).ToList();

        var stored = await installationService.UpdateAgentCapabilitiesAsync(installation, message.ReportedAtUtc,
            message.AgentType, message.AgentVersion, message.ControlProtocolVersion, capabilities, cancellationToken);
        if (stored)
            log.LogInformation("Stored capability report from {AgentId} ({AgentType} {AgentVersion}): {Capabilities}.",
                agentId, message.AgentType, message.AgentVersion, string.Join(", ", capabilities.Select(c => c.Name)));
        else
            log.LogInformation("Ignored capability report from {AgentId} made at {ReportedAt}: a newer report is stored.",
                agentId, message.ReportedAtUtc);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
