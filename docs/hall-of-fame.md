# Hall of Fame

Open **Hall of Fame** in public navigation or visit `/hall-of-fame`. Sign-in is
not required. Sections and optional columns appear in the administrator's saved
order. No enabled sections and a section without eligible results have separate
empty messages; loading failures offer **Retry**.

Rankings are all-time for each statistics item across all of its configurations
and facilities. Each currently public rider profile has one row, including
results from all of its linked transponders. Private profiles and unlinked results
are excluded. **Most completed results** counts completed statistics results,
not raw timing-point passings. The same item can appear in both ranking modes.

Equal ranking values share competition ranks (for example, 1, 1, 3). Ties do not
increase the configured row limit. Rank and name always appear first; optional
columns describe the selected fastest result or the completed-result count.
Best time, speed, facility and date all refer to the same best result, even in
count-ranked sections. Missing optional values display `-`, not zero.

Speeds are converted from the Hall of Fame API's m/s values to the viewer's
preferred units. Numbers follow browser locale. UTC result dates use the existing
display preferences for time zone and date/time culture, including their UTC
offset and zone label. Anonymous defaults remain km/h and browser time, with
date/time formatting following the UI language unless explicitly overridden.
The table can scroll horizontally on narrow screens; its scroll region is
keyboard-focusable.

## Statistics administrators

Open **Statistics Administration → Hall of Fame** or visit
`/admin/statistics/hall-of-fame`. The existing statistics-administration policy
protects the editor; API/BFF authorization remains authoritative.

1. **Add section** chooses the first unused item/mode combination. It starts
   enabled, with 10 rows and no optional columns. Empty settings never generate
   automatic public sections.
2. Choose the statistics item and ranking mode. Each item may have one section
   per mode, including disabled sections. Already-used combinations are
   unavailable in the selectors.
3. Set a row limit from 1 through 100. Enable/disable controls public visibility
   without removing the saved section.
4. Select optional columns. Newly selected columns are appended. Use the
   labelled **Move up / Move down** buttons to order columns and sections.
   These are ordinary keyboard-operable buttons, not drag-only controls.
   **Remove section** only changes the draft.
5. **Save** writes the entire ordered document atomically, including disabled
   sections. Removing all sections and saving clears the public page.

Unsaved edits are tracked for internal/external navigation and language/display
preference changes. Reload asks before discarding edits; a failed reload keeps
the draft. Validation and save failures also retain edits for correction/retry.
Inputs and internal navigation are blocked during a save to avoid racing it.

If another administrator saved first, HTTP 409 produces an explicit conflict
message and blocks a blind save retry. **Reload settings / retry**, confirm
discarding the draft, reapply edits to the latest settings, and save again.
There is deliberately no automatic merge or force overwrite. A network failure
can leave the save outcome uncertain; a retry uses the same observed version,
so a previously successful write will result in a conflict rather than a silent
overwrite.

## Verification and Tester handoff

Implementation checks (2026-11-03):

- `dotnet build src/WebUI.Mud.Client/VeloTime.WebUI.Mud.Client.csproj --configuration Release --no-restore`:
  passed. A compiling build reported three existing `BL0005` warnings in
  `TransponderManagement.razor`; the subsequent incremental build reported zero
  warnings/errors. The introduced obsolete form-validation warning was fixed.
- `dotnet build src/WebUI.Mud/VeloTime.WebUI.Mud.csproj --configuration Release --no-restore`:
  passed, zero errors, five warnings (the same three `BL0005` warnings and two
  existing `NU1903` WebAssembly.Server vulnerability advisories). OpenAPI
  generation logged an existing Facilities data-protection Redis-configuration
  error without failing the build; no generated tracked files changed.
  The final incremental BFF build passed with two `NU1903` warnings, zero errors.
- Resource XML parsing, duplicate-key detection and English/Norwegian parity:
  passed, 41 Hall of Fame keys per language. `git diff --check` passed.
- A temporary console harness under the client's ignored `obj/` directory,
  referencing the actual UI project, passed 41 assertions via
  `dotnet run --project src/WebUI.Mud.Client/obj/HallOfFameUiChecks/HallOfFameUiChecks.csproj --configuration Release --no-restore`.
  This checked SI conversion to all three speed units, localized counts,
  duration/date and nullable presentation, isolated draft snapshots, public
  static HTML order/escaping/table semantics/empty/error states, admin static
  controls/defaults/both modes/reordering, whole-document saves including
  disabled sections, validation, conflict blocking, failed-reload retention,
  successful reload/save and tracker disposal. Temporary source was removed;
  this is smoke evidence, not a committed test suite or browser interaction
  validation. Static lifecycle checks bypassed MudForm interactivity.

No browser tools were available. Live keyboard focus retention, narrow-screen
layout, dialogs, numeric-input conversion/validation and server/WASM interactions
still need verification. Tester owns persistent tests and the previously reported
platform fake-service compile blocker; no test fakes were changed by UI work.

Prioritize these scenarios:

- Anonymous and authorized/unauthorized navigation; English and Norwegian
  headings/messages; no settings, all disabled, empty results, 500/503 and retry.
- Server and WASM renders: saved section/column order, both modes for one item,
  rank/name fixed, duplicate profile names, ranks 1/1/3 and exact row cutoff.
- Browser locale versus UI language, explicit date/time preferences and zones,
  m/s/km/h/mph, null cells, coherent best-result values, and preference changes
  without refetching results.
- Add/remove/disable/re-enable/reorder with keyboard; row limits 1, 10, 100,
  zero/101, blank and nonnumeric input; no available items; rank/name-only tables.
- Whole-document save/reload; dirty navigation, external navigation, language
  switch and display-preference changes; failed reload/save retaining drafts;
  overlapping saves, two-tab 409, reload cancellation, reapply/retry and
  component disposal during outstanding requests.

See [API contract](../api/hall-of-fame-contract.md) for authoritative aggregation,
tie-breaking, payload and concurrency semantics.
