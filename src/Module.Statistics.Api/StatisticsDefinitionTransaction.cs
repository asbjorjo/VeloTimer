using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api;

internal static class StatisticsDefinitionTransaction
{
    // Retry the complete read/lock/validate/write/enqueue operation, never just SaveChanges or publish.
    internal static async Task<T> RunAsync<T>(StatisticsDbContext storage, Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
                return await operation();
            }
            catch (Exception exception) when (attempt < 3 && IsConcurrencyFailure(exception))
            {
                storage.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private static bool IsConcurrencyFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }
}
