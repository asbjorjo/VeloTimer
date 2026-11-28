using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VeloTime.Agent.Capabilities;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent.Replay;

/// <summary>
/// Advertises <c>AgentStore</c> passing replays over <c>agent.passing</c>, from the oldest stored passing, up to
/// <see cref="ReplayOptions.AgentStoreMaxRange"/> per request, with a loop filter.
/// </summary>
public sealed class AgentStoreReplayCapabilityProvider(IDbContextFactory<AgentDbContext> contexts, IOptions<ReplayOptions> options)
    : IAgentCapabilityProvider
{
    public async ValueTask<IReadOnlyList<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        options.Value.AgentStoreEnabled ? [new AgentCapability(AgentCapabilities.PassingReplay, await DescribeAsync(cancellationToken))] : [];

    public async Task<PassingReplayCapability> DescribeAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var oldest = await context.Set<Model.Passing>().MinAsync(p => (DateTime?)p.Time, cancellationToken);
        return new PassingReplayCapability(PassingReplayModes.AgentStore,
            oldest is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null,
            options.Value.AgentStoreMaxRange, SupportsLoopFilter: true);
    }

    public event EventHandler? CapabilityChanged { add { } remove { } }
}
