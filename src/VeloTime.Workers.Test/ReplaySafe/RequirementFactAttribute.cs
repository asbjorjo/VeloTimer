namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// A requirement from docs/replay-safe-statistics-plan.md, exercised end to end on PostgreSQL.
/// Requires VELOTIME_TEST_STATISTICS_POSTGRES (an admin connection string; each world creates
/// and drops its own databases).
///
/// <para><see cref="PendingPhase"/> names the plan phase expected to make the test pass. Until
/// then the test is skipped so CI stays green. The phase that fixes it removes the property.
/// Set VELOTIME_RUN_PENDING_REQUIREMENTS=1 to run pending tests anyway and see what still fails.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequirementFactAttribute : FactAttribute
{
    public const string PostgresVariable = "VELOTIME_TEST_STATISTICS_POSTGRES";
    public const string RunPendingVariable = "VELOTIME_RUN_PENDING_REQUIREMENTS";

    private string? pendingPhase;

    public RequirementFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresVariable)))
            Skip = $"Set {PostgresVariable} to run PostgreSQL requirement tests.";
    }

    /// <summary>The plan phase (and problem IDs from plan §2) expected to make this pass.</summary>
    public string? PendingPhase
    {
        get => pendingPhase;
        set
        {
            pendingPhase = value;
            if (Skip is null && value is not null && Environment.GetEnvironmentVariable(RunPendingVariable) != "1")
                Skip = $"Pending {value} of the replay-safe statistics plan. Set {RunPendingVariable}=1 to run.";
        }
    }
}
