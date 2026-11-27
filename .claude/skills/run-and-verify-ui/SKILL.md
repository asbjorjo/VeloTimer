---
name: run-and-verify-ui
description: Start the VeloTimer Aspire AppHost locally and verify WebUI behavior in a real browser with playwright-cli, including Keycloak sign-in. Use to confirm a UI, BFF, or end-to-end change works in the running app, not just in tests.
---

# Run the app and verify the UI

## Prerequisites

The AppHost (`src/AppHost`) starts PostgreSQL, Redis, Keycloak (port 8888, with the dev realm imported from `config/keycloak/realms/`), all APIs, processors, and migration runners, and the `frontend` WebUI. It needs Docker and these AppHost user secrets (`dotnet user-secrets list --project src/AppHost` shows which are set):

- `ConnectionStrings:MessageBus`: an Azure Service Bus connection string. There is no local emulator, and processors fail without it.
- `ConnectionStrings:VeloTimerDb`, `ConnectionStrings:VeloTimerIdentityDb`: legacy databases read by `users-migration`.
- `Parameters:KeycloakAdminUser`, `Parameters:KeycloakAdminPassword`.
- `Parameters:StatisticsProcessorSecret`, `Parameters:TimingProcessorSecret`, `Parameters:UserMigrationSecret`: these must match between Keycloak and the workers. See README [access-token audience](../../../README.md#access-token-audience-configuration).
- `ConnectionStrings:MessageBusManagement` (optional): when set, the topology job reconciles Service Bus entities first.

If any required secret is missing, **stop and ask the user to set it**. Never invent, print, or commit secret values. `OIDC_AUDIENCE` defaults to `https://development.veloti.me` in Development.

## Start and wait

```bash
aspire start --apphost src/AppHost --non-interactive   # background run
aspire wait frontend --timeout 300                     # until healthy
aspire describe frontend --format Json                 # find the frontend URL
aspire describe --format Table                         # overall resource state
aspire logs frontend                                   # when something is unhealthy
```

Migration runners (`module-*-migration`) and `bootstrap` exit after running, which is expected. A runner that exits non-zero is a failure: check `aspire logs <resource>`.

## Verify in the browser

`playwright-cli` is installed in the devcontainer and ships its own agent skill (`playwright-cli --help`). A typical check:

```bash
playwright-cli -s=velotime open "<frontend-url>"
playwright-cli -s=velotime snapshot          # get element refs
playwright-cli -s=velotime click <ref>       # e.g. the sign-in link
playwright-cli -s=velotime fill <ref> testuser
playwright-cli -s=velotime screenshot
playwright-cli -s=velotime console error     # client-side errors
playwright-cli -s=velotime requests          # failed BFF/API calls
```

The dev realm defines `testuser` (regular user) and `testadm` (`velotime-admin` realm role). Their credentials are in the dev realm import; ask the user if sign-in fails.

Check the states the change affects: loading, empty, error, and unauthorized; both `en` and `nb-NO` localization; locale-formatted times and units; and narrow and wide viewports (`resize`).

## Finish

- Stop the app with `aspire stop --apphost src/AppHost` and close the browser with `playwright-cli -s=velotime close`.
- Report which scenarios you exercised, with screenshots or snapshot evidence. If the app could not start (for example, missing secrets), say so: a UI check that was never performed is not a pass.
