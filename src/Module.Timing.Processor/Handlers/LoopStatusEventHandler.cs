using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Handlers;

/// <summary>
/// Stores each loop status sample (deduplicated on timing point and decoder time) with its 5-minute rollup, then
/// advances the installation's LastSeen.
/// </summary>
public class LoopStatusEventHandler(LoopStatusService loopStatusService, InstallationService installationService)
    : IConsumer<LoopStatusEvent>, IConsumerWithContext
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

        await loopStatusService.RecordAsync(new LoopStatusObservation(
            AgentId: AgentId,
            LoopId: message.LoopId,
            Time: message.Time,
            Noise: message.Noise,
            LoopTriggers: message.LoopTriggers,
            DecoderTemperature: message.DecoderTemperature,
            InputVoltage: message.InputVoltage,
            TimeSynchronised: message.TimeSynchronised,
            SatellitesInUse: message.SatellitesInUse,
            DecoderId: message.DecoderId,
            NoiseSharesStrengthScale: message.NoiseSharesStrengthScale), cancellationToken);

        var installation = await installationService.GetInstallationForAgent(AgentId, cancellationToken);
        if (installation is not null)
        {
            await installationService.UpdateLastSeenAsync(installation, message.Time, cancellationToken);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
