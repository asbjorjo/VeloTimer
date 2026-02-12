# .github/upgrades/plan.md

# .NET 10 Upgrade Plan for VeloTimer

Table of contents
- [1 Executive Summary](#executive-summary)
- [2 Migration Strategy](#migration-strategy)
- [3 Dependency Analysis & Tier Breakdown](#dependency-analysis--tier-breakdown)
- [4 Per-Tier Specifications](#per-tier-specifications)
  - [Tier 1 — Foundation / Leaf nodes](#tier-1---foundation--leaf-nodes)
  - [Tier 2 — Core Libraries & UI Clients](#tier-2---core-libraries--ui-clients)
  - [Tier 3 — Processors, Migrations, Helpers](#tier-3---processors-migrations-helpers)
  - [Tier 4 — Host & Orchestrators](#tier-4---host--orchestrators)
  - [Tier 5 — Test Projects](#tier-5---test-projects)
- [5 Consolidated Package Update Reference (by tier)](#consolidated-package-update-reference-by-tier)
- [6 Breaking Changes & Expected Code Modifications](#breaking-changes--expected-code-modifications)
- [7 Execution Sequence & Validation Gates](#execution-sequence--validation-gates)
- [8 Testing Strategy](#testing-strategy)
- [9 Risk Management & Mitigation](#risk-management--mitigation)
- [10 Source Control & Branching Strategy](#source-control--branching-strategy)
- [11 Success Criteria](#success-criteria)
- [12 Appendix: Assessment pointers and open questions](#appendix-assessment-pointers-and-open-questions)


---

## 1 Executive Summary

Scenario: upgrade the solution `VeloTimer.sln` from `net9.0` ? `net10.0` (target framework: .NET 10.0 LTS).

Scope & key metrics (from assessment):
- Total projects: 27 (all require target framework change to `net10.0`).
- Total NuGet packages: 92 (43 recommended upgrades, 2 incompatible packages flagged).
- Total issues discovered: 274 (binary/source/behavioral compatibility items reported).
- Notable high-risk projects: `src\IdentityProvider\VeloTime.IdentityProvider.csproj` (multiple binary incompatible APIs), `src\WebUI.Mud\VeloTime.WebUI.Mud.csproj` (several binary/source incompatibilities and many package updates).

Chosen strategy: Bottom-Up (Dependency-First).
Rationale: the solution has a clear dependency hierarchy and many shared libraries used by multiple consumers. Bottom-Up minimizes cascading build failures and avoids multi-targeting: leaf libraries are upgraded first, validated, then consumers are updated.

Primary goals of this plan:
- Retarget all projects to `net10.0` in dependency order
- Apply all recommended package updates (and address packages marked incompatible)
- Resolve binary/source incompatibilities with focused code changes
- Preserve functionality via per-tier validation gates (build + tests + smoke)


## 2 Migration Strategy

Selected approach: Bottom-Up Strategy (dependency-first). Key characteristics adopted:
- Identify leaf projects (no internal project references) ? Tier 1
- Upgrade all projects in a tier together (batch per tier)
- Validate each tier fully before proceeding to next tier
- Keep higher-tier projects on `net9.0` until all their dependencies are upgraded

Why Bottom-Up here:
- Large solution surface (27 projects) with clear dependency tiers
- Multiple shared libraries (Module.* and interfaces) used across apps
- Several projects show package or API incompatibility risk — conservative staged upgrade reduces blast radius

Parallelism policy within tiers:
- Projects in the same tier may be upgraded in parallel if they have no project references between them
- Testing should still be performed together as the tier validation requires the whole tier to be stable

Notes about package updates:
- The assessment lists specific suggested versions. This plan includes those exact versions (see Section 5). All package updates from assessment must be included in the upgrade.
- Packages flagged as incompatible (NuGet.0001) must be resolved before the project is considered stable: either update to a compatible version or replace the package.


## 3 Dependency Analysis & Tier Breakdown

Principles applied for tiering:
1. Tier 1: Projects with no internal project references (leaf nodes)
2. Tier N+1: Projects that depend only on projects in Tiers <= N
3. Test projects placed last (migrate after product projects they test)

Derived tiers (topology extracted from assessment):

Tier 1 (Leaf nodes - no internal project references)
- `src\Agent.Interface\VeloTime.Agent.Interface.csproj`
- `src\Module.Common\VeloTime.Module.Common.csproj`
- `src\Module.Facilities.Interface\VeloTime.Module.Facilities.Interface.csproj`
- `src\Module.Statistics.Interface\VeloTime.Module.Statistics.Interface.csproj`
- `src\Module.Timing.Interface\VeloTime.Module.Timing.Interface.csproj`
- `src\ServiceDefaults\VeloTime.ServiceDefaults.csproj`
- `src\WebUI.Mud.Client\VeloTime.WebUI.Mud.Client.csproj`
- `src\IdentityProvider\VeloTime.IdentityProvider.csproj` (note: although an AspNetCore app, assessment shows it has no internal project references)

Why Tier 1: these projects have Dependencies=0 in the assessment and are referenced by multiple other projects. Stabilizing them first gives downstream consumers consistent modern binaries.

Tier 2 (Depends only on Tier 1)
- `src\Agent\VeloTime.Agent.csproj` (depends on `Agent.Interface`, `Module.Timing.Interface`)
- `src\Module.Timing\VeloTime.Module.Timing.csproj` (depends on `Module.Timing.Interface`, `Module.Common`)
- `src\Module.Facilities\VeloTime.Module.Facilities.csproj` (depends on `Module.Facilities.Interface`, `Module.Common`)
- `src\Module.Statistics\VeloTime.Module.Statistics.csproj` (depends on `Module.Statistics.Interface`, `Module.Common`)
- `src\WebUI.Mud\VeloTime.WebUI.Mud.csproj` (depends on `WebUI.Mud.Client`, `ServiceDefaults`, interface libraries)
- Lightweight API projects that reference only Tier1 libs: `src\Module.Facilities.Api`, `src\Module.Statistics.Api`, `src\Module.Timing.Api`

Why Tier 2: these projects consume only Tier 1 libraries (per assessment) and will compile against upgraded Tier 1 outputs.

Tier 3 (Depends on Tier 1 & Tier 2)
- `src\Agent.Dummy\VeloTime.Agent.Dummy.csproj` (depends on `Agent`)
- `src\Module.Facilities.Processor`, `src\Module.Statistics.Processor`, `src\Module.Timing.Processor` (processors that depend on modules in Tier2 and `ServiceDefaults`)
- Migration and short-lived runner projects that depend on Tier2 libs (e.g., `Module.*.Migration` projects in some cases — evaluate per-project during tier runtime)
- `src\Bootstrap\VeloTime.Bootstrap.csproj` (references multiple Tier2 projects)

Tier 4 (Host & Orchestrators)
- `src\AppHost\VeloTime.AppHost.csproj` (depends on many Tier2/3 projects and third-party host packages like Aspire)
- Identity-related migration projects that reference IdentityProvider and host-level tooling

Tier 5 (Test projects)
- `src\VeloTime.Module.Timing.Test\VeloTime.Module.Timing.Test.csproj` and other test projects

Notes and assumptions:
- Circular dependencies: assessment shows no explicit cycles; if any circular dependency is discovered during upgrade, merge involved projects into same tier and upgrade together.
- Test projects are intentionally scheduled last to avoid multi-targeting; they will be upgraded once the projects they exercise are upgraded.


## 4 Per-Tier Specifications

For each tier the plan lists metadata, upgrade details, package groups, known breaking-change exposure, code modifications expected, and validation requirements.

### Tier 1 — Foundation / Leaf nodes

Tier metadata
- Tier number: 1
- Projects included:
  - `Agent.Interface`
  - `Module.Common`
  - `Module.Facilities.Interface`
  - `Module.Statistics.Interface`
  - `Module.Timing.Interface`
  - `ServiceDefaults`
  - `WebUI.Mud.Client`
  - `IdentityProvider` (leaf by project references)
- Dependencies on previous tiers: external NuGet packages only
- Estimated complexity: Low ? Medium (some projects like `IdentityProvider` have high LOC but appear as leaf due to no internal references)

Upgrade details
- Framework change: update `<TargetFramework>` from `net9.0` ? `net10.0` for all projects in tier
- Project file updates: single batched change for the tier (edit csproj TargetFramework or TargetFrameworks entries)
- Package updates (tier-scoped):
  - `Microsoft.Extensions.DependencyInjection`: `9.0.12` ? `10.0.3` (affects `Module.Facilities.Interface`, `Module.Statistics.Interface`, `Module.Timing.Interface`)
  - `Microsoft.Extensions.Http`: `9.* / 9.0.12` ? `10.0.3` (affects interfaces where present)
  - `Microsoft.AspNetCore.Components.WebAssembly` is targeted in `WebUI.Mud.Client` and will be updated in Tier 1 ? `10.0.3`
  - Smaller runtime libraries used by `Module.Common` (e.g., `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` references) — ensure suggested versions from assessment are applied

Breaking changes / code modifications expected
- Mostly source-incompatible and behavioral warnings flagged in assessment. Expect to:
  - Recompile and fix API signature changes (e.g., changes in `OpenIdConnectOptions` properties if used in IdentityProvider)
  - Verify `System.Uri` behavioral changes in code that constructs URIs (assessment flagged `T:System.Uri` occurrences)
  - Replace or adapt minor API usages where compiler errors occur

Validation requirements (Tier 1)
- Build all Tier 1 projects successfully
- Run any unit tests defined inside Tier 1 projects (there are usually none) and smoke-run `IdentityProvider` if standalone
- Confirm no package incompatibility warnings remain
- Confirm `net10.0` TargetFramework visible in project files
- Mark Tier 1 done only after above validations


### Tier 2 — Core Libraries & UI Clients

Tier metadata
- Tier number: 2
- Projects included:
  - `Agent`
  - `Module.Timing`
  - `Module.Facilities`
  - `Module.Statistics`
  - `WebUI.Mud` (Server app)
  - API projects that depend only on tier1 libs (`Module.*.Api`)
- Dependencies: Tier 1
- Estimated complexity: Medium (WebUI.Mud is larger and has many flagged API issues)

Upgrade details
- Framework change: target `net10.0`
- Package updates (tier-scoped):
  - `Microsoft.EntityFrameworkCore`: `9.0.12` ? `10.0.3` (applies to `Agent`, `Module.Common` consumers)
  - `Microsoft.AspNetCore.*` family: `9.*` ? `10.0.3` (WebUI + Identity providers)
  - `Microsoft.Extensions.Hosting` and hosting abstractions: `9.0.12` ? `10.0.3`
  - `Microsoft.Extensions.Caching.StackExchangeRedis`: `9.0.12` ? `10.0.3` (if used by Module.Common)
  - `Microsoft.AspNetCore.Components.WebAssembly.Authentication`: `9.*` ? `10.0.3` (ensure `WebUI.Mud.Client` updates align)

Breaking changes / code modifications expected
- `WebUI.Mud` and `IdentityProvider` show most binary/source incompatible APIs. Expected work:
  - Review and update OpenID Connect configuration (`OpenIdConnectOptions` property changes and token handler configuration)
  - Update code referencing `System.IdentityModel.Tokens.Jwt` types if binary incompatible signatures reported
  - Address ActivitySource usage changes and `System.Uri` behavioral changes in HTTP/redirect logic

Validation requirements (Tier 2)
- All Tier 2 projects build successfully when referencing upgraded Tier 1 outputs (or local projects)
- Run Tier 2 unit tests and UI smoke tests (start `WebUI.Mud` and verify pages render; run `WebUI.Mud.Client` build)
- Verify no remaining incompatible NuGet warnings for Tier 2 projects


### Tier 3 — Processors, Migrations, Helpers

Tier metadata
- Tier number: 3
- Projects included:
  - `Agent.Dummy`
  - `Module.*.Processor` projects
  - `Module.*.Migration` projects (some may be Tier2 or Tier3 depending on dependencies — treat them within this tier if they reference Tier2)
  - `Bootstrap`
- Dependencies: Tier 1 & Tier 2
- Estimated complexity: Low ? Medium

Upgrade details
- Framework change: `net10.0`
- Package updates (tier-scoped):
  - Any migration runner packages and migration-time dependencies to match EF Core 10 packages (if migrations use EF tooling)
  - `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` and similar tooling packages may appear here — review for incompatibility

Breaking changes / code modifications expected
- Migration projects often contain scripts and direct tool invocations; ensure CLI/SDK compatibility and update `dotnet-ef` or design package versions as needed

Validation requirements (Tier 3)
- Build processors and migration projects successfully
- If processors interact with databases or external services, run integration smoke checks against dev/test endpoints


### Tier 4 — Host & Orchestrators

Tier metadata
- Tier number: 4
- Projects included:
  - `AppHost` and orchestrator/host-level projects
  - Identity provider migration host projects (if separate)
- Dependencies: Tier 1..Tier 3
- Estimated complexity: Medium ? High (host uses Aspire.* packages and several integration points)

Upgrade details
- Framework change: `net10.0`
- Package updates (tier-scoped):
  - `Aspire.Hosting.AppHost`: `13.1.0` ? `13.1.1`
  - `Aspire.Hosting.PostgreSQL`: `13.1.0` ? `13.1.1`
  - `Aspire.Hosting.Redis`: `13.1.0` ? `13.1.1`
  - Other host-side packages per assessment
  - Resolve `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` incompatibilities — either update to compatible packages or remove/replace for CI builds

Breaking changes / code modifications expected
- Host-level configuration and DI wiring changes dependent on updated AspNetCore/hosting packages
- Container tooling package incompatibilities may require changes to local dev container targets and CI scripts

Validation requirements (Tier 4)
- Build AppHost and verify startup logs (no exceptions during host start)
- Run smoke end-to-end scenario that loads AppHost and a small subset of endpoints
- Verify container build tasks in CI (if package removal/updates affected them)


### Tier 5 — Test Projects

Tier metadata
- Tier number: 5
- Projects included: test projects (e.g., `VeloTime.Module.Timing.Test`)
- Dependencies: product projects (Tiers 1..4)
- Estimated complexity: Low

Upgrade details
- Framework change: `net10.0`
- Update test SDK: keep `Microsoft.NET.Test.Sdk` as compatible; update runners if needed

Validation requirements (Tier 5)
- Run full test suite (`dotnet test`) and expect no failing tests caused by upgrade changes
- If tests rely on environment or local services, run integration test harnesses post-upgrade


## 5 Consolidated Package Update Reference (by tier)

Notes: use exact suggested versions from assessment. The list below includes primary packages flagged by the assessment and maps them to tier scope. Executors should apply these specific versions.

Tier 1 Package Updates
- `Microsoft.Extensions.DependencyInjection`: `9.0.12` ? `10.0.3` (affects interface libraries)
- `Microsoft.Extensions.Http`: `9.0.12` ? `10.0.3`
- `Microsoft.AspNetCore.Components.WebAssembly`: `9.*` ? `10.0.3` (`WebUI.Mud.Client`)

Tier 2 Package Updates
- `Microsoft.EntityFrameworkCore`: `9.0.12` ? `10.0.3` (`Agent`, `Module.Common` consumers)
- `Microsoft.EntityFrameworkCore.Design/Relational/Tools`: `9.*` ? `10.0.3` (applies where design/EF tooling used)
- `Microsoft.AspNetCore.Authentication.OpenIdConnect`: `9.* / 9.0.12` ? `10.0.3` (`WebUI.Mud`, `IdentityProvider`)
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` & `Microsoft.AspNetCore.Identity.UI`: `9.*` ? `10.0.3` (`IdentityProvider`)
- `Microsoft.Extensions.Hosting` / `Host.Abstractions`: `9.0.12` ? `10.0.3`
- `Microsoft.Extensions.Caching.StackExchangeRedis`: `9.0.12` ? `10.0.3` (Module.Common consumers)

Tier 3 Package Updates
- `Microsoft.Extensions.ServiceDiscovery` / `Microsoft.Extensions.Http.Resilience`: `9.x` ? `10.3.0` (`ServiceDefaults` and processors)
- Migration runner dependencies: update EF-related tooling packages per Tier 2

Tier 4 Package Updates
- `Aspire.Hosting.AppHost`: `13.1.0` ? `13.1.1` (AppHost)
- `Aspire.Hosting.*` companion packages: update to `13.1.1` where suggested
- Tooling: `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` flagged as incompatible in some projects — resolve per CI pipeline needs

Global rules
- Include ALL suggested versions from the assessment; do not use vague "latest" references
- If a package is flagged `incompatible` (NuGet.0001), prioritise resolving it during the same tier upgrade
- For EF Core, keep relational provider versions aligned (e.g., `Npgsql.EntityFrameworkCore.PostgreSQL` compatible with EF Core 10)


## 6 Breaking Changes & Expected Code Modifications

This section maps notable item categories from the assessment to actions an executor should expect.

High-impact items (examples from assessment):
- `System.IdentityModel.Tokens.Jwt` types — binary incompatible in `IdentityProvider` and `WebUI.Mud`. Action: update JWT package versions, review code using `JwtSecurityToken` and `JwtSecurityTokenHandler`, adapt to changed constructors/properties.
- `OpenIdConnectOptions` property changes — update configuration usage and token handler settings.
- `System.Uri` behavioral changes — review URI construction and tests that depend on exact normalization/escaping behaviors.
- `ActivitySource.StartActivity` semantics — confirm telemetry initialization still works and adjust ActivityKind if required.
- `Microsoft.Extensions.Configuration.ConfigurationBinder.Get<T>` binary incompatibility instances — test config-based options binding and adjust extension calls if signature changed.

Common code fixes:
- Replace obsolete or removed APIs with recommended equivalents reported by compiler errors
- Update `AddDbContext`/EF registration patterns if EF Core 10 modifies recommended usage
- Revisit cookie/OpenID auth configuration to align with updated `AddOpenIdConnect` patterns

Checklist for code modifications per project (executor):
- Recompile and fix all compiler errors; prefer minimal, targeted API adjustments
- Run unit tests and adapt tests for behavioral changes as required
- For binary incompatible changes, locate usages and apply explicit replacements (no automatic code fixes)


## 7 Execution Sequence & Validation Gates

Strict ordering rules (Bottom-Up):
1. Upgrade Tier 1 (all projects in tier) ? validate builds/tests
2. Upgrade Tier 2 ? validate build/tests and integration with Tier1
3. Upgrade Tier 3 ? validate build/tests and integration with Tiers 1-2
4. Upgrade Tier 4 ? validate orchestration and host start
5. Upgrade Tier 5 tests and run full solution tests

Tier completion criteria (what "done" means):
- All projects in tier target `net10.0` in csproj files
- All package updates listed for the tier are applied
- All projects in tier build successfully (no errors)
- Unit tests for the tier pass
- Integration smoke tests to confirm behavior against previous tiers
- Documented lessons/notes and PR created for tier changes

Between-tier validation (before starting next tier):
- Verify consumers (higher tiers) still compile/run using the new binaries from lower tiers (they will still be net9.0 until their turn but must load/consume artifacts)
- Confirm no unresolved NuGet incompatibilities remain

Deployment cadence recommendation
- Create one PR per tier (batch operations): project file updates + package updates + required code fixes
- Each PR must include build artifact or CI success and a checklist of validations

Task batching guidance (for execution stage):
- Each phase (Preparation, Update, Testing, Stabilization) is one task per tier
- Within a tier, group all project file updates in a single commit
- Apply package updates for the tier in a single commit where possible


## 8 Testing Strategy

Multi-level testing approach
- Per-project unit tests: run immediately after upgrading a project
- Tier-level tests: run after all projects in the tier are upgraded and built together
- Integration tests: run between tiers (lower-tier artifacts + current-tier projects)
- Full-solution end-to-end tests: after final tier complete

Validation checklist (apply per tier):
- [ ] Projects build without errors
- [ ] No new critical warnings for removed APIs
- [ ] Unit tests pass for projects in tier
- [ ] Integration smoke tests against lower-tier services pass
- [ ] Web UI smoke tests: pages render and no major runtime errors in console
- [ ] Worker services start/stop cleanly

Test runner notes
- Use `dotnet test` with the upgraded SDK
- Ensure `global.json` (if present) is updated to allow `net10.0` SDK, or inform CI to install the .NET 10 SDK


## 9 Risk Management & Mitigation

Top risks identified in assessment
- High-risk: `IdentityProvider` and `WebUI.Mud` (binary incompatibilities)
  - Mitigation: treat these as higher validation priority, allocate additional reviewers, and run expanded integration tests for auth flows
- Package incompatibilities: `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` flagged incompatible
  - Mitigation: review CI container tasks, update/replace tools or adjust CI pipeline to use supported targets
- EF Core provider mismatches
  - Mitigation: upgrade EF Core and provider packages together and run DB-related integration tests

Rollback & contingency
- Each tier's changes should be contained in a single branch/PR to enable easy revert
- If a package cannot be updated safely, consider temporary multi-targeting of the consumer project until replacement available — note: Bottom-Up strategy prefers avoiding multi-targeting; use only as last resort and document reasons

Monitoring
- After each tier PR merges, run smoke CI pipeline and verify telemetry (ActivitySource) and error rates in test environment


## 10 Source Control & Branching Strategy

Branching
- Work branch (already created): `upgrade-to-NET10` (based on `restructure` source branch)
- Per-tier PRs: create per-tier feature branches off `upgrade-to-NET10` (e.g., `upgrade/Tier1-foundation`, `upgrade/Tier2-core`) or create incremental commits on `upgrade-to-NET10` and open one PR per tier (preferred to keep changes grouped)

Commit strategy
- Batch changes per tier:
  1. Commit: `chore(upgrade): retarget Tier N projects to net10.0`
  2. Commit: `chore(upgrade): update NuGet packages for Tier N`
  3. Commit: `fix(upgrade): code adjustments for compilation errors in Tier N`
- PR must include: short description, list of projects changed, package update table, validation checklist

PR & review
- Require at least one reviewer with knowledge of authentication/hosting for `IdentityProvider` and `AppHost` PRs
- Attach CI build logs and test results to PR


## 11 Success Criteria

The migration is complete when all the following are true:
1. All projects target `net10.0` (project files updated)
2. All package updates from the assessment are applied and no packages remain flagged as incompatible
3. Solution builds without errors
4. All unit and integration tests pass
5. Web UI, Worker, and Host projects start and basic smoke scenarios pass
6. No unresolved binary incompatibilities remain (or documented mitigations exist)


## 12 Appendix: Assessment pointers and open questions

- The assessment enumerates many `System.Uri` behavioral changes and `ActivitySource` effects — automated tests should pay attention to telemetry and URL parsing behavior in integration tests.
- `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` flagged incompatible in some projects; confirm if these targets are required for CI container builds. If not needed, remove them or update.
- Security vulnerabilities: the analysis report did not return explicit CVE-level vulnerabilities. If you want, re-scan packages for advisories and include remediations in this plan.

Open questions for executor/team before starting execution stage:
1. Do you want security-vulnerable packages addressed during this upgrade, or deferred to a later task? (recommended: address now)
2. Are there CI constraints (e.g., pinned global.json or pinned SDK on build agents) that must be changed prior to starting Tier 1?
3. Are we allowed to perform limited multi-targeting for any project (temporary net9.0+net10.0) if an essential dependency lacks a net10.0 build?


---

End of plan. Follow the Execution sequence in Section 7. Create per-tier PRs and follow validation gates strictly — do not proceed to next tier until previous tier is validated.
