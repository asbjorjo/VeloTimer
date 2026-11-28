using Microsoft.Extensions.DependencyInjection;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Service;

namespace VeloTime.Agent.Capabilities;

/// <summary>Advertises <see cref="AgentCapabilities.PauseResume"/> when the agent registered an <see cref="IControlService"/>.</summary>
internal sealed class PauseResumeCapabilityProvider(IServiceProviderIsService services) : IAgentCapabilityProvider
{
    public ValueTask<IReadOnlyList<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AgentCapability>>(
            services.IsService(typeof(IControlService)) ? [new AgentCapability(AgentCapabilities.PauseResume)] : []);

    public event EventHandler? CapabilityChanged { add { } remove { } }
}
