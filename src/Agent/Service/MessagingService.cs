using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using System.Data.Common;
using System.Diagnostics;
using VeloTime.Agent.Logging;
using VeloTime.Agent.Model;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent.Service;

public class MessagingService(IMessageBus messageBus, AgentDbContext dbContext, Metrics metrics, ILogger<MessagingService> log)
{
    public Task SendEventAsync(InstallationConfig config) => SendEventAsync(config, CancellationToken.None);

    public Task SendEventAsync(InstallationConfig config, CancellationToken cancellationToken) =>
        EnqueueAsync([config], m => m.ToMessage(), "Installation Config", cancellationToken);

    public Task SendEventsAsync(IEnumerable<LoopConfig> config) => SendEventsAsync(config, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<LoopConfig> config, CancellationToken cancellationToken) =>
        EnqueueAsync(config, m => m.ToMessage(), "Loop Config", cancellationToken);

    public Task SendEventAsync(LoopStatus status) => SendEventAsync(status, CancellationToken.None);

    public Task SendEventAsync(LoopStatus status, CancellationToken cancellationToken) =>
        EnqueueAsync([status], m => m.ToMessage(), "Loop Status", cancellationToken);

    public Task SendEventsAsync(IEnumerable<Passing> passing) => SendEventsAsync(passing, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<Passing> passing, CancellationToken cancellationToken) =>
        EnqueueAsync(passing, m => m.ToMessage(), "Passings", cancellationToken, PreparePassingsAsync, processedPassing: true);

    public Task SendEventsAsync(IEnumerable<SegmentConfig> config) => SendEventsAsync(config, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<SegmentConfig> config, CancellationToken cancellationToken) =>
        EnqueueAsync(config, m => m.ToMessage(), "Segment Config", cancellationToken);

    public Task SendEventsAsync(IEnumerable<SystemConfig> config) => SendEventsAsync(config, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<SystemConfig> config, CancellationToken cancellationToken) =>
        EnqueueAsync(config, m => m.ToMessage(), "System Config", cancellationToken);

    // Keeps each multi-row insert well below provider parameter limits (8 parameters per passing).
    private const int PassingInsertChunkSize = 1000;

    private async Task<Passing[]> PreparePassingsAsync(Passing[] batch, CancellationToken cancellationToken)
    {
        var unique = batch.DistinctBy(p => p.Id).ToArray();
        // Insert in a stable key order so concurrent overlapping batches acquire row locks consistently.
        var ordered = unique.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        var inserted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in ordered.Chunk(PassingInsertChunkSize))
            inserted.UnionWith(await InsertPassingsIfAbsentAsync(chunk, cancellationToken));
        return unique.Where(p => inserted.Contains(p.Id)).ToArray();
    }

    /// <summary>
    /// Inserts passings in the current transaction, skipping IDs that already exist or are being inserted by a
    /// concurrent transaction (which blocks until that transaction commits or rolls back). Returns inserted IDs.
    /// </summary>
    private async Task<List<string>> InsertPassingsIfAbsentAsync(Passing[] chunk, CancellationToken cancellationToken)
    {
        var entity = dbContext.Model.FindEntityType(typeof(Passing))
            ?? throw new InvalidOperationException("Passing is not mapped in AgentDbContext.");
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("Passing is not mapped to a table.");
        var schema = entity.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        var timeMapping = entity.FindProperty(nameof(Passing.Time))!.GetRelationalTypeMapping();
        string[] columns =
        [
            Column(nameof(Passing.Id)), Column(nameof(Passing.Time)), Column(nameof(Passing.TransponderType)),
            Column(nameof(Passing.TransponderId)), Column(nameof(Passing.LoopId)), Column(nameof(Passing.LowBattery)),
            Column(nameof(Passing.LowStrength)), Column(nameof(Passing.LowHits))
        ];

        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Passings must be inserted inside the enqueue transaction.");
        if (dbContext.Database.GetCommandTimeout() is { } timeout) command.CommandTimeout = timeout;

        var rows = new List<string>(chunk.Length);
        var index = 0;
        foreach (var passing in chunk)
        {
            object[] values =
            [
                passing.Id, passing.Time, passing.TransponderType, passing.TransponderId, passing.LoopId,
                passing.LowBattery, passing.LowStrength, passing.LowHits
            ];
            var names = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var parameterName = $"@p{index++}";
                var parameter = values[i] is DateTime time
                    ? timeMapping.CreateParameter(command, parameterName, time)
                    : command.CreateParameter();
                parameter.ParameterName = parameterName;
                parameter.Value = values[i];
                command.Parameters.Add(parameter);
                names[i] = parameter.ParameterName;
            }
            rows.Add($"({string.Join(", ", names)})");
        }
        var id = columns[0];
        command.CommandText =
            $"INSERT INTO {sql.DelimitIdentifier(tableName, schema)} ({string.Join(", ", columns)}) " +
            $"VALUES {string.Join(", ", rows)} ON CONFLICT ({id}) DO NOTHING RETURNING {id}";

        var inserted = new List<string>(chunk.Length);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) inserted.Add(reader.GetString(0));
        }
        catch (DbException exception)
        {
            // Keep the store-failure contract callers saw when passings were saved through EF.
            throw new DbUpdateException("An error occurred while saving passings.", exception);
        }
        return inserted;
    }

    private async Task EnqueueAsync<TModel, TMessage>(IEnumerable<TModel> input, Func<TModel, TMessage> map,
        string operation, CancellationToken cancellationToken,
        Func<TModel[], CancellationToken, Task<TModel[]>>? prepare = null, bool processedPassing = false)
        where TMessage : class
    {
        using var activity = Instrumentation.Source.StartActivity($"Send {operation}");
        IDbContextTransaction? transaction = null;
        var count = 0;
        var accepted = 0;
        try
        {
            var batch = input.ToArray();
            count = batch.Length;
            activity?.SetTag("count", count);
            if (count == 0) return;
            log.LogSendingEvents(operation, count);
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            if (prepare != null) batch = await prepare(batch, cancellationToken);
            foreach (var item in batch)
            {
                // Collection publishes bypass SlimMessageBus 3.4 outbox interceptors.
                await messageBus.Publish(map(item), cancellationToken: cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            accepted = batch.Length;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            log.LogError(exception, "Failed to durably enqueue {Count} events for {Operation}.", count, operation);
            try
            {
                if (transaction != null) await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                log.LogError(rollbackException, "Rollback failed for {Operation}.", operation);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
        activity?.SetStatus(ActivityStatusCode.Ok);
        if (processedPassing) metrics.ProcessedPassing(accepted, activity);
    }
}
