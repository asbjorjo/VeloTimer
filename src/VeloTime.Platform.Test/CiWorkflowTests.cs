using Npgsql;
using YamlDotNet.RepresentationModel;

namespace VeloTime.Platform.Test;

public sealed class CiWorkflowTests
{
    private const string FacilitiesConnectionKey = "FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION";
    private static readonly string[] NotificationConnectionKeys =
        ["NOTIFICATIONS_API_TEST_ADMIN_CONNECTION", "NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION"];

    private const string RequirePostgresKey = "VELOTIME_TEST_REQUIRE_POSTGRES";
    private const string TestCommand = "dotnet test VeloTimer.sln --logger \"trx\" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov";

    [Fact]
    public void Definition_RunsUnitTestsForV2AndPostgresIntegrationOnlyForMaster()
    {
        using var reader = File.OpenText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ci.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var triggers = Mapping(root, "on");
        foreach (var trigger in new[] { "push", "pull_request" })
            Assert.Equal(new[] { "master", "v2" }, Sequence(Mapping(triggers, trigger), "branches"));
        Assert.True(triggers.Children.ContainsKey(new YamlScalarNode("workflow_dispatch")));

        var pullRequest = Mapping(triggers, "pull_request");
        Assert.Equal(new[] { "opened", "synchronize", "reopened", "ready_for_review" }, Sequence(pullRequest, "types"));
        Assert.Equal(new[] { "src/**", "VeloTimer.sln", ".github/workflows/**" }, Sequence(pullRequest, "paths"));

        var jobs = Mapping(root, "jobs");
        Assert.Equal(new[] { "unit", "integration" }, jobs.Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToArray());

        // Unit job: v2 pushes and non-draft PRs into anything but master, with no database service or connections.
        var unit = Mapping(jobs, "unit");
        Assert.Equal("(github.event_name == 'push' && github.ref != 'refs/heads/master') || (github.event_name == 'pull_request' && github.event.pull_request.draft == false && github.base_ref != 'master')",
            Scalar(unit, "if"));
        Assert.False(unit.Children.ContainsKey(new YamlScalarNode("services")), "The unit job must not start PostgreSQL.");
        var unitEnvironment = Mapping(unit, "env");
        Assert.Equal("https://build.invalid", Scalar(unitEnvironment, "OIDC_AUDIENCE"));
        Assert.Equal(new[] { "OIDC_AUDIENCE" }, unitEnvironment.Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToArray());
        AssertTestSteps(unit);

        // Integration job: master pushes, non-draft PRs into master and manual runs, against the PostgreSQL 17 service.
        var integration = Mapping(jobs, "integration");
        Assert.Equal("github.event_name == 'workflow_dispatch' || (github.event_name == 'push' && github.ref == 'refs/heads/master') || (github.event_name == 'pull_request' && github.event.pull_request.draft == false && github.base_ref == 'master')",
            Scalar(integration, "if"));
        var environment = Mapping(integration, "env");
        Assert.Equal("https://build.invalid", Scalar(environment, "OIDC_AUDIENCE"));
        Assert.Equal("true", Scalar(environment, RequirePostgresKey));
        Assert.Equal("true", Scalar(environment, ReleasePublishTheoryAttribute.ConfigurationKey));
        var postgres = Mapping(Mapping(integration, "services"), "postgres");
        Assert.Equal("postgres:17-alpine", Scalar(postgres, "image"));

        var facilities = new NpgsqlConnectionStringBuilder(Scalar(environment, FacilitiesConnectionKey));
        Assert.Equal("localhost", facilities.Host);
        Assert.Equal("postgres", facilities.Database); // Fixtures create/drop their own databases, never a supplied application DB.
        var serviceEnvironment = Mapping(postgres, "env");
        Assert.Equal(serviceEnvironment.Children.TryGetValue(new YamlScalarNode("POSTGRES_USER"), out var user)
            ? Assert.IsType<YamlScalarNode>(user).Value : "postgres", facilities.Username);
        Assert.False(string.IsNullOrWhiteSpace(facilities.Password));
        Assert.True(string.Equals(Scalar(serviceEnvironment, "POSTGRES_PASSWORD"), facilities.Password, StringComparison.Ordinal),
            "Facilities must use the ephemeral PostgreSQL service password.");
        Assert.Contains($"{facilities.Port}:5432", Sequence(postgres, "ports"));
        Assert.Contains("pg_isready -U postgres", Scalar(postgres, "options"));

        foreach (var key in new[] { "VELOTIME_TEST_OUTBOX_POSTGRES", "VELOTIME_TEST_STATISTICS_POSTGRES" }
                     .Concat(NotificationConnectionKeys))
            Assert.True(new NpgsqlConnectionStringBuilder(Scalar(environment, key)).EquivalentTo(facilities),
                $"{key} and Facilities must target the same disposable PostgreSQL service.");
        AssertTestSteps(integration);

        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "summarize-test-results.py"));
        Assert.Contains("| Project | Passed | Failed | Skipped | Other | Total |", script);
        Assert.Contains("### Failed tests", script);
        Assert.Contains("UnitTestResult", script);
    }

    private static void AssertTestSteps(YamlMappingNode job)
    {
        var steps = Assert.IsType<YamlSequenceNode>(job.Children[new YamlScalarNode("steps")]);
        var test = Step(steps, "Test");
        Assert.Equal(TestCommand, Scalar(test, "run"));

        var summary = Step(steps, "Summarize Test Results");
        Assert.Equal("always()", Scalar(summary, "if"));
        Assert.Equal("python3 .github/scripts/summarize-test-results.py", Scalar(summary, "run"));
        var upload = Step(steps, "Upload Test Results");
        Assert.True(steps.Children.IndexOf(test) < steps.Children.IndexOf(summary));
        Assert.True(steps.Children.IndexOf(summary) < steps.Children.IndexOf(upload));
    }

    private static YamlMappingNode Step(YamlSequenceNode steps, string name) =>
        Assert.Single(steps.Children.OfType<YamlMappingNode>(),
            step => step.Children.TryGetValue(new YamlScalarNode("name"), out var value) && value.ToString() == name);

    [Fact]
    public void IntegrationCi_RequiresFacilitiesEnvironmentSoGatedTestsCannotSilentlySkip()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RequirePostgresKey), "true", StringComparison.OrdinalIgnoreCase))
            return; // Local runs and the unit CI job retain the fixtures' explicit PostgreSQL opt-in.

        var connection = Environment.GetEnvironmentVariable(FacilitiesConnectionKey);
        Assert.False(string.IsNullOrWhiteSpace(connection), $"CI must set {FacilitiesConnectionKey}; otherwise Facilities PostgreSQL tests skip.");
        Assert.Equal("postgres", new NpgsqlConnectionStringBuilder(connection).Database);
    }

    [Theory]
    [InlineData("NOTIFICATIONS_API_TEST_ADMIN_CONNECTION")]
    [InlineData("NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION")]
    public void IntegrationCi_RequiresNotificationEnvironmentSoGatedTestsCannotSilentlySkip(string key)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RequirePostgresKey), "true", StringComparison.OrdinalIgnoreCase))
            return; // Local runs and the unit CI job retain the fixtures' explicit PostgreSQL opt-in.

        var connection = Environment.GetEnvironmentVariable(key);
        Assert.False(string.IsNullOrWhiteSpace(connection), $"CI must set {key}; otherwise Notifications PostgreSQL tests skip.");
        Assert.Equal("postgres", new NpgsqlConnectionStringBuilder(connection).Database);
    }

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlMappingNode>(parent.Children[new YamlScalarNode(key)]);

    private static string Scalar(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlScalarNode>(parent.Children[new YamlScalarNode(key)]).Value!;

    private static string[] Sequence(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlSequenceNode>(parent.Children[new YamlScalarNode(key)]).Children
            .Select(node => Assert.IsType<YamlScalarNode>(node).Value!).ToArray();
}
