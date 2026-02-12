# VeloTimer .NET 10 Upgrade Tasks

## Overview

This document lists executable tasks to perform the Bottom-Up (dependency-first) upgrade of `VeloTimer.sln` to .NET 10.0, progressing tier-by-tier from leaf libraries to test projects with per-tier validation gates.

**Progress**: 1/11 tasks complete (9%) ![0%](https://progress-bar.xyz/9)

---

## Tasks

### [✓] TASK-001: Verify prerequisites *(Completed: 2026-02-12 20.54)*
**References**: Plan §8 Testing Strategy, Plan §2 Migration Strategy, Plan §10 Source Control & Branching Strategy

- [✓] (1) Verify .NET 10 SDK is installed on the executor environment per Plan §8
- [✓] (2) Runtime/SDK version meets minimum requirements (**Verify**)
- [✓] (3) If present, update `global.json` to allow the .NET 10 SDK per Plan §8 (or record CI agents that must be updated)
- [✓] (4) `global.json` updated or CI owner notified (**Verify**)

### [▶] TASK-002: Upgrade Tier 1 foundation/leaf projects (retarget + package updates + build)
**References**: Plan §3 Dependency Analysis & Tier Breakdown, Plan §4 Tier 1, Plan §5 Package Update Reference, Plan §6 Breaking Changes

- [▶] (1) Update `<TargetFramework>` to `net10.0` in all Tier 1 projects listed in Plan §4 (batch per tier)
- [ ] (2) All Tier 1 project files updated to `net10.0` (**Verify**)
- [ ] (3) Apply Tier 1 package updates per Plan §5 (exact versions from assessment)
- [ ] (4) All Tier 1 package references updated and `dotnet restore` completes successfully (**Verify**)
- [ ] (5) Build all Tier 1 projects and fix compilation errors per Plan §6
- [ ] (6) All Tier 1 projects build with 0 errors (**Verify**)
- [ ] (7) Commit changes with message: "TASK-002: Upgrade Tier 1 - retarget projects and apply package updates"

### [ ] TASK-003: Test Tier 1 and stabilize
**References**: Plan §4 Tier 1, Plan §7 Execution Sequence & Validation Gates, Plan §8 Testing Strategy

- [ ] (1) Run unit tests for Tier 1 projects (if present) per Plan §4
- [ ] (2) Fix any test failures referencing Plan §6 breaking changes
- [ ] (3) Re-run Tier 1 tests after fixes
- [ ] (4) All Tier 1 tests pass with 0 failures (**Verify**)
- [ ] (5) Commit test fixes with message: "TASK-003: Tier 1 testing complete"

### [ ] TASK-004: Upgrade Tier 2 core libraries & UI clients (retarget + package updates + build)
**References**: Plan §3 Dependency Analysis & Tier Breakdown, Plan §4 Tier 2, Plan §5 Package Update Reference, Plan §6 Breaking Changes

- [ ] (1) Update `<TargetFramework>` to `net10.0` in all Tier 2 projects listed in Plan §4 (batch per tier)
- [ ] (2) All Tier 2 project files updated to `net10.0` (**Verify**)
- [ ] (3) Apply Tier 2 package updates per Plan §5 (exact versions from assessment)
- [ ] (4) All Tier 2 package references updated and `dotnet restore` completes successfully (**Verify**)
- [ ] (5) Build Tier 2 projects against upgraded Tier 1 outputs and fix compilation errors per Plan §6
- [ ] (6) Tier 2 projects build with 0 errors (**Verify**)
- [ ] (7) Commit changes with message: "TASK-004: Upgrade Tier 2 - retarget projects and apply package updates"

### [ ] TASK-005: Test Tier 2 and validate integration with Tier 1
**References**: Plan §4 Tier 2, Plan §7 Execution Sequence & Validation Gates, Plan §8 Testing Strategy

- [ ] (1) Run Tier 2 unit tests and run automated startup smoke for `WebUI.Mud` to confirm no startup exceptions (no manual UI checks)
- [ ] (2) Fix any test or startup failures (reference Plan §6 breaking changes)
- [ ] (3) Re-run Tier 2 tests and automated startup after fixes
- [ ] (4) All Tier 2 tests pass with 0 failures and automated startup shows no exceptions (**Verify**)
- [ ] (5) Commit test fixes with message: "TASK-005: Tier 2 testing complete"

### [ ] TASK-006: Upgrade Tier 3 processors, migrations, helpers (retarget + package updates + build)
**References**: Plan §3 Dependency Analysis & Tier Breakdown, Plan §4 Tier 3, Plan §5 Package Update Reference, Plan §6 Breaking Changes

- [ ] (1) Update `<TargetFramework>` to `net10.0` in all Tier 3 projects listed in Plan §4 (batch per tier)
- [ ] (2) All Tier 3 project files updated to `net10.0` (**Verify**)
- [ ] (3) Apply Tier 3 package updates per Plan §5 (exact versions from assessment) and update migration/tooling packages as needed
- [ ] (4) All Tier 3 package references updated and `dotnet restore` completes successfully (**Verify**)
- [ ] (5) Build Tier 3 projects and fix compilation/errors related to migration tooling per Plan §6
- [ ] (6) Tier 3 projects build with 0 errors (**Verify**)
- [ ] (7) Commit changes with message: "TASK-006: Upgrade Tier 3 - retarget projects and apply package updates"

### [ ] TASK-007: Test Tier 3 and run integration smoke checks
**References**: Plan §4 Tier 3, Plan §7 Execution Sequence & Validation Gates, Plan §8 Testing Strategy

- [ ] (1) Run Tier 3 unit tests and any automated integration smoke checks that exercise Tier 1-2 interactions per Plan §8
- [ ] (2) Fix any test or integration failures (reference Plan §6)
- [ ] (3) Re-run tests after fixes
- [ ] (4) All Tier 3 tests and integration smoke checks pass with 0 failures (**Verify**)
- [ ] (5) Commit test/integration fixes with message: "TASK-007: Tier 3 testing complete"

### [ ] TASK-008: Upgrade Tier 4 host & orchestrators (retarget + package updates + build)
**References**: Plan §3 Dependency Analysis & Tier Breakdown, Plan §4 Tier 4, Plan §5 Package Update Reference, Plan §6 Breaking Changes

- [ ] (1) Update `<TargetFramework>` to `net10.0` in all Tier 4 projects listed in Plan §4 (batch per tier)
- [ ] (2) All Tier 4 project files updated to `net10.0` (**Verify**)
- [ ] (3) Apply Tier 4 package updates per Plan §5 (exact versions from assessment), resolve container/CI tooling incompatibilities per Plan §4/§5
- [ ] (4) All Tier 4 package references updated and `dotnet restore` completes successfully (**Verify**)
- [ ] (5) Build AppHost and host projects and fix compilation or host-configuration errors per Plan §6
- [ ] (6) Tier 4 hosts build with 0 errors and automated host startup shows no exceptions (**Verify**)
- [ ] (7) Commit changes with message: "TASK-008: Upgrade Tier 4 - retarget hosts and apply package updates"

### [ ] TASK-009: Test Tier 4 and run host-level smoke scenarios
**References**: Plan §4 Tier 4, Plan §7 Execution Sequence & Validation Gates, Plan §8 Testing Strategy

- [ ] (1) Run Tier 4 unit tests and automated host startup smoke scenarios (verify no startup exceptions, no CI container build failures)
- [ ] (2) Fix any failures or hosting integration issues (reference Plan §6)
- [ ] (3) Re-run tests and smoke scenarios after fixes
- [ ] (4) All Tier 4 tests and automated smoke scenarios pass with 0 failures (**Verify**)
- [ ] (5) Commit test/fix changes with message: "TASK-009: Tier 4 testing complete"

### [ ] TASK-010: Upgrade Tier 5 test projects (retarget test projects + package updates + build)
**References**: Plan §3 Dependency Analysis & Tier Breakdown, Plan §4 Tier 5, Plan §5 Package Update Reference, Plan §8 Testing Strategy

- [ ] (1) Update `<TargetFramework>` to `net10.0` in all test projects listed in Plan §4 and update `Microsoft.NET.Test.Sdk`/runners per Plan §5
- [ ] (2) All test project files updated to `net10.0` and test SDKs updated (**Verify**)
- [ ] (3) Apply any Tier 5 package updates per Plan §5 and run `dotnet restore`
- [ ] (4) Test projects restore and build successfully (**Verify**)
- [ ] (5) Commit changes with message: "TASK-010: Upgrade Tier 5 - retarget tests and update test SDKs"

### [ ] TASK-011: Run full solution test suite and validate upgrade
**References**: Plan §7 Execution Sequence & Validation Gates, Plan §8 Testing Strategy, Plan §11 Success Criteria

- [ ] (1) Run full test suite (`dotnet test` for all test projects) per Plan §8
- [ ] (2) Fix any failing tests caused by upgrade changes (reference Plan §6 breaking changes)
- [ ] (3) Re-run full test suite after fixes
- [ ] (4) All tests pass with 0 failures (**Verify**)
- [ ] (5) Commit test fixes with message: "TASK-011: Run full test suite and complete upgrade validation"


