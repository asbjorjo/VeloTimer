---
name: update-api-contract
description: Change a VeloTimer HTTP API contract (endpoint, route, request/response DTO, status codes, or OpenAPI metadata) and propagate it through the generated OpenAPI document, the NSwag clients, consumers, and contract docs. Use whenever a Module.*.Api endpoint or a DTO it exposes changes.
---

# Update an API contract

The OpenAPI document is a build output of each Api project: `OpenApiDocumentsDirectory` writes `api/VeloTime.Module.{Domain}.Api.json` on every build. Each `Module.{Domain}.Client` generates its NSwag client from the Api project (`OpenApiProjectReference`). An endpoint change therefore flows: **Api code → `api/*.json` → generated client → consumers**, and all of it belongs in one commit.

## Consumers of each generated client

| Client | Consumers |
|---|---|
| `Module.Timing.Client` | `WebUI.Mud`, `Module.Statistics.Processor`, `Users.Migration` |
| `Module.Facilities.Client` | `WebUI.Mud`, `Module.Statistics`, `Module.Statistics.Processor`, `Module.Facilities.Api.Test` |
| `Module.Statistics.Client` | `WebUI.Mud`, `Users.Migration` |
| `Module.Profiles.Client` | `WebUI.Mud`, `Module.Profiles.Api.Test` |
| `Module.Notifications.Client` | `WebUI.Mud` |

Recheck with `grep -l "Module.<Domain>.Client" src/*/*.csproj` if in doubt.

## Steps

1. Read the related contract record, if one exists (`api/*-contract.md`, `docs/*-plan.md`). If another specialist consumes the contract, settle the shape first (contract-first gate).
2. Change the endpoint, following the existing route group, static handler, and `TypedResults` patterns. Declare every result type in the handler signature so OpenAPI records all status codes. Keep times in UTC and quantities in base SI units.
3. Regenerate the document:

   ```bash
   OIDC_AUDIENCE=https://build.invalid dotnet build src/Module.Timing.Api
   git diff --stat api/
   ```

   Review the `api/VeloTime.Module.Timing.Api.json` diff. A change you didn't intend (removed operation, renamed schema, changed operation ID) breaks generated clients and is a compatibility change.
4. Build every consumer of the client (table above) and fix the compile errors, for example `dotnet build src/WebUI.Mud`. Operation IDs become generated method names, so renaming one is a breaking client change.
5. Update the contract tests that read the OpenAPI document or exercise the endpoint: `FacilitiesOpenApiContractTests`, `NotificationsApiContractTests`, and `TimingEndpointTests`, or the relevant `*.Api.Test`.
6. Update `api/*-contract.md` when the change affects a documented contract.
7. Commit the endpoint, regenerated `api/*.json`, consumer fixes, tests, and docs together.

## Compatibility rules

- Additive changes (a new optional field, a new endpoint) are fine. Removing or renaming fields, routes, or operation IDs, or changing a status code, needs explicit approval and a plan for deployed consumers.
- `WebUI.Mud` BFF endpoints re-expose some APIs to the browser. Update them when an underlying shape changes.
- Never hand-edit `api/VeloTime.Module.*.Api.json`. Regenerate it. `api/keycloak*.yaml` are external specs that change only when Keycloak is upgraded.
