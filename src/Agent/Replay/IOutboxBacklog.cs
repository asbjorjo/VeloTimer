using Microsoft.EntityFrameworkCore;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent.Replay;

/// <summary>How many outbox messages are waiting to be delivered.</summary>
public interface IOutboxBacklog
{
    Task<int> PendingAsync(CancellationToken cancellationToken);
}

internal sealed class PostgreSqlOutboxBacklog(IDbContextFactory<AgentDbContext> contexts) : IOutboxBacklog
{
    public async Task<int> PendingAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM agent.smb_outbox WHERE NOT delivery_complete AND NOT delivery_aborted")
            .SingleAsync(cancellationToken);
    }
}
