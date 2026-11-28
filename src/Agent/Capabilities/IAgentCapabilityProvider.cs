using VeloTime.Agent.Interface.Messages.Events;

namespace VeloTime.Agent.Capabilities;

/// <summary>
/// Contributes entries to the agent's <see cref="AgentCapabilitiesV1"/> report. Register implementations as
/// singletons; <see cref="CapabilityReporter"/> asks every provider for each report and sends a new report whenever one
/// raises <see cref="CapabilityChanged"/>. Advertise only what the agent can actually do right now: the platform
/// offers admins exactly what is advertised.
/// </summary>
public interface IAgentCapabilityProvider
{
    ValueTask<IReadOnlyList<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken);

    event EventHandler? CapabilityChanged;
}
