using VeloTime.Module.Statistics.Client;

namespace VeloTime.Users.Migration.Services;

internal sealed class StatsProfileMigrationWriter(IStatisticsClient statistics, ILogger logger)
{
    private readonly List<string> issues = [];
    private int matched;

    internal IReadOnlyList<string> Issues => issues;

    internal void MissingOwner(long riderId, string reason = "the legacy rider has no identity account") =>
        Report($"Rider {riderId}: cannot ensure a statistics profile because {reason}.");

    internal async Task EnsureAsync(long riderId, Guid userId, string profileName, bool isPublic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var profile = await statistics.EnsureStatsProfileAsync(new CreateStatsProfileRequest
            {
                UserId = userId, ProfileName = profileName, IsPublic = isPublic
            }, cancellationToken);
            if (profile is null || profile.UserId != userId || string.IsNullOrWhiteSpace(profile.ProfileName))
                throw new InvalidOperationException($"Statistics ensure returned an invalid profile for owner {userId}.");
            if (!string.Equals(profile.ProfileName, profileName, StringComparison.Ordinal) || profile.IsPublic != isPublic)
                Report($"Rider {riderId}, owner {userId}: statistics profile differs. Requested name '{profileName}', public {isPublic}; saved name '{profile.ProfileName}', public {profile.IsPublic}. Existing settings were preserved.");
            else matched++;
        }
        catch (Exception exception) when (!MigrationFailure.IsFatalOrStopping(exception, cancellationToken))
        {
            var detail = exception is ApiException apiException
                ? $"HTTP {apiException.StatusCode}: {apiException.Response}"
                : exception.Message;
            Report($"Rider {riderId}, owner {userId}: statistics profile could not be ensured: {detail}");
        }
    }

    internal void LogSummary() =>
        logger.LogInformation("Statistics profile migration finished: {MatchedCount} matching profiles, {UnresolvedCount} unresolved issues.",
            matched, issues.Count);

    private void Report(string issue)
    {
        issues.Add(issue);
        logger.LogError("Unresolved statistics profile migration: {Issue}", issue);
    }
}
