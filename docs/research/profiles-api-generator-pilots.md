# Profiles specification-first API generator pilots

## Purpose and common scope

Three isolated pilots evaluated generating ASP.NET Core server boundaries from an
authored OpenAPI contract while keeping application logic handwritten.

Each pilot:

- Covers the five existing Profiles operations: language GET/PUT, display GET/PUT,
  and supported-time-zones GET.
- Generates the full client from authored YAML using NSwag 14.7.1, without an
  API-host project dependency for generation.
- Migrates only `GET /api/profiles/me/preferences/time-zones` to a generated
  server boundary. The other four operations remain handwritten.
- Preserves authentication, catalogue JSON fields/order, unique IANA IDs, and
  no preference-store or issuer/subject lookup for the catalogue.
- Keeps generated source below `obj`, with application logic, persistence,
  identity validation, and message contracts handwritten.

Test counts reflect different coverage and base revisions, not performance or
quality rankings. The first two pilots started at `b35d2a5` (alpha.18); the third
started at `3bc7d46` (alpha.19).

## Comparison

| Aspect | NSwag custom server template | OpenAPI Generator | MinimalOpenAPI |
| --- | --- | --- | --- |
| Version | NSwag 14.7.1 | CLI 7.26.0 | NuGet 1.1.0 |
| Server style | Minimal API | MVC abstract controller | Minimal API typed handler base |
| Toolchain | .NET | Java 17+ and pinned JAR | .NET 10 Roslyn source generator |
| Custom templates | Narrow Liquid override | None; stock Mustache internally | None |
| Single-operation selection | Included operation IDs | Built-in normalizer filter | Local MSBuild/YAML projection |
| Existing server DTOs | Reused | Reused through schema mapping | Mapped to generated API DTOs |
| Application-owned glue | Template and implementation seam | Controller, MVC conventions, security transformer | Handler, authorization configuration, mapping, projection, security transformer |
| Generated writes | Not migrated or proven | Not migrated or proven | Binding incompatibility reproduced; not migrated |

## 1. NSwag with custom Liquid

Branch/worktree: `feat/profiles-openapi-pilot`.

The API build invokes `openapi2cscontroller` with a Liquid override emitting a
Minimal API registration and implementation interface. The template fixes
security and response policy for this authenticated, parameterless catalogue GET.
It is not a general-purpose Minimal API generator.

Verified results:

- Authored contract lint passed with a missing-license advisory.
- Full Profiles API suite: **189 passed, 0 failed, 0 skipped**, with PostgreSQL 17,
  before final response-metadata refinements.
- Final focused metadata test: **1 passed** after removing redundant explicit
  200 metadata and asserting exactly the 200/401 response set.
- API Release build after the fix: **0 warnings, 0 errors**.
- Full solution Release build before final metadata-only refinements:
  **0 errors, 117 warnings**. Final diff checks and narrow review passed.

Trade-off: preserves .NET, Minimal APIs, and existing DTOs, but we own the server
template. Typed `Ok` already supplies success metadata; adding explicit success
metadata originally duplicated it.

## 2. OpenAPI Generator ASP.NET Core

Branch/worktree: `feat/profiles-openapi-generator-pilot`.

Stock `aspnetcore` output generates an abstract controller, filtered by operation
ID. Only the required controller and validation-attribute source compile into
the existing host. A handwritten subclass and scoped MVC conventions preserve
behavior, operation metadata, and exact responses. A standard OpenAPI transformer
reflects actual bearer authorization.

The generated project/dependency set was not adopted: default output had a
ProblemDetails name collision and unnecessary JsonSubTypes/Newtonsoft dependencies.
Source-only integration avoided these problems. Adopting the complete generated
project unchanged is not the validated approach.

Verified results:

- Official contract validation and source-only .NET 10 compilation passed.
- **201 Profiles tests passed, 0 failed, 0 skipped**, with PostgreSQL 17.11.
- **3 workflow regression tests passed**.
- Default-path bootstrap and clean API build passed; final solution Release build
  had **0 errors, 45 warnings**.
- Two clean generations produced identical compiled sources.
- Missing Java/JAR, corrupt artifacts, and download failures failed explicitly.
- CI and reusable publishing provision Java and a checksum-verified pinned JAR.
  Actionlint/Bash syntax checks passed; final review cleared the CI bootstrap gap.

Trade-off: stock server templates and existing DTO reuse, but MVC boundaries and
Java/JAR provisioning. MVC metadata duplication needed a scoped convention.
Generated write validation/error compatibility remains unproven.

## 3. MinimalOpenAPI

Branch/worktree: `feat/profiles-minimalopenapi-pilot`.

The .NET 10 incremental source generator emits typed handler bases, DTOs, endpoint
mapping, and registration. The catalogue implementation maps the existing DTO to
a generated DTO array. Its configuration explicitly calls `RequireAuthorization()`;
the OpenAPI security declaration alone is not runtime enforcement.

No operation filter or existing CLR DTO reuse setting was found in 1.1.0. A
local MSBuild Roslyn/YamlDotNet task projects the catalogue operation from the
full authored contract under `obj`, preserving components and security. This
uses the pinned package's analyzer asset layout: upgrades require rechecking it.
The projection is application-owned glue, not native generator functionality.

Verified results:

- **205 Profiles tests passed, 0 failed, 0 skipped**, with PostgreSQL 17.11.
- Real bearer validation, actual-host metadata/OpenAPI, catalogue JSON, all five
  client operations, cancellation, and handwritten write codes are covered.
- API clean Release, design-time, publish, and full-client builds passed with
  **0 warnings/errors**; full solution Release had **0 errors, 45 warnings**.
- Two clean rebuilds produced identical projection hashes.
- Read-only review found no implementation defect; stale validation documentation
  was corrected.

**Write migration blocker:** local full-generation probes and checked-in TestServer
binding tests reproduce bare 400s before handler execution for absent/JSON-null
write bodies and some incomplete display payloads. These lose stable Profiles
problem codes. Production writes remain handwritten and pass their code tests;
the catalogue pilot does not resolve generated write binding.

Detailed evidence is in `api/specifications/profiles-minimalopenapi-gate.md`
and binding probes in
`src/Module.Profiles.Api.Test/GeneratedWriteBindingProbeTests.cs`, both in the
`feat/profiles-minimalopenapi-pilot` worktree (not the main worktree).

## Recommendation and next adoption gate

None of the pilots establishes readiness to generate every Profiles operation.

- If MVC is acceptable, **OpenAPI Generator is the preferred established,
  stock-template server candidate**, retaining NSwag for clients. Prove a
  preference PUT before broader adoption.
- If pure .NET and Minimal APIs are requirements, **MinimalOpenAPI is viable for
  the read-only slice**, but resolve nullable-body/problem-code parity before
  write migration. Include projection and DTO mapping in the maintenance cost.
- Choose **NSwag plus custom Liquid** when preserving Minimal APIs and existing
  DTOs outweighs owning the template. Expanding that template is additional
  engineering, not a proven general generator outcome.

The next gate is one generated preference PUT preserving absent/null/incomplete
body handling, stable problem codes/extensions, issuer/subject identity,
cancellation, persisted results, and exact metadata. Do not strengthen schemas
or change errors just to accommodate the generator.

## Evidence boundaries and delivery status

- Results are local recorded evidence. Full solution **builds**, not full solution
  test suites, ran. Test counts are not directly comparable across pilots.
- Solution warnings include package/compiler warnings outside Profiles. Their
  baseline provenance was not established; unrelated dependencies were not fixed.
- Production OIDC startup, remote GitHub Actions execution, and deployments were
  not validated.
- Implementations remain uncommitted in separate worktrees. Branch names identify
  those worktrees; branches alone do not contain the uncommitted pilot changes.
- This summary was moved to the main repository worktree at the user's request.
  Pilot implementations remain isolated; other sessions' files were not modified.
