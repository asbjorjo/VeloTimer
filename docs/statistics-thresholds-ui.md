# Statistics approval-limit editor

The statistics administration item and configuration editors expose optional,
inclusive approval limits. This implements the UI portion of
[the validity-threshold plan](statistics-validity-thresholds-plan.md).

## Input and save semantics

- The editor starts in duration mode. Its alternate speed mode uses the captured
  `EffectiveDisplaySettings` number culture and preferred km/h, mph or m/s.
  Duration fields reuse the results editor's `[days.]hh:mm:ss` shape, extended
  with an optional browser-local decimal separator and 1–7 fractional digits.
  The optional day separator remains a literal dot.
- Empty global bounds are unbounded. Empty configuration bounds inherit the
  selected item's global bounds. Requested, global/inherited and effective
  duration/speed previews are separate; global limits always intersect local
  limits. Broader requested local bounds remain saveable with a clamping warning.
- Minimum speed sets **maximum duration**; maximum speed sets **minimum duration**,
  using the selected item's distance in metres. Only nullable durations enter the
  existing shared read/save DTOs. No API signatures or generated clients change.
- Untouched saves and mode switches retain exact canonical duration ticks.
  Edited inputs round to PostgreSQL microseconds (10 ticks), with midpoint
  rounding away from zero. Values rounding to zero or exceeding the supported
  duration are rejected. Positive finite speeds and positive durations are
  required; malformed/nonfinite drafts remain visible and cannot be saved or
  discarded by switching modes.
- Changing the selected item or editing distance retains duration bounds and
  updates speed equivalents and effective previews. Multi uses its **own** item's
  distance and globals, while its facility remains represented by the existing
  parent configuration label. Every contributing lap must also be valid.
- Save rejects reversed or disjoint effective ranges. Global item edits check
  each loaded configuration's effective range and identify a conflicting
  configuration. The backend must independently enforce all validation,
  authorization and recorded-result structural edit protections, including
  changes concurrent with the loaded UI snapshot.
- Clearing either field explicitly saves null. Cancel/discard and failed-save
  behavior remain in the existing editor; thresholds participate in the same
  unsaved-edit lifetime and preference-change deferral. A successful save says
  settings were saved and historical revalidation runs in the background, not
  that old results have already been reclassified. There is no rejected-results
  inspection screen.

The presentation helper and component live beside `StatisticsDefinitions.razor`
as `StatisticsThresholdDraft.cs` and `StatisticsThresholdEditor.razor`. They issue
no requests. English and Norwegian Bokmål resources cover labels and notices.

## Checks performed

From `/workspaces/VeloTimer-statistics-thresholds-ui`:

```sh
dotnet build src/WebUI.Mud.Client/VeloTime.WebUI.Mud.Client.csproj --configuration Release --no-restore --nologo
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DisplayPresentationTests|FullyQualifiedName~LocalizationContractTests|FullyQualifiedName~StatisticsTableRenderingTests|FullyQualifiedName~PublicStatisticsTransportTests' --logger 'trx' --nologo
```

- Client build: passed, 0 errors; three existing BL0005 warnings in
  `TransponderManagement.razor`.
- Existing related UI checks: **82 passed, 0 failed, 0 skipped**. Initial test
  build required restoring Statistics, Profiles and Timing API project assets;
  after restore the checks passed. Dependency vulnerability/obsolete-use warnings
  were not changed in this UI work.
- A temporary local console harness at `/tmp/statistics-thresholds-ui-checks`
  passed **200 assertions**, run with
  `dotnet run --project /tmp/statistics-thresholds-ui-checks/Checks.csproj --configuration Release --nologo`.
  It references the built client, not a copy of the helper. Coverage includes
  en-US/nb-NO and all three speed units, exact inversion, untouched sub-microsecond
  ticks and repeated mode switches, edited microsecond rounding, invalid/overflow
  inputs, clearing/null inheritance, clamping/disjoint/equal boundaries,
  item-distance changes, exact browser-service item/Multi request serialization
  and legacy omitted-field reads. Static `HtmlRenderer` assertions cover inherited
  and effective values, clamping warnings, disjoint errors, mph labels, malformed
  inputs, responsive grid classes and the live preview region.
  This is a local check, not a committed regression suite; Tester owns durable
  broader coverage.
- `git diff --check`: passed. No browser tools were available. Static rendering
  does **not** establish visual, keyboard or live interaction correctness.

## API and Tester handoff

API owner must integrate the shared optional read/save fields through server
adapters/mappings and validate authoritative hard-limit intersection and
background scheduling. The browser administration service already serializes
these shared DTOs unchanged; full BFF/upstream persistence is not validated by
the isolated UI checks.

Tester should add durable draft/transport/editor regressions and verify:

1. New/edit/save/reload and clearing bounds for items, Simple and Multi; exact
   untouched durations in both modes, including repeated switches and cancelled
   or failed saves.
2. One-sided/unset globals and locals, inclusive equal bounds, broader requested
   locals retaining their raw values with a warning, and disjoint config/global
   edits blocked both in the UI and authoritatively by the API.
3. Switching selected configuration items with different distances/globals,
   editing item distance while displaying speed, and Multi with a different item
   from its Simple parent. Parent-derived facility labels must remain correct.
4. Browser decimal comma and all speed preferences; malformed, nonfinite,
   zero/negative, overflow and sub-microsecond entries; malformed drafts must not
   disappear on a mode toggle.
5. Save failures (400/422, conflict, unauthorized/forbidden, unavailable), retained
   drafts/retry/cancel, unsaved navigation and preference-change deferral.
6. Mobile field wrapping, visible warning/error states, field labels, keyboard
   toggles/clearing and screen-reader announcement of effective previews.
7. The saved notice is eventual/pending only; no claim historical classifications
   already changed. Verify actual historical processing separately after API and
   processor integration.
