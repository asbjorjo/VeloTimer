# VeloTimer Agent Instructions

## Project Overview

VeloTimer is a competitive cycling race timing system built as .NET 10 microservices orchestrated with Aspire. It has five domain services (Timing, Facilities, Statistics, Profiles, Notifications), shared infrastructure for hardware timing agents, a Blazor web frontend with a backend-for-frontend, and event-driven processors.

**Key Technologies:**

- `.NET 10` (all projects)
- **Aspire** for microservices orchestration
- **Entity Framework Core** with PostgreSQL
- **Blazor Server** for UI (with MudBlazor components)
- **SlimMessageBus** over **Azure Service Bus** for async messaging, with a PostgreSQL outbox
- **NSwag** clients generated from build-time OpenAPI documents
- **Keycloak** for authentication (OIDC)
- **OpenTelemetry** for observability
- **Helm** charts for Kubernetes deployment

## Working In The Repository

- Use the checked-in CI commands as the build and test baseline: `dotnet test --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov` and `dotnet build --configuration Release`. See [CI](.github/workflows/ci.yml) and [publish workflow](.github/workflows/publish.yml).
- Test projects: `src/VeloTime.Module.Timing.Test/`, `src/VeloTime.Module.Facilities.Test/`, `src/VeloTime.Module.Statistics.Test/`, `src/Module.Timing.Api.Test/`, `src/Module.Facilities.Api.Test/`, `src/Module.Statistics.Api.Test/`, `src/Module.Profiles.Api.Test/`, `src/Module.Notifications.Api.Test/`, `src/Module.Notifications.Processor.Test/`, `src/VeloTime.Platform.Test/`, and `src/VeloTime.Workers.Test/`. Start with targeted checks; use [TransponderTest.cs](src/VeloTime.Module.Timing.Test/TransponderTest.cs) for a domain-test example. The README JavaScript test is `node --test src/VeloTime.Platform.Test/RegistrationDates.test.mjs`. CI test runs need `OIDC_AUDIENCE=https://build.invalid`; PostgreSQL is required for tests that exercise database integration.
- For concrete implementations, follow [TimingEndpoints.cs](src/Module.Timing.Api/Endpoints/TimingEndpoints.cs) for route grouping, [TransponderEndpoints.cs](src/Module.Timing.Api/Endpoints/TransponderEndpoints.cs) for typed results, [PassingObservedHandler.cs](src/Module.Timing.Processor/Handlers/PassingObservedHandler.cs) for message consumers, [InstallationMapper.cs](src/Module.Timing/Mapping/InstallationMapper.cs) for entity-to-contract mapping, and [Module.Timing/HostingExtensions.cs](src/Module.Timing/HostingExtensions.cs) for module registration.
- Package versions are centrally managed in [Directory.Packages.props](Directory.Packages.props); avoid adding per-project versions unless a project has an established exception.

## Time and Unit Conventions

- Backend services, APIs, messages, and databases represent and persist all times and dates in UTC. Do not store or exchange browser-local times; convert at user-facing boundaries.
- Backend services, APIs, messages, and databases represent and persist quantities in base SI units. Do not store or exchange values in display or customary units; convert at user-facing boundaries.
- User-facing times and units are formatted according to the browser locale or the user's explicit preferences. Keep presentation conversion and formatting in the UI, not in domain logic or persistence.

## Specialist Agent Workflow

The shared architecture and conventions in this file remain the source of truth. The eight workspace agents are configured for selection in the agent picker and for subagent invocation: [Coordinator](.github/agents/coordinator.agent.md), [Tester](.github/agents/tester.agent.md), [.NET API Developer](.github/agents/dotnet-api-developer.agent.md), [Blazor UI Developer](.github/agents/blazor-ui-developer.agent.md), [Data & Migrations Developer](.github/agents/data-migrations-developer.agent.md), [Messaging & Workers Developer](.github/agents/messaging-workers-developer.agent.md), [Platform & Deployment Developer](.github/agents/platform-deployment-developer.agent.md), and [Code Reviewer](.github/agents/code-reviewer.agent.md). Keep agent models unpinned.

- Handle trivial tasks directly; avoid unnecessary delegation. For substantial work, the Coordinator routes to specialists, assigns non-overlapping ownership, and sequences dependencies. The normal sequence is developer → Tester → Reviewer. Send requested fixes through the Coordinator to the original owner.
- When work crosses specialist boundaries through shared contracts, the Coordinator assigns one relevant specialist to create/update and validate the contract first (or confirm an existing contract is compatible). Only then delegate dependent implementations with the agreed artifacts, semantics, compatibility decisions, and file ownership. Failed contract validation blocks affected dependents; later revisions return to the contract owner before those implementations resume. Directly selected specialists must report such prerequisites through the Coordinator rather than independently redefine shared contracts. See the [Coordinator contract-first gate](.github/agents/coordinator.agent.md#contract-first-gate).
- When selected directly or delegated, a specialist must stay within its role and must not delegate further. Return a complete report with changed files, contract decisions, checks and exact results, blockers, and the next owner. The Tester adds or updates tests, reports exact outcomes, and never changes production code to suppress a failure. The Reviewer is read-only.
- For portable Copilot SDK compatibility, do not define handoffs or `agents` YAML fields. Move manually by selecting the next agent in the picker and providing the previous agent's report. Start a fresh session or reload the workspace to discover new agents. Agent discovery and behavior depend on runtime support and higher-priority instructions.

## Git Workflow

- Before making changes, start each independent session in a separate Git worktree on a dedicated branch based on the appropriate up-to-date base branch, for example `git worktree add -b <branch> <separate-path> <base-ref>`. Run edits, builds, tests, and commits from that worktree so multiple sessions can work in parallel without sharing working files or an index. Do not make changes directly on the default branch.
- Reuse the session's assigned worktree for sequential specialist handoffs. Parallel editing specialists must use separate worktrees and branches; the Coordinator integrates their commits explicitly. Read-only reviews do not require a new worktree.
- Preserve existing worktrees and uncommitted changes belonging to users or other sessions. Do not switch their branches, stage their files, or remove their worktrees.
- Make atomic commits as work progresses, after each focused, self-contained change passes its relevant checks; do not defer all commits until the end. Stage only the files or hunks belonging to that change, and never commit unrelated work or known-broken intermediate states.
- Use Conventional Commit messages in the format `<type>(<scope>): <imperative summary>`, for example `fix(timing): validate passing timestamps`.

## Architecture

### Microservices Structure

The system is organized as **independent domain services with vertical slices**. Timing, Facilities, Statistics, Profiles, and Notifications each follow the layout below; Profiles has no Processor.

```
src/
├── Module.{Domain}/             # Core: domain logic, EF model, DbContext, and Migrations/
├── Module.{Domain}.Api/         # Service: Minimal API endpoints; emits api/VeloTime.Module.{Domain}.Api.json on build
├── Module.{Domain}.Client/      # Client: NSwag client generated from the Api project
├── Module.{Domain}.Interface/   # Contracts: shared DTOs and messages consumed by other services
├── Module.{Domain}.Processor/   # Service: background worker consuming events
├── Module.{Domain}.Migration/   # Runner: applies Module.{Domain}/Migrations at startup
│
├── Agent/                       # Library: shared infrastructure for concrete agent implementations (external)
├── Agent.Interface/             # Contracts: agent events/commands shared with processors
├── Agent.Dummy/                 # Dev tool: local agent simulator for development/testing only
│
├── Module.Common/               # Shared: module storage/cache registration, MigrationWorker, admin authorization
├── ServiceDefaults/             # Shared: Aspire defaults (health checks, OTEL, discovery, resilience)
├── MessageBus.Topology/         # Job: reconciles Azure Service Bus topics/subscriptions from topology.json
├── Bootstrap/                   # Utility: seed data and initialization
├── Users.Migration/             # Runner: one-off import of legacy VeloTimer riders into Keycloak, Timing, and Statistics
├── WebUI.Mud/                   # Service: Blazor Server host and backend-for-frontend (BFF) endpoints
├── WebUI.Mud.Client/            # Components: Blazor pages, components, and localization resources
└── AppHost/                     # Orchestration: Aspire AppHost defining all resources
```

### Service Composition Pattern

Each **domain service** consists of:

1. **`Module.{Domain}` (Core)**: business logic, EF models, and storage
   - `Model/` - entity classes
   - `Service/` - domain services
   - `Storage/` - `DbContext` (with an `IDesignTimeDbContextFactory` for EF tooling)
   - `Migrations/` - EF Core migrations and model snapshot
   - `Mapping/` - entity → DTO mappers (extension methods like `.ToDto()`)
   - `HostingExtensions.cs` - `AddModule{Domain}()` registration

2. **`Module.{Domain}.Api`**: REST service exposing domain operations
   - Minimal APIs grouped by `MapGroup(...)`, with `static` handlers returning `TypedResults.*`
   - Dependency injection via handler parameters
   - Build writes the OpenAPI document to `api/VeloTime.Module.{Domain}.Api.json`

3. **`Module.{Domain}.Interface`**: service contracts published to consumers
   - DTOs, event messages, constants; **no implementation dependencies**

4. **`Module.{Domain}.Processor`**: event subscription service
   - `IConsumer<TEvent>` handlers (usually also `IConsumerWithContext`) via SlimMessageBus
   - May publish derived events downstream; runs as a separate hosted service/container

5. **`Module.{Domain}.Client`**: typed HTTP client
   - Generated by NSwag from the Api project (`OpenApiProjectReference`)
   - Registered by consumers with service discovery, for example `.AddFacilitiesClient().AddDefaultAccessTokenResiliency()`

6. **`Module.{Domain}.Migration`**: migration runner host
   - Runs [MigrationWorker](src/Module.Common/MigrationWorker.cs), which applies pending migrations through `MigrateAsync()` and exits

### Service Boundaries & Communication

- **Each service owns its database schema** - no cross-service DB access
- **Async messaging** via SlimMessageBus on Azure Service Bus for inter-service events
- **HTTP clients** (`.Client` packages) for synchronous calls, resolved through Aspire service discovery

**Event Flow Example:**

1. Agent (Worker Service) emits `PassingObservedV1` (with the agent's `SourcePassingId`) → SlimMessageBus
2. Timing Processor service consumes `PassingObservedV1` → deduplicates on (installation, `SourcePassingId`), persists the passing fact and enqueues `PassingRecordedV1` through the Timing outbox in one transaction
3. Statistics Processor service consumes `PassingRecordedV1` → computes aggregates in Statistics DB (see [docs/replay-safe-statistics-plan.md](docs/replay-safe-statistics-plan.md))
4. WebUI.Mud consumes services via HTTP clients (`.AddTimingClient()`, `.AddStatisticsClient()`)

### Cross-Cutting Concerns

- **`ServiceDefaults`** - OpenTelemetry, service discovery, health checks, resilience (Polly)
- **`Module.Common`** - `AddModuleStorage<T>()`, `AddModuleCache()`, `MigrationWorker<T>`, administration authorization
- **Keycloak clients** - the `VeloTime.WebUI.Keycloak` and `VeloTime.WebUI.KeycloakAdmin` namespaces are NSwag-generated in `WebUI.Mud` from `api/keycloak.yaml` and `api/keycloak-admin.yaml`
- **Instrumentation** - name new `ActivitySource`s and meters `"VeloTime.Module.{Domain}"` (Timing, Facilities, and Statistics define meters today)

## Key Naming Conventions

- **Projects**: `VeloTime.Module.{Domain}.{Layer}` (e.g., `VeloTime.Module.Timing.Api`)
- **Classes**: `{Entity}Service`, `{Event}Handler`, `{Domain}DbContext`, `{Entity}Mapper`
- **Migrations**: EF timestamp prefix plus a descriptive name, e.g. `20261019105522_AddTransponderOwnerSurrogateKey.cs`
- **DTOs**: suffix `DTO` or match the entity name (e.g., `InstallationDTO`)
- **Events**: suffix `Event` or `Command` (e.g., `PassingEvent`, `InstallationLayoutEvent`)

## Database & Migrations

- **Provider**: PostgreSQL with EF Core, snake_case naming convention
- **Connection**: every module uses the `"velotimedb"` connection, registered with `builder.AddModuleStorage<TContext>(connectionName: "velotimedb")`
- **Schemas**: each module owns a default schema (`timing`, `facility`, `statistics`, `profiles`, `notifications`, `agent`); EF history is kept in `ef.__ef_migrations_history`
- **Migrations path**: `src/Module.{Domain}/Migrations/`. The `src/Module.{Domain}.Migration/` projects only run them.
- Migration runners apply pending migrations at startup through `MigrateAsync()`; see [MigrationWorker.cs](src/Module.Common/MigrationWorker.cs).

## Agent Infrastructure

**`src/Agent/`** is a **shared infrastructure library** for building concrete timing agent implementations. Concrete agents that integrate with specific hardware timing systems live **outside this solution** and reference `VeloTime.Agent` as a dependency.

- `HostingExtensions.cs` exposes `ConfigureAgent()` — the entry point all concrete agents must call
- Manages Azure Service Bus connection, outbox pattern, OTEL, and the `VELOTIME_AGENT` identity
- `MessagingService` handles publishing `PassingObservedV1`, `InstallationLayoutEvent`, etc. to the message bus
- `AgentDbContext` provides a local outbox store to prevent duplicate event delivery
- `Handler/` contains `PauseAgentHandler` and `ResumeAgentHandler` for remote control via Service Bus

**`src/Agent.Interface/`** defines the shared contracts (events/commands) used by both agents and the processors that consume them (e.g., `PassingObservedV1`, `InstallationLayoutEvent`).

**`src/Agent.Dummy/`** is a local in-solution agent simulator used for development and testing only.

**Building a concrete agent (outside this solution):**

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.ConfigureAgent();  // registers all agent infrastructure
builder.Services.AddHostedService<MyHardwareWorker>();
var app = builder.Build();
await app.RunAsync();
```

The `VELOTIME_AGENT` environment variable **must** be set — it is injected as the `AgentId` header on every outbound message so downstream processors can correlate events to a specific installation.
