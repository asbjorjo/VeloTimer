# Statistics entry contribution API contracts

These administrative endpoints expose entry-window sample contributions from Statistics
and time-window passings from Timing. Each service queries only its own database.
Results are window matches, not persisted provenance; later requests may include
late-arriving rows. Timestamps use UTC offsets and quantities use base SI units.

## Statistics entry samples

`GET /api/statistics/admin/entries/{id:guid}/samples`

Operation ID: `GetAdminStatisticsEntrySamples`. Requires the existing global or
Statistics administration policy. An unknown entry returns 404. This administrative
endpoint includes entries regardless of privacy or linkage status.

The entry supplies the authoritative transponder and inclusive time bounds. A sample
matches when its transponder equals the entry transponder, `Sample.TimeStart >=
entry.TimeStart`, and `Sample.TimeEnd <= entry.TimeEnd`. Results sort by `TimeStart`,
`TimeEnd`, then UUID `Id`. At most 501 matches are fetched; at most 500 are returned,
with `Truncated` true only if another match exists. No caller-supplied identity/window
or facility filter applies.

Contracts in `VeloTime.Module.Statistics.Interface.Data`:

```csharp
AdminStatisticsEntrySamplesDTO(
    Guid EntryId, Guid TransponderId,
    DateTimeOffset TimeStart, DateTimeOffset TimeEnd,
    IReadOnlyList<AdminStatisticsSampleDTO> Samples, bool Truncated)

AdminStatisticsSampleDTO(
    Guid Id, DateTimeOffset TimeStart, DateTimeOffset TimeEnd,
    Guid CoursePointStartId, Guid CoursePointEndId,
    double DistanceMetres, TimeSpan Duration, double? SpeedMetresPerSecond,
    Guid FacilityId)
```

Speed is null for NaN or infinity. A returned sample with nonfinite distance produces a
logged 409 ProblemDetails response; it is not replaced with a fabricated value or
serialized as a nonstandard JSON number. Duration is the stored `TimeSpan`, serialized
as a constant-format string. Course-point and facility IDs are identifiers, not names.
Successful, 404, and 409 handler responses set `Cache-Control: no-store`.

## Timing passings

`GET /api/timing/passings?transponderId={uuid}&from={utc}&to={utc}`

Operation ID: `GetTransponderPassings`. Requires the existing global, Facilities,
Timing, or Statistics administration policy. Transponder ownership alone does not grant
access.

All three query parameters are mandatory, single-valued, and case-sensitive.
`transponderId` is a nonempty D-format UUID. Bounds use
`yyyy-MM-ddTHH:mm:ss[.1-to-7-fractional-digits]Z` or explicit `+00:00`; encode `+` as
`%2B` in raw query strings. Date-only, offsetless, nonzero or unknown (`-00:00`)
offsets, empty fractions, malformed or missing values, duplicates, and unsupported
parameters return 400 ValidationProblem. Bounds require `from <= to` and a window no
longer than seven days; equal bounds and exactly seven days are valid. Precision is
validated before querying. Since PostgreSQL parameters have microsecond precision, the
lower query bound is rounded inward to exclude passings before the requested `from`; the
response echoes the original UTC bounds.

Passings match the exact transponder and inclusive `Passing.Time` bounds. Results sort
by `Time`, then UUID `Id`; at most 501 are fetched and 500 returned, with accurate
`Truncated`. Timing-point and installation descriptions are projected from current
records in SQL. An unknown transponder or no matches returns a genuine empty list.
Successful and validation handler responses set `Cache-Control: no-store`.

Contracts in `VeloTime.Module.Timing.Interface.Data`:

```csharp
TransponderPassingsDTO(
    Guid TransponderId, DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<PassingDTO> Passings, bool Truncated)

PassingDTO(
    Guid Id, DateTimeOffset Time,
    Guid TimingPointId, string TimingPointDescription,
    Guid InstallationId, string InstallationDescription,
    Guid FacilityId, bool LowBattery, bool LowStrength, bool LowHits)
```

Passing flags are recorded values; descriptions reflect current Timing point and
installation descriptions. No interpretation or unit conversion is applied.

## Generated clients

The generated client interfaces expose:

```csharp
// VeloTime.Module.Statistics.Client.IStatisticsClient
Task<AdminStatisticsEntrySamplesDTO> GetAdminStatisticsEntrySamplesAsync(
    Guid id, CancellationToken cancellationToken);

// VeloTime.Module.Timing.Client.ITimingClient
Task<TransponderPassingsDTO> GetTransponderPassingsAsync(
    Guid transponderId, string from, string to, CancellationToken cancellationToken);
```

Each method also has an overload without a cancellation token. Generated DTOs are in
their respective Client namespaces, distinct from Interface records. The generated
Duration is a string and must be parsed with invariant `TimeSpan` constant format;
response timestamps are `DateTimeOffset`.

Timing bounds remain required strings in OpenAPI so NSwag preserves UTC offsets and
fractional precision. Consumers should format authoritative UTC bounds with
`ToString("O", CultureInfo.InvariantCulture)`; generated HTTP clients encode query
parameters as needed. Do not convert timestamps to browser-local time.
