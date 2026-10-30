# VeloTimer Copilot Instructions

## Project Overview

VeloTimer is a microservices architecture built with .NET 10 using Aspire for distributed application orchestration. It's a competitive cycling race timing system with three domain services (Timing, Facilities, Statistics), backend agents for hardware integration, a Blazor web frontend, and event-driven processors.

**Key Technologies:**
- `.NET 10` (all projects)
- **Aspire** for microservices orchestration
- **Entity Framework Core** with PostgreSQL
- **Blazor Server** for UI (with MudBlazor components)
- **SlimMessageBus** for async messaging
- **Keycloak** for authentication (OIDC)
- **OpenTelemetry** for observability

## Working In The Repository

- Use the checked-in CI commands as the build and test baseline: `dotnet test --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov` and `dotnet build --configuration Release`. See [CI](.github/workflows/ci.yml) and [publish workflow](.github/workflows/publish.yml).
- Test projects are in `src/VeloTime.Module.Timing.Test/`, `src/VeloTime.Module.Facilities.Test/`, `src/VeloTime.Module.Statistics.Test/`, `src/Module.Timing.Api.Test/`, `src/Module.Facilities.Api.Test/`, `src/Module.Statistics.Api.Test/`, `src/VeloTime.Platform.Test/`, and `src/VeloTime.Workers.Test/`. Start with targeted checks; use [TransponderTest.cs](src/VeloTime.Module.Timing.Test/TransponderTest.cs) for a domain-test example. The README JavaScript test is `node --test src/VeloTime.Platform.Test/RegistrationDates.test.mjs`. CI test runs need `OIDC_AUDIENCE=https://build.invalid`; PostgreSQL is required for tests that exercise database integration.
- For concrete implementations, follow [TimingEndpoints.cs](src/Module.Timing.Api/Endpoints/TimingEndpoints.cs) for route grouping, [TransponderEndpoints.cs](src/Module.Timing.Api/Endpoints/TransponderEndpoints.cs) for typed results, [PassingObservedHandler.cs](src/Module.Timing.Processor/Handlers/PassingObservedHandler.cs) for message consumers, and [InstallationMapper.cs](src/Module.Timing/Mapping/InstallationMapper.cs) for entity-to-contract mapping.
- Package versions are centrally managed in [Directory.Packages.props](Directory.Packages.props); avoid adding per-project versions unless a project has an established exception.

## Specialist Agent Workflow

The shared architecture and conventions in this file remain the source of truth. The eight workspace agents are configured for selection in the agent picker and for subagent invocation: [Coordinator](.github/agents/coordinator.agent.md), [Tester](.github/agents/tester.agent.md), [.NET API Developer](.github/agents/dotnet-api-developer.agent.md), [Blazor UI Developer](.github/agents/blazor-ui-developer.agent.md), [Data & Migrations Developer](.github/agents/data-migrations-developer.agent.md), [Messaging & Workers Developer](.github/agents/messaging-workers-developer.agent.md), [Platform & Deployment Developer](.github/agents/platform-deployment-developer.agent.md), and [Code Reviewer](.github/agents/code-reviewer.agent.md). Keep agent models unpinned.

- Handle trivial tasks directly; avoid unnecessary delegation. For substantial work, the Coordinator routes to specialists, assigns non-overlapping ownership, and sequences dependencies. The normal sequence is developer → Tester → Reviewer. Send requested fixes through the Coordinator to the original owner.
- When work crosses specialist boundaries through shared contracts, the Coordinator assigns one relevant specialist to create/update and validate the contract first (or confirm an existing contract is compatible). Only then delegate dependent implementations with the agreed artifacts, semantics, compatibility decisions, and file ownership. Failed contract validation blocks affected dependents; later revisions return to the contract owner before those implementations resume. Directly selected specialists must report such prerequisites through the Coordinator rather than independently redefine shared contracts. See the [Coordinator contract-first gate](.github/agents/coordinator.agent.md#contract-first-gate).
- When selected directly or delegated, a specialist must stay within its role and must not delegate further. Return a complete report with changed files, contract decisions, checks and exact results, blockers, and the next owner. The Tester adds or updates tests, reports exact outcomes, and never changes production code to suppress a failure. The Reviewer is read-only.
- For portable Copilot SDK compatibility, do not define handoffs or `agents` YAML fields. Move manually by selecting the next agent in the picker and providing the previous agent's report. Start a fresh session or reload the workspace to discover new agents. Agent discovery and behavior depend on runtime support and higher-priority instructions.

## Git Workflow

- Start new work on a dedicated branch based on the appropriate up-to-date base branch; do not make changes directly on the default branch.
- Keep commits atomic: each commit should contain one focused, self-contained change.
- Use Conventional Commit messages in the format `<type>(<scope>): <imperative summary>`, for example `fix(timing): validate passing timestamps`.

## Architecture

### Microservices Structure

The system is organized as **independent domain services with vertical slices**:

```
src/
├── Module.Timing/              # Service: Timing domain core logic + EF models
├── Module.Timing.Api/          # Service: REST endpoints (Minimal APIs)
├── Module.Timing.Client/       # Client: HTTP client library for Timing API
├── Module.Timing.Interface/    # Contracts: Shared DTOs, messages (consumed by other services)
├── Module.Timing.Processor/    # Service: Background worker consuming events
├── Module.Timing.Migration/    # Utility: EF migrations for Timing DB schema
│
├── Module.Facilities/          # Service: Facilities domain (repeats pattern)
├── Module.Facilities.Api/
├── Module.Facilities.Client/
├── Module.Facilities.Interface/
├── Module.Facilities.Processor/
├── Module.Facilities.Migration/
│
├── Module.Statistics/          # Service: Statistics domain (repeats pattern)
├── [same as above...]
│
├── Agent/                      # Library: Shared infrastructure for concrete agent implementations (external)
├── Agent.Interface/            # Contracts: Agent event/command definitions (shared with processors)
├── Agent.Dummy/                # Dev tool: Local agent simulator for development/testing only
│
├── Bootstrap/                  # Utility: Seed data + initialization
├── ServiceDefaults/            # Shared: Aspire defaults (health checks, OTEL)
├── WebUI.Mud/                  # Service: Blazor Server frontend (ASP.NET Core host)
├── WebUI.Mud.Client/           # Components: Blazor client-side code
├── AppHost/                    # Orchestration: Aspire manifest defining all services
```

### Service Composition Pattern

Each **domain service** (e.g., Timing, Facilities, Statistics) consists of:

1. **`Module.{Domain}` (Core Service)**: Business logic, EF models, services
   - `Model/` - Entity classes (marked as `[Table("public.{entity}")]`)
   - `Service/` - Domain logic (IServices with implementations)
   - `Storage/` - `DbContext` with `DbSet<>` properties
   - `Mapping/` - Entity → DTO mappers (extension methods like `.ToDto()`)
   - `Instrumentation/` - Metrics and tracing sources
   - `HostingExtensions.cs` - `AddModule{Domain}()` registration method

2. **`Module.{Domain}.Api`**: REST service exposing domain operations
   - Minimal APIs grouped by `MapGroup("/endpoint")`
   - Endpoints use `static` methods returning `TypedResults.*` (e.g., `Ok<T>`, `NotFound`)
   - Dependency injection via method parameters
   - Service discovery-enabled via Aspire

3. **`Module.{Domain}.Interface`**: Service contracts (published to consumers)
   - DTOs, event messages, constants
   - **No implementation dependencies** — consumed by other services via `.Client` package
   - Enables loose coupling between services

4. **`Module.{Domain}.Processor`**: Event subscription service
   - Implements `IConsumer<TEvent>` handlers via `SlimMessageBus`
   - Consumes domain events from other services
   - May publish derived events downstream
   - Runs as separate hosted service/container

5. **`Module.{Domain}.Client`**: Service client library
   - Configured in consumer's `Program.cs` with service discovery
   - Example: `.AddFacilitiesClient().AddDefaultAccessTokenResiliency()`
   - Enables typed HTTP communication between services

6. **`Module.{Domain}.Migration`**: Database schema management
   - EF Core migrations for service's PostgreSQL database
   - Auto-applied at startup via `Migrate()` extension in `ServiceDefaults`

### Service Boundaries & Communication

- **Each service owns its database** - no cross-service DB access
- **Async messaging** via SlimMessageBus for inter-service events
- **HTTP clients** (`.Client` packages) for synchronous calls
- **API Gateway** pattern via Aspire service discovery

**Event Flow Example:**
1. Agent (Worker Service) emits `PassingEvent` → SlimMessageBus
2. Timing Processor service consumes `PassingEvent` → persists to Timing DB + publishes `PassingCreatedEvent`
3. Statistics Processor service consumes `PassingCreatedEvent` → computes aggregates in Statistics DB
4. WebUI.Mud consumes services via HTTP clients (`.AddTimingClient()`, `.AddStatisticsClient()`)

### Cross-Cutting Concerns

- **`ServiceDefaults`** - Shared library: OpenTelemetry, service discovery, health checks, resilience (Polly)
- **`WebUI.Keycloak`** - OIDC authentication utilities (shared by frontend)
- **Instrumentation** - Each service has `ActivitySource` named `"VeloTime.Module.{Domain}"` and meter `"VeloTime.Module.{Domain}"`

## Key Naming Conventions

- **Projects**: `VeloTime.Module.{Domain}.{Layer}` (e.g., `VeloTime.Module.Timing.Api`)
- **Classes**: `{Entity}Service`, `{Event}Handler`, `{Entity}DbContext`, `{Entity}Mapper`
- **Endpoints**: `MapGroup("/resources")`, methods: `GetById()`, `List()`, `Create()`, `Update()`
- **Migrations**: Named with date prefix: `20260222113831_timing_history_schema.cs`
- **DTOs**: Suffix `DTO` or match entity name (e.g., `InstallationDTO`)
- **Events**: Suffix `Event` or `Command` (e.g., `PassingObservedEvent`, `RegisterAgentCommand`)

## Module Initialization Pattern

**In `HostingExtensions.cs`:**
```csharp
public static IHostApplicationBuilder AddModule{Domain}(this IHostApplicationBuilder builder)
{
    var services = builder.Services;

    services.ConfigureOpenTelemetryTracerProvider(tracer =>
        tracer.AddSource("VeloTime.Module.{Domain}"));
    services.ConfigureOpenTelemetryMeterProvider(metrics =>
        metrics.AddMeter("VeloTime.Module.{Domain}"));

    builder.AddModuleStorage<{Domain}DbContext>(connectionName: "velotimedb");
    builder.AddModuleCache();
    services.AddScoped<{Domain}Service>();
    services.AddSingleton<Metrics>();

    return builder;
}

public static void MapModule{Domain}Endpoints(this IEndpointRouteBuilder app)
{
    app.MapAgentEndpoints();
    app.MapTransponderEndpoints();
    // ... other endpoint groups
}
```

## Minimal APIs Pattern

**In API project endpoints:**
```csharp
internal static void MapAgentEndpoints(this IEndpointRouteBuilder builder)
{
    var group = builder.MapGroup("/agents");
    group.MapGet("", ListAgents);
    group.MapGet("{id}", GetAgentById);
}

static async Task<Ok<IEnumerable<InstallationDTO>>> ListAgents(
    TimingDbContext storage, [FromQuery] Guid? FacilityId)
{
    // Method parameter injection handles DI
    return TypedResults.Ok(data);
}
```

## Database & Migrations

- **Provider**: PostgreSQL with EF Core
- **Connection**: `"velotimedb"` reference (resolved via service discovery)
- **History Schema**: Each module tracks row changes in `public.{table}_history`
- **Migrations Path**: `src/Module.{Domain}.Migration/`

### Running Migrations

Migration workers apply pending migrations at startup through `MigrateAsync()`; see [MigrationWorker.cs](src/Module.Common/MigrationWorker.cs).

## Agent Infrastructure

**`src/Agent/`** is a **shared infrastructure library** for building concrete timing agent implementations. Concrete agents that integrate with specific hardware timing systems live **outside this solution** and reference `VeloTime.Agent` as a dependency.

- `HostingExtensions.cs` exposes `ConfigureAgent()` — the entry point all concrete agents must call
- Manages Azure Service Bus connection, outbox pattern, OTEL, and the `VELOTIME_AGENT` identity
- `MessagingService` handles publishing `PassingEvent`, `InstallationLayoutEvent`, etc. to the message bus
- `AgentDbContext` provides a local outbox store to prevent duplicate event delivery
- `Handler/` contains `PauseAgentHandler` and `ResumeAgentHandler` for remote control via Service Bus

**`src/Agent.Interface/`** defines the shared contracts (events/commands) used by both agents and the processors that consume them (e.g., `PassingEvent`, `InstallationLayoutEvent`).

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