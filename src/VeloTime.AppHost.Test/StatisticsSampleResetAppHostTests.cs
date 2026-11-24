using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace VeloTime.AppHost.Test;

public sealed class StatisticsSampleResetAppHostTests
{
    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(true, "true", false)]
    [InlineData(true, "Approved", false)]
    [InlineData(false, "approved", false)]
    [InlineData(true, "approved", true)]
    public async Task ApprovalOnlyChangesRunModeMigrationConnection(bool runMode, string? approval, bool expected)
    {
        var builder = DistributedApplication.CreateBuilder();
        const string connection = "Host=localhost;Database=synthetic;Username=fixture";
        builder.Configuration["ConnectionStrings:velotimedb"] = connection;
        var database = builder.AddConnectionString("velotimedb");
        var migrator = builder.AddExecutable("statistics-migration", "unused", ".")
            .WithReference(database);
        var api = builder.AddExecutable("statistics-api", "unused", ".")
            .WithReference(database);
        var processor = builder.AddExecutable("statistics-processor", "unused", ".")
            .WithReference(database);

        StatisticsSampleReset.Configure(migrator, database, runMode, approval);

        using var app = builder.Build();
        async Task<Dictionary<string, string>> EnvironmentOf(IResource resource) =>
            (await ExecutionConfigurationBuilder.Create(resource)
                .WithEnvironmentVariablesConfig().BuildAsync(builder.ExecutionContext)).EnvironmentVariables
                .ToDictionary(item => item.Key, item => item.Value);
        var migrationEnvironment = await EnvironmentOf(migrator.Resource);
        var apiEnvironment = await EnvironmentOf(api.Resource);
        var processorEnvironment = await EnvironmentOf(processor.Resource);
        Assert.Equal(connection + (expected ? ";Options=-c velotime.statistics_sample_reset=approved" : ""),
            migrationEnvironment["ConnectionStrings__velotimedb"]);
        var parsed = new NpgsqlConnectionStringBuilder(migrationEnvironment["ConnectionStrings__velotimedb"]);
        Assert.Equal(expected ? "-c velotime.statistics_sample_reset=approved" : null, parsed.Options);
        Assert.Equal(connection, apiEnvironment["ConnectionStrings__velotimedb"]);
        Assert.Equal(connection, processorEnvironment["ConnectionStrings__velotimedb"]);
    }
}
