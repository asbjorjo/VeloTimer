---
name: add-message-event
description: Add or change a VeloTimer SlimMessageBus message (event or command), its producer, consumer, and Azure Service Bus topic, subscription, or filter rule. Use for any new message type, new subscription, changed filter, or consumer added to an existing message.
---

# Add or change a message

Applications run with SlimMessageBus `TopologyProvisioning.Enabled = false`. Topics, subscriptions, and filter rules exist only because the `MessageBus.Topology` job creates them from a declared topology. A consumer whose subscription or rule is not declared receives nothing, and nothing errors.

Current topics and subscriptions (from [topology.json](../../../src/MessageBus.Topology/topology.json)):

| Topic | Subscriptions |
|---|---|
| `velotime-agent` | `timing` (sessions), `infrastructure` |
| `velotime-timing` | `statistics` |
| `velotime-statistics` | `statistics`, `statistics-validity` |
| `velotime-facilities` | none yet |
| `velotime-notifications` | `webui-notifications` |

## Steps

1. **Contract.** Add the message record to the producing module's `*.Interface` project (or `Agent.Interface` for agent messages). New cross-service events use a `V1` suffix (for example `StatisticsValidityThresholdsChangedV1`). Use UTC timestamps and base SI units, and include enough identity for consumers to be idempotent. Never change the meaning of an existing field. Add a new version instead.
2. **Producer.** Register `mbb.Produce<TMessage>(x => x.DefaultTopic("velotime-<domain>"))` in the producer's bus configuration (`HostingExtensions.cs` or `MessagingExtensions.cs`). Keep the existing `WithHeaderModifier` that sets `headers["MessageName"] = message.GetType().Name`, because every filter rule depends on it.
   - If the consumer needs per-key ordering, set `sbMessage.SessionId` in `WithModifier` (see `PassingSaved`, keyed by transponder) and declare the subscription with `requiresSession: true`.
   - If the event must commit with a database change, publish through the module's PostgreSQL outbox (`UseOutbox()` + `AddOutboxUsingDbContext<TDbContext>`, as in `Module.Facilities.Api/MessagingExtensions.cs`).
3. **Consumer.** Implement `IConsumer<TMessage>`, plus `IConsumerWithContext` when headers or the session are needed, following [PassingObservedHandler](../../../src/Module.Timing.Processor/Handlers/PassingObservedHandler.cs). Register it with `mbb.Consume<TMessage>(x => x.Topic(...).SubscriptionName(...).SubscriptionSqlFilter($"MessageName = '{nameof(TMessage)}'").WithConsumer<Handler>())`. Processing must be idempotent under redelivery. Throw on failure rather than acknowledge it, and never silently drop a malformed message.
4. **Topology.** In the same commit, add the subscription or rule to `src/MessageBus.Topology/topology.json` **and** to the identical `messageBusTopology.topics` defaults in `charts/velotime/values.yaml`. Every subscription needs at least one rule. A rule has exactly one of `sqlFilter` or `correlationFilter`, and constant filters such as `1=1` are rejected. `requiresSession` cannot be changed after creation, so changing it means a new subscription.
5. **Validate.**
   - `dotnet build` the producer and consumer projects.
   - `python3 -m unittest discover -s charts/tests -v`, which includes `test_chart_defaults_match_bundled_provisioner_topology`.
   - Targeted tests in `src/VeloTime.Workers.Test/` (for example `MessageBusTopologyReconcilerTests`, `*ProcessorRegistrationTests`) or the module test project. Add consumer tests for duplicate delivery, out-of-order delivery, and failure.
6. **Document.** Update [README.md](../../../README.md#agent-messaging-and-outage-recovery) or the relevant `docs/` record when delivery semantics change.

## Deployment note

The topology job runs as a Helm `pre-install,pre-upgrade` hook before the services roll out. Locally, the AppHost runs it only when `ConnectionStrings:MessageBusManagement` is configured; otherwise the local topology has to exist already.
