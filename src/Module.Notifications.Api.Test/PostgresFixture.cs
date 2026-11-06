using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.DataProtection;
using VeloTime.Module.Notifications.Storage;
using Xunit.Sdk;

namespace VeloTime.Module.Notifications.Api.Test;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.ConfigurationKey)))
            Skip = $"Requires {PostgresFixture.ConfigurationKey}; see src/Module.Notifications.Api/README.md.";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ConfigurationKey = "NOTIFICATIONS_API_TEST_ADMIN_CONNECTION";
    private readonly string databaseName = "notifications_api_test_" + Guid.NewGuid().ToString("N");
    private string? adminConnectionString;
    private bool created;

    public string ConnectionString { get; private set; } = null!;
    public IDataProtectionProvider DataProtection { get; } = new EphemeralDataProtectionProvider();

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConfigurationKey);
        if (string.IsNullOrWhiteSpace(configured)) return;

        var admin = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        if (!string.Equals(admin.Database, "postgres", StringComparison.Ordinal))
            throw new InvalidOperationException($"{ConfigurationKey} must explicitly target Database=postgres.");
        adminConnectionString = admin.ConnectionString;

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\" TEMPLATE template0", connection);
            await command.ExecuteNonQueryAsync();
        }

        created = true;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public NotificationsDbContext CreateContext() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseNpgsql(ConnectionString, options => options.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention()
        .Options);

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE notifications.notification CASCADE;
            TRUNCATE notifications.account_dismissal, notifications.session_dismissal,
                notifications.refresh_outbox, notifications.scheduled_work CASCADE;
            UPDATE notifications.snapshot_revision SET revision = 0 WHERE id = 1;
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
        created = false;
    }
}
