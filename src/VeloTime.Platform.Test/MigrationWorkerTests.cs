using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Common;

namespace VeloTime.Platform.Test;

public sealed class MigrationWorkerTests
{
    [Fact]
    public async Task MigrationWorker_CompletesMigrationAndRequestsHostShutdown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestDbContext>(options => options.UseSqlite(connection));
        await using var serviceProvider = services.BuildServiceProvider();
        var lifetime = new TestHostApplicationLifetime();
        var worker = new MigrationWorker<MigrationTestDbContext>(serviceProvider, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        Assert.True(lifetime.StopRequested);
    }

    private sealed class MigrationTestDbContext(DbContextOptions<MigrationTestDbContext> options) : DbContext(options);

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => stopped.Token;
        public bool StopRequested { get; private set; }

        public void StopApplication()
        {
            StopRequested = true;
            stopping.Cancel();
            stopped.Cancel();
        }
    }
}
