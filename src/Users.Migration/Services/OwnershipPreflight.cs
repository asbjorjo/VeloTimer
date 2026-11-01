using System.Globalization;
using VeloTime.Module.Timing.Interface;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.Users.Migration.Services;

internal sealed record SourceOwnership(long RiderId, long RowId, TransponderType.TimingSystem System,
    string SystemId, DateTime OwnedFrom, DateTime? OwnedUntil);

internal sealed record MigrationOwnership(long RiderId, string Code, DateTimeOffset OwnedFrom,
    DateTimeOffset? OwnedTo, IReadOnlyList<long> SourceIds);

internal sealed record OwnershipPreflightResult(IReadOnlyList<MigrationOwnership> Registrations,
    IReadOnlyList<string> Issues, int SourceCount, int NormalizedCount);

internal static class OwnershipPreflight
{
    internal static OwnershipPreflightResult Create(IEnumerable<SourceOwnership> source)
    {
        var rows = source.ToList();
        var issues = new List<string>();
        var normalized = new List<MigrationOwnership>();
        foreach (var group in rows.GroupBy(row => (row.RiderId, row.System, SystemId: CanonicalId(row.SystemId)))
            .OrderBy(group => group.Key.RiderId).ThenBy(group => group.Key.SystemId, StringComparer.Ordinal))
        {
            try
            {
                if (group.Key.System != TransponderType.TimingSystem.Mylaps_X2)
                    throw new ArgumentException("Only MyLaps X2 transponders are supported.");
                if (!long.TryParse(group.Key.SystemId, NumberStyles.None, CultureInfo.InvariantCulture, out var systemId))
                    throw new ArgumentException("The system ID must be numeric.");
                var code = MylapsX2Code.IdToCode(systemId);
                if (MylapsX2Code.CodeToId(code) != systemId)
                    throw new ArgumentException("The system ID is outside the X2 code range.");
                foreach (var row in group)
                {
                    if (row.OwnedFrom == default || row.OwnedFrom.Kind != DateTimeKind.Utc ||
                        row.OwnedUntil.HasValue && (row.OwnedUntil.Value.Kind != DateTimeKind.Utc ||
                            row.OwnedUntil <= row.OwnedFrom || row.OwnedUntil.Value.Ticks > DateTime.MaxValue.Ticks - 10))
                        throw new ArgumentException($"Source row {row.RowId} has an invalid UTC ownership period.");
                }

                DateTime start = default;
                DateTime? end = null;
                var ids = new List<long>();
                foreach (var row in group.OrderBy(row => row.OwnedFrom).ThenBy(row => row.RowId))
                {
                    if (ids.Count > 0 && end.HasValue && row.OwnedFrom - end.Value >= TimeSpan.FromMinutes(15))
                    {
                        normalized.Add(Period());
                        ids = [];
                    }
                    if (ids.Count == 0)
                    {
                        start = row.OwnedFrom;
                        end = row.OwnedUntil;
                    }
                    else if (!row.OwnedUntil.HasValue || end.HasValue && row.OwnedUntil > end)
                        end = row.OwnedUntil;
                    ids.Add(row.RowId);
                }
                normalized.Add(Period());

                MigrationOwnership Period() => new(group.Key.RiderId, code, new(start),
                    // Legacy ends are inclusive; PostgreSQL timestamps have microsecond precision.
                    end.HasValue ? new DateTimeOffset(end.Value.AddTicks(10)) : null, ids.ToArray());
            }
            catch (ArgumentException exception)
            {
                issues.Add($"Rider {group.Key.RiderId}, transponder {group.Key.SystemId}, source rows [{string.Join(", ", group.Select(row => row.RowId))}]: {exception.Message}");
            }
        }

        var blocked = new HashSet<(long RiderId, string Code)>();
        foreach (var transponder in normalized.GroupBy(period => period.Code))
        {
            var active = new List<MigrationOwnership>();
            foreach (var period in transponder.OrderBy(period => period.OwnedFrom).ThenBy(period => period.RiderId))
            {
                active.RemoveAll(previous => previous.OwnedTo.HasValue && previous.OwnedTo <= period.OwnedFrom);
                foreach (var previous in active.Where(previous => previous.RiderId != period.RiderId))
                {
                    blocked.Add((previous.RiderId, previous.Code));
                    blocked.Add((period.RiderId, period.Code));
                    issues.Add($"Transponder {period.Code}: conflicting riders {previous.RiderId} (source rows [{string.Join(", ", previous.SourceIds)}], {previous.OwnedFrom:O} to {previous.OwnedTo:O}) and {period.RiderId} (source rows [{string.Join(", ", period.SourceIds)}], {period.OwnedFrom:O} to {period.OwnedTo:O}). Both rider/transponder groups are withheld.");
                }
                active.Add(period);
            }
        }
        return new(normalized.Where(period => !blocked.Contains((period.RiderId, period.Code))).ToList(),
            issues, rows.Count, normalized.Count);
    }

    private static string CanonicalId(string systemId) =>
        long.TryParse(systemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id.ToString(CultureInfo.InvariantCulture) : systemId;
}
