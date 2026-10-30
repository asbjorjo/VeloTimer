# Multilingual frontend and Profiles service

## Handoff status

Planning is complete; implementation has not started. This branch contains only the plan, saved for continuation on another computer. The implementation base is `v2`; verify its latest state before starting implementation on a dedicated feature branch.

To resume, ask the assistant to read this plan, check the current codebase for changes, reconstruct its task tracking, and present the implementation plan for approval before changing application code.

## Problem and approach

VeloTimer currently has hard-coded English and Norwegian frontend text, no shared localization setup, and a fixed English document language. Its Blazor frontend uses Interactive Auto, so culture must remain consistent across server prerendering, server interactivity, and WebAssembly.

Add complete English and Norwegian Bokmal localization using .NET resources, plus a separate Profiles microservice that owns signed-in users' language preferences. Do not store preferences in Keycloak or conflate these settings with Statistics display-name profiles.

## Confirmed requirements

- English is the default and fallback language. Norwegian translations use `nb-NO`.
- Recognize browser `nb-NO` and `nn-NO` preferences as `nb-NO`; also handle their neutral language tags and Norwegian `no` consistently. Do not offer a separate Nynorsk translation.
- Translate all existing frontend screens, navigation, validation, accessible text, titles, dialogs, notifications, empty/loading states, and app-owned user-facing errors, including built-in MudBlazor text.
- Do not translate user-entered names/content, identifiers, protocol values, logs, or diagnostics intended as technical data.
- Offer language switching and remember anonymous selections in the browser.
- Signed-in selections persist in a new independent Profiles service with its own PostgreSQL database, API, and typed client.
- Initial profile storage contains only language preference; use typed fields so future settings can be added without introducing a generic settings framework.
- Preference precedence on later visits: saved signed-in profile, anonymous browser preference, supported browser language, then English. An explicit successful switch wins.
- Apply language changes by reloading the current page; preserve unsaved-change protections.
- Report save failures explicitly and never claim that a failed profile write succeeded.
- Pass the selected language to Keycloak through supported OIDC parameters; translating/customizing Keycloak-hosted pages is out of scope. Never write the preference to Keycloak.

## Current implementation anchors

- Host setup and middleware: [Program.cs](../../src/WebUI.Mud/Program.cs).
- Client startup: [Program.cs](../../src/WebUI.Mud.Client/Program.cs).
- Interactive Auto and document language: [App.razor](../../src/WebUI.Mud/Components/App.razor).
- Shared shell: [MainLayout.razor](../../src/WebUI.Mud.Client/Layout/MainLayout.razor) and [NavMenu.razor](../../src/WebUI.Mud.Client/Layout/NavMenu.razor).
- Existing `IProfileService`, `ProfileService`, and `ProfileHttpService` implement Keycloak account operations; retain those behaviors and introduce a distinct preferences contract rather than repurpose account-linking APIs.
- Cookie-authenticated BFF routes and antiforgery protection: [BffEndpoints.cs](../../src/WebUI.Mud/Endpoints/BffEndpoints.cs).
- Domain authentication, storage, and migration patterns: [Module.Common](../../src/Module.Common/), [Module.Statistics.Api](../../src/Module.Statistics.Api/), and [Module.Statistics.Migration](../../src/Module.Statistics.Migration/).
- Orchestration/deployment: [AppHost](../../src/AppHost/), [application chart](../../charts/velotime/), [migration chart](../../charts/velotime-migration/), and [publish workflow](../../.github/workflows/publish.yml).
- Existing frontend rendering and BFF regression tests: [VeloTime.Platform.Test](../../src/VeloTime.Platform.Test/).

## Implementation todos

### 1. Define and validate the preferences contract

- Begin implementation on a dedicated branch from the verified appropriate base; preserve unrelated work.
- Define `Module.Profiles.Interface` DTOs and authenticated current-user API semantics, preferably `GET /profiles/me/preferences` and `PUT /profiles/me/preferences/language`.
- Derive identity from validated authentication claims, not a caller-supplied user ID. Include issuer/subject identity scoping where needed to prevent cross-issuer collisions.
- Return an explicit unset language for users without a preference; do not create a row or persist browser detection on reads.
- Accept only canonical supported saved values, `en` and `nb-NO`; browser-language aliases are normalized at language resolution, not silently accepted as arbitrary stored values.
- Specify missing/invalid identity, invalid culture, dependency failure, and update semantics. Persist only the language field and use a uniqueness constraint/atomic upsert so concurrent first saves cannot create duplicate profiles.
- Define shared supported-culture/resource conventions for the frontend and stable error identifiers for frontend-owned failures.
- Validate the contract before dependent implementations.

### 2. Implement the independent Profiles service

- Add core, API, interface, client, and migration projects using existing `VeloTime.Module.{Domain}` conventions and centrally managed dependency versions.
- Add the preferences entity, EF Core context, PostgreSQL migration, and migration worker; use a dedicated `profilesdb` connection/database rather than existing service-owned storage.
- Implement authenticated self-only reads and language updates with typed results and explicit validation. No administration, profile directory, messaging processor, bootstrap data, or display-name migration is needed.
- Add an HTTP client using the established discovery/authentication patterns and preserve cancellation and meaningful status codes.
- Add focused API/domain/storage tests, including isolation between users, absent settings, rejected values, persistence, and concurrent initial writes.

### 3. Wire orchestration and production deployment

- Register new projects in the solution and AppHost. Add the separate PostgreSQL database, API discovery/reference from WebUI, authentication/audience configuration, and migration-before-API ordering.
- Add Profiles API and migration Helm subcharts and update dependencies/lock files through existing Helm tooling.
- Wire WebUI discovery configuration, dedicated database secret/connection requirements, readiness, and migration deployment ordering using current chart conventions.
- Ensure container publishing/versioning includes the new deployable projects; change workflows only where project-wide discovery does not already cover them.
- Verify Helm rendering and that unrelated services retain existing references and behavior.

### 4. Implement end-to-end culture resolution and persistence

- Register resource localization on both hosts and configure the supported/default cultures.
- Implement a request-scoped resolver after authentication, with access to the Profiles typed client through the existing user-token infrastructure.
- Use a separate frontend preferences abstraction/BFF surface; preserve existing Keycloak account-service contracts and linked-account functionality.
- Provide antiforgery-protected language changes for anonymous and authenticated users; validate culture and local return paths. Authenticated writes must complete successfully before updating the applied preference.
- Distinguish anonymous browser preferences from signed-in applied-culture state so one user's saved setting cannot override another user's profile on a shared browser.
- Resolve missing preferences through the agreed fallback chain without implicitly creating a profile. Log dependency failures and show a localized retryable notice rather than treating an unavailable profile service as an unset preference.
- Transfer the resolved culture into WebAssembly before startup; ensure supported globalization data and satellite resource assemblies are included. Keep API query/serialization formats invariant.
- Render the resolved HTML `lang`; use supported OIDC `ui_locales` forwarding without any Keycloak profile updates.
- Ensure culture changes/sign-in/sign-out do not leak per-user preferences through shared caches or singleton culture state.

### 5. Translate the complete frontend and add the selector

- Add a small shared resource marker with English fallback `.resx` resources and complete `nb-NO` resources in the client assembly, available to both rendering hosts.
- Add an accessible language selector to the shared layout and a profile-page preference control using the same save/apply path. Show language names in their native forms.
- Respect dirty forms before initiating a save-and-reload switch; cancellation must not persist a new choice. Preserve the current local path/query on successful reload.
- Replace all app-owned visible literals across the client Razor pages/components/services and the host error page, including runtime-generated labels, enum display names, attributes, validation, snackbar/dialog messages, and partial-success/retry instructions.
- Configure MudBlazor localization for required-field errors, pickers, pagination, grids, filters, and built-in accessibility labels; validate against the installed MudBlazor 9.5 behavior.
- Keep enum/filter/protocol values stable while localizing their display labels.
- Localize app-owned error presentation consistently in server and WebAssembly execution. Preserve useful distinctions such as conflict, access denied, dependency unavailable, and partially saved changes. For upstream details rendered today, add/propagate narrowly scoped stable error identifiers where necessary; do not match arbitrary English prose or expose raw exception messages.
- Use the selected culture for human-facing number/date formatting while preserving existing UTC/local-time semantics, timing precision, units, and machine formats.
- Inventory all Razor surfaces and perform a final literal audit so coverage is not limited to sampled pages.

### 6. Verify behavior and regression coverage

- Add tests for English fallback, Norwegian translation completeness, matching format placeholders, and no missing resource keys.
- Test browser language negotiation with quality weights, neutral/regional variants, `nb-NO` and `nn-NO` normalization, unsupported values, and the full preference precedence chain.
- Test anonymous switching, signed-in persistence across browsers/sessions, authentication transitions/shared-browser isolation, unset preference, and profile read/write failures.
- Test mutation authentication/antiforgery, invalid culture rejection, local-return validation, identity isolation, and persistence across service restarts.
- Extend rendering test setup with localization and assert representative public, profile, administration, validation, dialog/error, and MudBlazor output in both languages.
- Validate server prerendering and WebAssembly startup use the same culture, HTML language, resource bundles, and formatted output.
- Use browser validation for switching/reload behavior, unsaved-change cancellation, profile-save failures, and authenticated/anonymous flows.
- Start with targeted Profiles and Platform tests, then run the Release solution build and CI-compatible tests as warranted. Use `OIDC_AUDIENCE=https://build.invalid` for applicable validation; PostgreSQL is required for database integration tests.
- Render/lint the changed Helm charts and verify container publish discovery/configuration.

### 7. Document the feature

- Update README with supported languages, fallback/selection rules, Profiles service ownership, database/deployment configuration, and how to add a language/resource key.
- Update directly related architecture/API/deployment documentation and relevant repository instruction architecture lists if they would otherwise incorrectly describe the number of domain services.
- Record any environment-blocked verification explicitly; do not describe unexecuted tests as passing.

## Dependencies and execution considerations

- Contract validation precedes Profiles implementation and frontend culture/persistence integration.
- Deployment wiring depends on the new service projects/connection contract.
- Translation resources and culture integration can proceed independently after shared conventions are agreed; selector and profile UI integration require the preferences API.
- Final validation depends on all runtime, translation, and deployment work. Final documentation reflects the verified implementation.
- The new service expands the work beyond a frontend-only change, but does not require migrating existing Statistics profiles or Keycloak users.
- English fallback is a runtime safety net, not permission to ship untranslated Norwegian resource keys.
- This handoff document is the portable source of truth; session-local task tracking must be reconstructed on the next computer.
