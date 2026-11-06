# Statistics validity-threshold messaging rollout

## Producer registration / API owner

`builder.AddStatisticsValidityThresholdsMessaging()` in
`VeloTime.Module.Statistics.Api.MessagingExtensions` is called by API `Program.cs`
after `AddModuleStatistics()`. The existing `GetDocument.Insider` guard excludes
messaging during OpenAPI extraction. Registration fails explicitly if
`ConnectionStrings:MessageBus` is missing/blank, including Development.

This helper registers only the producer, JSON serializer, Statistics EF outbox
and explicit hosted bus startup. **The API owner must still register and implement
the scoped guarded event publisher and transactional threshold saves.** Require
nonempty EventId/item/config IDs (config may be null), the same scoped
`StatisticsDbContext`, and an active PostgreSQL transaction for definition saves,
durable job scheduling and `IMessageBus.Publish`. Commit them together and invalidate
caches only after commit. No direct-send or save-then-publish fallback is provided.
An enqueue is not broker receipt or completed recalculation.

## Exact consumer transport / worker owner

| Setting | Value |
|---|---|
| Contract | `StatisticsValidityThresholdsChangedV1` |
| Topic | `velotime-statistics-test` (existing Statistics topic) |
| Dedicated subscription | `statistics-validity` |
| Rule name (externally managed) | `StatisticsValidityThresholdsChangedV1` |
| SQL filter | `MessageName = 'StatisticsValidityThresholdsChangedV1'` |
| Application header | `MessageName`: `StatisticsValidityThresholdsChangedV1` |
| Message ID | `EventId.ToString()` |
| Session ID | `StatisticsItemId.ToString()` (also for configuration changes) |
| Subscription | `RequiresSession = false`, durable, not auto-deleted |
| Consumer registration | `.Topic("velotime-statistics-test").SubscriptionName("statistics-validity").Instances(1)`; **no** `.EnableSession()` |

The worker owner owns processor registration/handlers and explicit startup failure
propagation. Do not add outbox to its existing producers: their publishing
transactions have not been established. Threshold events wake durable local pending
jobs; retries/duplicates must not reset generation/checkpoints or replay old bounds.
This matches worker commit `ed4bdd73`. The producer retains the item SessionId,
but the non-session consumer does not provide per-item session serialization.
The worker's database coordination/checkpoints must guarantee convergence; neither
`Instances(1)` nor producer SessionId guarantees concurrent-host/commit ordering.

## Deployment prerequisites (external approval required)

There are no checked-in broker provisioning scripts/templates; the provisioning
research document is a proposal, not an executable provisioning mechanism. No new
provisioning service/Helm hook is introduced here. The API disables runtime topology
provisioning and cannot verify subscriptions with its send-only credentials.

Before enabling threshold saves/publication:

1. Preserve the existing topic. Precreate the dedicated **non-session** subscription
   and its exact rule above; remove `$Default`/other catch-all rules from that
   subscription. Rules are ORed, so adding a precise rule without removing catch-all
   rules does not isolate delivery.
2. Restrict the existing `velotime-statistics-test` / `statistics` subscription's
   **complete** filter set to `MessageName IN ('EntryCreated', 'SampleComplete')`.
   Preserve sessions and both existing contracts; remove/replace any broad rules
   which would deliver threshold events to incompatible consumers. Review any other
   deployed Statistics-topic subscriptions for the same compatibility issue.
   Coordinate existing processor runtime topology provisioning so it does not
   reintroduce broad rules. Changing a deployed subscription's session mode is not
   part of this rollout.
3. Supply existing secret-backed `ConnectionStrings:MessageBus` configuration.
   Aspire now forwards the existing `MessageBus` reference to the Statistics API.
   Helm emits `ConnectionStrings__MessageBus` from
   `module-statistics-api.secrets.MessageBus`, or the deployment uses
   `module-statistics-api.existingSecret` containing that key. The processor already
   receives its MessageBus secret/reference. Never put actual credentials in source
   or command-line Helm values. Authorize API Send on the Statistics topic and
   processor Listen on the dedicated subscription, using existing authentication
   mechanisms; topology Manage rights belong to the approved external provisioner.
   No OIDC clients, policies or domain authorization are changed.
4. Apply the Statistics feature migration via the existing migration release gate.
   The API role needs Statistics outbox initialization DDL permissions or approved
   preprovisioning. The EF migration runner does not initialize `statistics.smb_outbox`.
   Explicit `IMasterMessageBus.Start()` propagates outbox initialization errors to
   host startup. A broker outage leaves committed events durable; publication retries
   indefinitely, without requiring live broker provisioning at API startup.

No cloud resources were contacted or changed. Topology/authentication provisioning
and verification remain rollout blockers until externally approved and completed.

## Operational checks / Tester handoff

Use an isolated PostgreSQL database and controlled sender (no Azure contact) to test
real API definition/job/outbox atomic rollback/commit, missing transaction rejection,
initialization DDL failure causing host startup failure, and broker outage/restart
leaving committed work durable. The API endpoint factory deliberately removes bus
startup for its SQLite tests; these tests do not prove PostgreSQL outbox behavior.
Verify the real producer sets the exact header/IDs above and routes only to the
dedicated consumer; verify existing EntryCreated/SampleComplete consumers still work.
Perform any live topology tests only after separate approval.

Outbox cleanup is disabled; retained pending/dispatched rows grow until a retention/
replay policy is approved. Monitor pending age, attempts, errors and database disk
growth. A poison oldest message can block sequential dispatch. At-least-once delivery
requires idempotent worker behavior; there is no automatic retained-row replay tool.
