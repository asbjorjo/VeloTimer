using Npgsql;
using YamlDotNet.RepresentationModel;

namespace VeloTime.Platform.Test;

public sealed class CiWorkflowTests
{
    private const string FacilitiesConnectionKey = "FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION";

    [Fact]
    public void Definition_RunsMasterAndV2WithFacilitiesPostgresEnabled()
    {
        using var reader = File.OpenText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ci.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var triggers = Mapping(root, "on");
        foreach (var trigger in new[] { "push", "pull_request" })
            Assert.Equal(new[] { "master", "v2" }, Sequence(Mapping(triggers, trigger), "branches"));

        var pullRequest = Mapping(triggers, "pull_request");
        Assert.Equal(new[] { "opened", "synchronize", "reopened", "ready_for_review" }, Sequence(pullRequest, "types"));
        Assert.Equal(new[] { "src/**", "VeloTimer.sln", ".github/workflows/**" }, Sequence(pullRequest, "paths"));

        var build = Mapping(Mapping(root, "jobs"), "build");
        Assert.Equal("github.event_name == 'push' || (github.event_name == 'pull_request' && github.event.pull_request.draft == false)",
            Scalar(build, "if"));
        var environment = Mapping(build, "env");
        Assert.Equal("https://build.invalid", Scalar(environment, "OIDC_AUDIENCE"));
        var postgres = Mapping(Mapping(build, "services"), "postgres");
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

        foreach (var key in new[] { "VELOTIME_TEST_OUTBOX_POSTGRES", "VELOTIME_TEST_STATISTICS_POSTGRES" })
            Assert.True(new NpgsqlConnectionStringBuilder(Scalar(environment, key)).EquivalentTo(facilities),
                $"{key} and Facilities must target the same disposable PostgreSQL service.");

        var steps = Assert.IsType<YamlSequenceNode>(build.Children[new YamlScalarNode("steps")]);
        var test = Assert.Single(steps.Children.OfType<YamlMappingNode>(),
            step => step.Children.TryGetValue(new YamlScalarNode("name"), out var name) && name.ToString() == "Test");
        Assert.Equal("dotnet test --logger \"trx\" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov", Scalar(test, "run"));
    }

    [Fact]
    public void GitHubActions_RequiresFacilitiesEnvironmentSoGatedTestsCannotSilentlySkip()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            return; // Local runs retain the fixtures' explicit PostgreSQL opt-in.

        var connection = Environment.GetEnvironmentVariable(FacilitiesConnectionKey);
        Assert.False(string.IsNullOrWhiteSpace(connection), $"CI must set {FacilitiesConnectionKey}; otherwise Facilities PostgreSQL tests skip.");
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
