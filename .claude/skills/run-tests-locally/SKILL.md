---
name: run-tests-locally
description: Run VeloTimer .NET tests locally the way CI does, including the PostgreSQL integration tests that otherwise skip silently. Use when validating a change, reproducing a CI failure, or before claiming tests pass.
---

# Run tests locally

Many integration tests use `[Fact]` attributes that **skip** when their PostgreSQL connection variable is missing, so a green run without them proves nothing about database behavior. CI provides a `postgres:17-alpine` service and sets `OIDC_AUDIENCE` plus five connection variables. [scripts/test-with-postgres.sh](scripts/test-with-postgres.sh) reproduces that setup.

## Usage

From the repository root (Docker is available in the devcontainer):

```bash
# Targeted: one project, optionally filtered
.claude/skills/run-tests-locally/scripts/test-with-postgres.sh src/Module.Statistics.Api.Test --filter "FullyQualifiedName~StatsProfile"

# The CI baseline
.claude/skills/run-tests-locally/scripts/test-with-postgres.sh VeloTimer.sln --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov

# Export the variables into your shell, for an IDE or repeated runs
eval "$(.claude/skills/run-tests-locally/scripts/test-with-postgres.sh --env)"

# Remove the test server when done
.claude/skills/run-tests-locally/scripts/test-with-postgres.sh --stop
```

The script starts or reuses a disposable `velotime-test-postgres` container on `127.0.0.1:55432` (override with `VELOTIME_TEST_PG_PORT`). Tests create and drop their own databases from the `postgres` control database.

## Choosing scope

1. Start with the test project closest to the change (see the project list in AGENTS.md). Use `--filter` for a single class or test.
2. Broaden to dependent projects when a shared project changed (`Module.Common`, `ServiceDefaults`, an `*.Interface`, or a generated client).
3. Run the full solution before declaring a cross-cutting change done.
4. For registration-date JavaScript: `node --test src/VeloTime.Platform.Test/RegistrationDates.test.mjs`.

## Reporting

- Report the exact command and the `Passed/Failed/Skipped/Total` line. **A non-zero `Skipped` count on database tests means the environment was wrong, not that they passed.**
- To tell new failures from existing ones, rerun the failing test on the base commit (`git stash` or a separate worktree) before attributing it to the change.
- Never weaken assertions or skip tests to get a green run.
