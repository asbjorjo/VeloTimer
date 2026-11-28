using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using VeloTime.Module.Common;

namespace VeloTime.Platform.Test;

[Collection("Migration process exit state")]
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
        using var worker = new MigrationWorker<MigrationTestDbContext>(serviceProvider, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        Assert.True(lifetime.StopRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationWorker_FailureOrCancellationSetsFailureExitCode(bool cancelled)
    {
        var services = new ServiceCollection();
        services.AddScoped<MigrationTestDbContext>(_ => throw (cancelled
            ? new OperationCanceledException()
            : new InvalidOperationException("Migration setup failed.")));
        await using var serviceProvider = services.BuildServiceProvider();
        var lifetime = new TestHostApplicationLifetime();
        using var worker = new MigrationWorker<MigrationTestDbContext>(serviceProvider, lifetime);
        var originalExitCode = Environment.ExitCode;

        try
        {
            await worker.StartAsync(CancellationToken.None);
            if (cancelled)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ExecuteTask!);
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);

            Assert.Equal(1, Environment.ExitCode);
            Assert.False(lifetime.StopRequested);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Fact]
    public async Task MigrationExecutable_UnavailableDatabaseExitsNonzero()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "VeloTime.Module.Profiles.Migration.dll"));
        // A nonexistent local socket makes failure deterministic without accessing any database.
        startInfo.Environment["ConnectionStrings__velotimedb"] =
            $"Host={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))};Port=5432;Database=unused;Username=unused;Timeout=1";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        startInfo.Environment.Remove("DOTNET_STARTUP_HOOKS");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var logs = await output + await error;
        Assert.Equal(1, process.ExitCode);
        Assert.Contains("MigrationWorker", logs);
        Assert.Contains("Npgsql", logs);
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

[CollectionDefinition("Migration process exit state", DisableParallelization = true)]
public sealed class MigrationProcessExitStateCollection;
