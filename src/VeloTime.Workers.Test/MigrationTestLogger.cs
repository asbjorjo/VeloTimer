using Microsoft.Extensions.Logging;

namespace VeloTime.Workers.Test;

internal sealed class MigrationTestLogger : ILogger
{
    internal List<string> ReportedIssues { get; } = [];
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning && state is IEnumerable<KeyValuePair<string, object?>> values)
            foreach (var value in values.Where(value => value.Key == "Issue"))
                ReportedIssues.Add(Assert.IsType<string>(value.Value));
    }
}
