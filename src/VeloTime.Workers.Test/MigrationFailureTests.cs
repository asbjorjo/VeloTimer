using System.Data.Common;
using System.Net.Sockets;
using VeloTime.Users.Migration.Services;
using Keycloak = VeloTime.Users.Migration.Keycloak;
using Statistics = VeloTime.Module.Statistics.Client;
using Timing = VeloTime.Module.Timing.Client;

namespace VeloTime.Workers.Test;

public sealed class MigrationFailureTests
{
    [Theory]
    [InlineData("http", true)]
    [InlineData("socket", true)]
    [InlineData("timeout", true)]
    [InlineData("polly-timeout", true)]
    [InlineData("polly-circuit-breaker", true)]
    [InlineData("polly-rate-limiter", true)]
    [InlineData("task-cancelled", true)]
    [InlineData("cancelled", true)]
    [InlineData("database", true)]
    [InlineData("wrapped-database", true)]
    [InlineData("invalid-operation", false)]
    [InlineData("argument", false)]
    [InlineData("format", false)]
    [InlineData("unexpected", false)]
    [InlineData("wrapped-http", false)]
    public void ExceptionPolicy_OnlyListedServiceFailuresAreFatal(string kind, bool fatal)
    {
        Exception exception = kind switch
        {
            "http" => new HttpRequestException(),
            "socket" => new SocketException(),
            "timeout" => new TimeoutException(),
            "polly-timeout" => new Polly.Timeout.TimeoutRejectedException(),
            "polly-circuit-breaker" => new Polly.CircuitBreaker.BrokenCircuitException(),
            "polly-rate-limiter" => new Polly.RateLimiting.RateLimiterRejectedException(),
            "task-cancelled" => new TaskCanceledException(),
            "cancelled" => new OperationCanceledException(),
            "database" => new TestDbException(),
            "wrapped-database" => new InvalidOperationException("database", new TestDbException()),
            "invalid-operation" => new InvalidOperationException(),
            "argument" => new ArgumentException(),
            "format" => new FormatException(),
            "wrapped-http" => new InvalidOperationException("http", new HttpRequestException()),
            _ => new Exception()
        };
        AssertPolicy(exception, fatal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(404, false)]
    [InlineData(409, false)]
    [InlineData(422, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(599, true)]
    public void ApiStatusPolicy_IsConsistentAcrossKeycloakStatisticsAndTiming(int status, bool fatal)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();
        AssertPolicy(new Keycloak.ApiException("failure", status, "response", headers, null!), fatal);
        AssertPolicy(new Statistics.ApiException("failure", status, "response", headers, null!), fatal);
        AssertPolicy(new Timing.ApiException("failure", status, "response", headers, null!), fatal);
    }

    private static void AssertPolicy(Exception exception, bool fatal)
    {
        Assert.Equal(fatal, MigrationFailure.IsFatal(exception));
        Assert.Equal(fatal, MigrationFailure.IsFatalOrStopping(exception, CancellationToken.None));
        Assert.Equal(fatal, MigrationFailure.IsFatalOrStopping(exception, new CancellationToken(true)));
    }

    private sealed class TestDbException : DbException;
}
