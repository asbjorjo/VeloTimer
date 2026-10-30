using VeloTime.Module.Timing.Client;

namespace VeloTime.Users.Migration.Services;

internal sealed class OwnershipMigrationWriter(ITimingClient timing, ILogger logger, OwnershipPreflightResult preflight)
{
    private readonly List<string> issues = preflight.Issues.ToList();
    private int saved;

    internal void LogPreflight()
    {
        logger.LogInformation("Ownership preflight: {SourceCount} source rows, {NormalizedCount} normalized periods, {EligibleCount} eligible registrations.",
            preflight.SourceCount, preflight.NormalizedCount, preflight.Registrations.Count);
        foreach (var issue in preflight.Issues) logger.LogError("Ownership preflight: {Issue}", issue);
        foreach (var period in preflight.Registrations)
            logger.LogInformation("Ownership registration planned for rider {RiderId}, transponder {Code}, {Start:O} to {End:O}, source rows {SourceIds}.",
                period.RiderId, period.Code, period.OwnedFrom, period.OwnedTo, string.Join(", ", period.SourceIds));
    }

    internal void MissingOwner(long riderId)
    {
        foreach (var period in preflight.Registrations.Where(period => period.RiderId == riderId))
            Report(period, "The legacy rider has no identity account.");
    }

    internal async Task RegisterRiderAsync(long riderId, Guid ownerId, CancellationToken cancellationToken)
    {
        foreach (var period in preflight.Registrations.Where(period => period.RiderId == riderId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await timing.RegisterTransponderOwnershipAsync(new RegisterTransponderRequest
                {
                    Code = period.Code,
                    OwnerId = ownerId,
                    OwnedFrom = period.OwnedFrom,
                    OwnedTo = period.OwnedTo
                }, cancellationToken);
                if (result.RequiresConfirmation)
                {
                    var existing = string.Join("; ", result.ExistingOwnerships.Select(owner =>
                        $"{owner.Id}: {owner.OwnedFrom:O} to {owner.OwnedTo:O}"));
                    Report(period, $"Owner {ownerId}: shortening requires review; existing coverage [{existing}]. No confirmation was submitted.");
                }
                else saved++;
            }
            catch (ApiException exception) when (exception.StatusCode is 400 or 404 or 409 or 422)
            {
                Report(period, $"Owner {ownerId}: registration rejected with HTTP {exception.StatusCode}: {exception.Response}");
            }
        }
    }

    internal void Complete() => Complete([]);

    internal void Complete(IEnumerable<string> profileIssues)
    {
        logger.LogInformation("Ownership migration finished: {SourceCount} source rows, {NormalizedCount} normalized periods, {SavedCount} successful registrations, {UnresolvedCount} unresolved issues.",
            preflight.SourceCount, preflight.NormalizedCount, saved, issues.Count);
        var unresolved = issues.Concat(profileIssues).ToList();
        if (unresolved.Count > 0)
            throw new AggregateException("Migration has unresolved profile or ownership registrations. Successful registrations remain saved and can be retried.",
                unresolved.Select(issue => new InvalidOperationException(issue)));
    }

    private void Report(MigrationOwnership period, string reason)
    {
        var issue = $"Rider {period.RiderId}, transponder {period.Code}, source rows [{string.Join(", ", period.SourceIds)}], {period.OwnedFrom:O} to {period.OwnedTo:O}: {reason}";
        issues.Add(issue);
        logger.LogError("Unresolved ownership registration: {Issue}", issue);
    }
}
