using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Statistics.Api.Endpoints;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class ProfilePostgreSqlFactAttribute : FactAttribute
{
    public ProfilePostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES")))
            Skip = "Set VELOTIME_TEST_STATISTICS_POSTGRES to run profile creation concurrency tests.";
    }
}

public sealed class StatsProfileConcurrencyTests : IAsyncLifetime
{
    private readonly string databaseName = $"statistics_profiles_{Guid.NewGuid():N}";
    private string adminConnectionString = "";
    private string connectionString = "";
    private StatisticsApiFactory? factory;

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        await ExecuteAsync(adminConnectionString, $"CREATE DATABASE \"{databaseName}\"");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        factory = new StatisticsApiFactory { PostgreSqlConnectionString = connectionString };
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        if (!string.IsNullOrEmpty(connectionString))
            await ExecuteAsync(adminConnectionString, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
    }

    [ProfilePostgreSqlFact]
    public Task ConcurrentEnsures_CreateExactlyOneProfile() => CompeteAsync(["/ensure", "/ensure", "/ensure"]);

    [ProfilePostgreSqlFact]
    public Task ConcurrentCreates_ReturnConflictsInsteadOfUniqueViolation() => CompeteAsync(["", "", ""]);

    [ProfilePostgreSqlFact]
    public Task MixedEnsureAndCreate_PreserveCommittedWinner() => CompeteAsync(["", "/ensure", "/ensure"]);

    private async Task CompeteAsync(string[] suffixes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var userId = Guid.NewGuid();
        using var client = factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        await using var blocker = new NpgsqlConnection(connectionString);
        await blocker.OpenAsync(timeout.Token);
        await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", blocker, transaction))
        {
            command.Parameters.AddWithValue("key", $"statistics-profile:{userId:D}");
            await command.ExecuteNonQueryAsync(timeout.Token);
        }

        var requests = suffixes.Select((suffix, index) => new CreateStatsProfileRequest(userId, $"Contender {index}", index % 2 == 0)).ToArray();
        var pending = suffixes.Select((suffix, index) =>
            client.PostAsJsonAsync($"/api/statistics/profiles{suffix}", requests[index], timeout.Token)).ToArray();
        await WaitForWaitersAsync(pending.Length, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        var responses = await Task.WhenAll(pending);
        try
        {
            var winnerIndex = Assert.Single(Enumerable.Range(0, responses.Length), index => responses[index].StatusCode == HttpStatusCode.Created);
            var winner = new StatsProfileResponse(userId, requests[winnerIndex].ProfileName, requests[winnerIndex].IsPublic);
            for (var index = 0; index < responses.Length; index++)
            {
                if (index == winnerIndex)
                    Assert.Equal($"/api/statistics/profiles/{userId}", responses[index].Headers.Location?.OriginalString);
                else
                    Assert.Equal(suffixes[index] == "" ? HttpStatusCode.Conflict : HttpStatusCode.OK, responses[index].StatusCode);
                if (responses[index].IsSuccessStatusCode)
                    Assert.Equal(winner, await responses[index].Content.ReadFromJsonAsync<StatsProfileResponse>(timeout.Token));
            }
            await using var scope = factory.Services.CreateAsyncScope();
            var profile = Assert.Single(await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>()
                .Set<Model.StatsProfile>().AsNoTracking().ToListAsync(timeout.Token));
            Assert.Equal(winner, new StatsProfileResponse(profile.UserId, profile.ProfileName, profile.IsPublic));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private async Task WaitForWaitersAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory'", connection);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) >= count) return;
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
