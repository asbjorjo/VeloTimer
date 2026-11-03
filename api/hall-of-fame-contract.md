# Hall of Fame API contract

This document defines the additive Hall of Fame contract. Existing statistics
contracts and endpoints are unchanged.

## Public results

`GET /api/statistics/hall-of-fame` is anonymous and returns a `HallOfFameDTO`.
Its `Sections` contain only enabled settings, in persisted section order. Each
section identifies its statistics item and ranking mode and includes the
configured row limit, optional columns in display order, and at most that many
entries. Empty settings produce an empty section list; a configured section with
no eligible profiles has an empty `Entries` list.
The response is always `Cache-Control: no-store`; public visibility is checked
against the current profile row on each request. When at least one returned
section requests `FacilityOfBestResult`, the API resolves the required facility
names through one Facilities service-client request; it makes no facility
request otherwise. A stale enabled item reference or malformed persisted
settings returns an explicit HTTP 500 problem rather than silently dropping a
section. A failed facility lookup returns an explicit 502 or 503 problem.

Entries always contain `Rank` and `ProfileName`. The other values are nullable
and are returned only when their matching optional column is enabled:

| Column | Response field | Meaning |
| --- | --- | --- |
| BestTime | `BestTime` | Duration of the rider's selected best result |
| SpeedOfBestResult | `SpeedOfBestResult` | Speed of that same best result, in m/s |
| PassingCount | `PassingCount` | Count of completed `StatisticsEntry` results for this item |
| FacilityOfBestResult | `FacilityOfBestResult` | Name of the facility for that same best result |
| DateOfBestResult | `DateOfBestResult` | UTC completion time of that same best result |

Best-result columns always refer to one result: the shortest duration; ties are
resolved by earliest UTC completion timestamp, then facility UUID ascending,
then entry UUID ascending. The UUID tie-breakers are internal and never appear
in public DTOs. `BestTime` is a `TimeSpan`, speed is a finite base-SI metres per
second value, and the date is a UTC `DateTimeOffset`. Legacy statistics
contracts that use km/h are not changed.

## Ranking and eligibility

Each section ranks all-time results for one item across all of its configurations
and facilities. Eligible entries are grouped by their linked public profile, not
by transponder; unlinked entries and profiles that are not currently public are
excluded. Passing count counts completed `StatisticsEntry` results across all
linked transponders for that profile and item, not raw timing-point passings.

`BestTime` ranks by the profile's shortest duration ascending. `MostPassings`
ranks by completed-result count descending. Equal primary values receive
competition ranks (`1, 1, 3`). Duration comparison uses the exact stored
duration precision; counts are integers. Rank is computed over all eligible
profiles before the row limit is applied, so a displayed rank can exceed the
number of displayed rows.

The cutoff is fixed at the configured number of rows and never expands for a
tie. To choose those rows deterministically, sort first by the ranking value,
then by profile name in ascending UTF-8 bytewise order, then by the internal
profile UUID in ascending PostgreSQL UUID order. The profile UUID is not exposed.
Thus profiles with the same name remain distinct and consistently ordered.

## Administrator settings

`GET /api/admin/statistics/hall-of-fame` and
`PUT /api/admin/statistics/hall-of-fame` are statistics-administrator-only.
The GET response is a `HallOfFameSettingsDTO`; PUT accepts a complete
`SaveHallOfFameSettingsRequest` and returns the saved `HallOfFameSettingsDTO`.
The ordered `Sections` list is the section order. Disabled sections are retained
in settings but omitted from public results. Within each section, `Columns`
preserves optional-column display order. An empty section list means no public
sections; there is no implicit default section.

`Version` is a monotonically increasing concurrency token. A new settings
document has version `0`; the caller sends its observed value as
`ExpectedVersion`. A successful replacement increments the version exactly
once. A stale version is rejected with HTTP 409 Conflict and must not overwrite
newer settings. Structural validation failures return HTTP 400
ValidationProblem details keyed by request JSON properties. Unknown item
references are also rejected by the API. Unauthorized writes are rejected by
the API's existing statistics-administrator authorization policy. Settings
reads and writes are `Cache-Control: no-store`. Item deletion returns HTTP 409
while any settings section references the item, including disabled sections;
deletion and settings replacement serialize on the singleton settings row so
concurrent writes cannot create a dangling reference.

The shared `HallOfFameQuery.Validate` contract checks nonnegative expected
version, required section and column collections (empty lists are valid),
nonempty item IDs, defined enum values, unique `(StatisticsItemId, RankingMode)`
pairs, unique columns within a section, and row counts from 1 through 100.
`HallOfFameQuery.DefaultRowCount` is 10 and is the initial row count for a newly
configured section. The API validates item existence separately. There is
intentionally no arbitrary maximum number of configured sections.

## DTO and compatibility summary

- `HallOfFameDTO` has ordered public sections; each section has item ID/name,
  ranking mode, row count, ordered columns, and ordered entries.
- `HallOfFameEntryDTO` exposes only rank, public profile name, and selected
  optional values. It includes no profile/user or transponder identifier.
- `HallOfFameSettingsDTO` and `SaveHallOfFameSettingsRequest` replace the
  settings document atomically using an explicit version.
- `HallOfFameSectionSettingsDTO` persists item, mode, enabled state, row count,
  and ordered optional columns. The same item can have one section in each mode.
- All new durations use `TimeSpan`; timestamps are UTC; speed is m/s and counts
  are 64-bit integers. Presentation formatting and display-unit conversion
  remain UI responsibilities.
