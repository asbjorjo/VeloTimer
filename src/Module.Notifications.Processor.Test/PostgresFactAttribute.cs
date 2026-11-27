using Xunit;

namespace VeloTime.Module.Notifications.Processor.Test;

/// <summary>
/// Skips a dispatcher test up front when no PostgreSQL admin connection is configured.
/// xUnit v2 reports a runtime <c>SkipException</c> as a failure, so the skip must be static.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConfigurationKey = "NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConfigurationKey)))
            Skip = $"Set {ConfigurationKey} to an isolated PostgreSQL admin connection.";
    }
}

/// <inheritdoc cref="PostgresFactAttribute"/>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.ConfigurationKey)))
            Skip = $"Set {PostgresFactAttribute.ConfigurationKey} to an isolated PostgreSQL admin connection.";
    }
}
