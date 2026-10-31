# Display preferences

Signed-in users can choose **Profile → Units and time**. Distance (m/ft), speed
(km/h/mph/m/s), and time display are independent. Save persists one complete
snapshot to the account, without changing its language setting. Browser time
means the current device’s IANA zone, detected after interactivity; it is not a
saved fixed offset. Anonymous viewers use metres, km/h, and browser time.

Named zones are searchable by IANA ID and catalogue label. Failed saves retain
the draft and keep the previous settings effective. Retry or discard explicitly.
Other unfinished editors keep a settings change pending until those edits are
finished/discarded. Pending settings are not applied or persisted. The layout
shows loading/error/retry rather than rendering server-local timestamps while
browser identity or signed-in preferences cannot be resolved.

Displayed timestamps include the zone and the applicable UTC offset. Ownership
editors retain precise original instants when untouched. A nonexistent local
time is rejected; repeated times require an explicit offset with its UTC instant.
Registration dates are inclusive in the selected zone; the end is the start of
the next calendar day, exclusive. DST days are not assumed to be 24 hours.
The same rules apply to midnight gaps/repeats and ownership column date filters.
Filters show their captured zone/boundaries and preserve the selected UTC interval
across later preference changes. Sorting and filtering remain server-side.

Distance editors preserve metres internally, enforce the original 0.001 m
minimum in the selected unit, and retain the exact SI value on untouched saves.
Numeric entry uses the browser number culture. Public speed bounds are entered
in the preferred unit but shared URLs remain invariant km/h. Untouched bounds
reuse their exact canonical values. Preference changes do not navigate, reset
paging, or change results. Durations/rolling observation windows are unchanged.
Samples, public results, and best-results API speeds are **km/h**, not m/s.

## Implemented integration behavior

`WebUI.Mud` registers scoped `DisplayPreferenceService` and
`DisplayPreferenceState` for server-interactive execution. The adapter uses the
existing delegated Profiles typed client, maps its generated DTOs to the shared
`Module.Profiles.Interface.Data` contracts, and passes cancellation and failures
through. `WebUI.Mud.Client` registers the same scoped state with the BFF-backed
`DisplayPreferenceHttpService` for WebAssembly.

The preference BFF routes are in the cookie-authenticated `/api` group, return
`Cache-Control: no-store`, and validate antiforgery on mutations:

| Method and route | Semantics |
| --- | --- |
| GET `/api/profile/preferences/display` | Return complete `DisplayPreferenceDTO`; an account without saved settings receives m/km/h/browser/null without an insert. |
| PUT `/api/profile/preferences/display` | Accept complete `UpdateDisplayPreferenceRequest`; return the successfully saved `DisplayPreferenceDTO` as 200 JSON. Browser/UTC mode clears the zone ID; named mode requires a supported IANA ID. |
| GET `/api/profile/preferences/time-zones` | Return the supported server catalogue as `TimeZoneOptionDTO` values (`id`, `displayName`). |

The BFF proxies the additive Profiles API routes
`GET/PUT /api/profiles/me/preferences/display` and
`GET /api/profiles/me/preferences/time-zones`. Upstream validation and authorization
errors (including 400/422 and 401/403), conflicts, and availability failures remain
failures; a successful default is not substituted after an error. Backend
authorization remains authoritative.

Both `GET /api/transponders` and `GET /api/admin/timing/transponders` accept these
optional paired range parameters (the admin route also requires the Timing
administration policy):

- `lastSeenUtcStart`, `lastSeenUtcEnd` → `Filters.LastSeenUtcRange`
- `ownedFromUtcStart`, `ownedFromUtcEnd` → `Filters.OwnedFromUtcRange`
- `ownedToUtcStart`, `ownedToUtcEnd` → `Filters.OwnedToUtcRange`

The browser transport emits ISO 8601 round-trip UTC timestamps with seven
fractional-second digits. Start is inclusive and end exclusive. Both bounds are
required; offsets must be UTC (zero), start must precede end, and a range cannot
be combined with its legacy DateOnly filter. The BFF passes validated ranges
through the typed Timing client to SQL filtering before count and paging. The
legacy date-only query fields remain supported for existing callers.

`registrationDates.js` exports only `browserPresentation()`, returning
`{ timeZone, locale }`; calendar conversion and descriptions use the shared .NET
presenter. `RegistrationDates.test.mjs` has been updated to cover that export and
browser identity; calendar/DST presenter coverage is in
`DisplayPresentationTests`. This describes unit-test coverage, not browser E2E
verification.

## Verification scenarios for Tester

- Anonymous defaults and signed-in preferences in interactive server and WASM;
  loading, 401/403, offline/retry, saved success, retained failed draft/discard,
  missing/empty zone catalogue and searchable Europe/Oslo.
- m/ft and all three speeds; 36 km/h must display as 10 m/s. Decimal comma input,
  finite/nonnegative speed validation, SI threshold, and unchanged editor saves.
- Canonical speed URLs, repeated apply without drift, duration/cursor precision,
  preference-only changes without duplicate requests or paging changes.
- Oslo spring gap/autumn repeat (both offsets); original repeated-hour instant
  saved untouched with fractional ticks; changing wall input resets offset.
- Registration and date filters over 23/25-hour days, inclusive end/next-day
  exclusive boundary, midnight transitions (e.g. skipped Pacific/Apia day),
  explicit repeated-midnight choices and stable captured filter ranges.
- Existing status/ownership authorization, mobile table overflow, responsive form,
  keyboard zone search/offset choice, localized errors and cancellation/disposal.

No browser tools were available for visual or live interaction validation.
