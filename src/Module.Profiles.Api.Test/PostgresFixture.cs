using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Api.Test;

// Missing configuration is an explicit skip, never a successful persistence check.
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.ConfigurationKey)))
            Skip = $"Requires {PostgresFixture.ConfigurationKey}; see this project's README.md.";
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.ConfigurationKey)))
            Skip = $"Requires {PostgresFixture.ConfigurationKey}; see this project's README.md.";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ConfigurationKey = "PROFILES_TEST_POSTGRES_ADMIN_CONNECTION";
    private readonly string database = "profiles_contract_" + Guid.NewGuid().ToString("N");
    private string? adminConnection;
    private bool created;
    public bool ApplyMigrations { get; init; } = true;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConfigurationKey);
        if (string.IsNullOrWhiteSpace(configured)) return;
        var admin = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        // Explicit control-plane connection only. Never migrate, clear, or delete an existing application DB.
        if (admin.Database != "postgres")
            throw new InvalidOperationException($"{ConfigurationKey} must explicitly target Database=postgres.");
        adminConnection = admin.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\" TEMPLATE template0", connection))
            await command.ExecuteNonQueryAsync();
        created = true;
        ConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
        try
        {
            await using var context = CreateContext();
            if (ApplyMigrations) await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public ProfilesDbContext CreateContext() => new(new DbContextOptionsBuilder<ProfilesDbContext>()
        .UseNpgsql(ConnectionString, options => options.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().Options);

    public async Task DisposeAsync()
    {
        if (!created) return;
        // Name is generated internally, never taken from configuration or a caller.
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
        created = false;
    }
}
