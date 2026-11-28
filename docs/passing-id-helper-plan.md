# Plan: deterministic UUIDv5 passing ID helper for agents (issue #141)

Issue: https://github.com/asbjorjo/VeloTimer/issues/141
Base: `v2` at `06f75d8` (2.1.0-alpha.50, after #138). Branch from and target `v2`.

## Problem

Since #138, `VeloTime.Agent.Model.Passing.Id` is a required non-empty `Guid` that becomes
`PassingObservedV1.SourcePassingId`. Timing (`Module.Timing/Service/AgentService.RegisterAsync`) treats:

- same `(installation, SourcePassingId)` with the same payload as a **Duplicate** (fine),
- same transponder + time + timing point under a **different** ID as a **`DuplicateObservation` conflict**.

So an agent that mints a new ID on resend or replay produces conflicts. The agent's own local store
(`MessagingService.PreparePassingsAsync`, insert-if-absent keyed on `Id`) has the same dependency: only a
deterministic ID makes its local dedupe work. P3 and X2 (external repos consuming the `VeloTime.Agent`
NuGet package) each need a stable UUID and should not each invent one.

## Proposed API

New file `src/Agent/Identity/PassingId.cs`, namespace `VeloTime.Agent.Identity`, public static class:

```csharp
public static class PassingId
{
    /// Fixed library namespace. Never change it: every replay would become a Timing conflict.
    public static readonly Guid Namespace = new("<hard-coded random v4, generated once>");

    public static Guid Create(string agentType, ReadOnlySpan<byte> identity);
    public static Guid Create(string agentType, string identity);                  // UTF-8, no normalisation
    public static Guid Create(string agentType, params ReadOnlySpan<string> parts); // length-prefixed parts
}
```

### Derivation (two-level UUIDv5, RFC 9562 §5.5)

1. `agentNamespace = UUIDv5(Namespace, UTF8(agentType))`, cached per agent type.
2. `id = UUIDv5(agentNamespace, identity)`.

This gives "one namespace per agent type" without a hard-coded list of agent types in the library, and
avoids any separator ambiguity between the agent type and the identity bytes.

- SHA-1 via `SHA1.HashData` over `namespaceBytes ‖ name`, take the first 16 bytes, set version nibble to 5
  and variant bits to `10`.
- **Byte order pitfall:** .NET `Guid` is mixed-endian. Use `Guid.TryWriteBytes(span, bigEndian: true, ...)`
  for the namespace and `new Guid(bytes, bigEndian: true)` for the result. The RFC test vector below
  catches mistakes here.
- Result is never `Guid.Empty`, because the version and variant bits are set.
- Stack-allocate for small inputs, rent from `ArrayPool` above a threshold. Agents hash at passing rate,
  so no per-call allocation beyond what is needed.

### Validation

- `agentType`: throw `ArgumentException` on null, empty or whitespace. Compared exactly (case-sensitive,
  no trimming). Doc comment says to use a constant such as `"P3"` / `"X2"` and never rename it.
- `identity`: throw on empty input (an empty identity would map every passing to one ID).
- `parts` overload: each part is encoded as a 4-byte big-endian UTF-8 length followed by its bytes, so
  `("ab","c")` and `("a","bc")` differ. Rejects zero parts and null parts; empty strings are allowed.

### Doc comment contract (what goes into `identity`)

- **Do** include a stable decoder or source identity (decoder serial, X2 server ID) plus the decoder's own
  passing identity (decoder passing number, SDK passing ID) or the canonical passing fields.
- **Do not** include hostname, IP or endpoint, so moving the agent does not change IDs.
- **Do not** include receive time or anything from the agent's clock or process (restart counters, random
  values).
- Same physical passing ⇒ same bytes, on every resend and replay, across agent restarts and versions.

## Tests

New `src/VeloTime.Workers.Test/PassingIdTests.cs` (the project already references `VeloTime.Agent`; pure
unit tests, no PostgreSQL):

1. **Algorithm check:** a private `UuidV5(ns, name)` path, exposed `internal` with `InternalsVisibleTo`,
   reproduces RFC 9562 Appendix A.4: DNS namespace `6ba7b810-9dad-11d1-80b4-00c04fd430c8` +
   `"www.example.com"` → `2ed6657d-e927-568b-95e1-2665a8aea6a2`.
2. **Golden vectors:** pin `PassingId.Namespace`, and 4–6 fixed `(agentType, identity)` → UUID literals
   covering the byte, string and parts overloads, including a non-ASCII string. A comment says that
   changing any of these is a breaking change for every deployed agent.
3. Version nibble is 5 and variant is RFC (`(b[8] & 0xC0) == 0x80`); never `Guid.Empty`.
4. Determinism and separation: the same input gives the same ID; a different agent type with the same
   identity gives a different ID; the parts overload does not collide on re-split boundaries.
5. Validation throws for empty agent type, empty identity and zero parts.
6. Optional: the string overload equals the byte overload over `Encoding.UTF8.GetBytes`.

## Other changes

- **Dummy agent** (`src/Agent.Dummy/DummyWorker.cs`): switch from `Guid.NewGuid()` to
  `PassingId.Create("Dummy", transponder, loop, time.Ticks.ToString())`, so it demonstrates the intended
  usage. Its redelivery of `previous` keeps exercising Timing's duplicate path. Small change, same PR.
- **Docs:** update the `Passing.Id` XML doc to point at `PassingId.Create`. In
  `docs/replay-safe-statistics-plan.md` (line ~69, "`SourcePassingId` is the agent's existing local passing
  ID"), note that agents derive it with `PassingId.Create`.
- **Release:** commit as `feat(agent): add deterministic UUIDv5 passing ID helper`. No breaking-change
  marker (project rule until v2 merges to master). Versionize then publishes it in the next
  `2.1.0-alpha.N` package.

## Store the hardware passing ID on the agent passing

Requested in the thread on 2026-11-27. Add a column to the agent's local `agent.passing` table for the
passing ID the hardware or SDK reports (P3 decoder passing number, X2 SDK `passing.ID`). The UUID stays the
key and the value sent to Timing. The raw ID is kept next to it so you can trace a passing back to the
decoder, debug replays, and re-derive the UUID.

- **Model** (`src/Agent/Model/Passing.cs`): add `public string? HardwarePassingId { get; init; }`. Use a
  string so P3 numbers, X2 64-bit IDs and future formats all fit unchanged. It is nullable because not
  every source has one (the dummy agent does not, and some decoders don't report one). Doc comment: the
  raw ID as reported by the source; not unique across decoders or counter resets; not the passing key.
- **Mapping** (`AgentDbContext`): no extra config is needed. Snake case gives `hardware_passing_id text
  NULL`. No unique constraint, because decoder counters reset and IDs repeat across decoders. A plain
  index is not needed until something queries it.
- **Migration** (`src/Agent/Migrations`): `AddHardwarePassingId`, made with `dotnet ef migrations add`
  against `AgentDbContextFactory`. It adds the nullable column; existing rows stay null, with no backfill
  (alpha policy).
- **Hand-written insert** (`MessagingService.InsertPassingsIfAbsentAsync`): add the column to `columns`
  and `values`. **Pitfall:** the generic parameter branch sets `parameter.Value = values[i]`, so a null
  must become `DBNull.Value`. Update the "8 parameters per passing" comment to 9; `PassingInsertChunkSize
  = 1000` stays well under the PostgreSQL limit (9,000 < 65,535).
- **Contract:** not sent to Timing. `PassingObservedV1` is unchanged, so the change stays inside the agent.
  If Timing should show it later, add it as an optional message field in a separate change.
- **Helper link:** agents should usually include the same value in the `PassingId.Create` identity, for
  example `PassingId.Create("X2", serverId, passing.HardwarePassingId!)`. The doc comment on
  `PassingId.Create` points at this.
- **Tests** (`src/VeloTime.Workers.Test/AgentOutboxTests.cs`, PostgreSQL): a passing with a
  `HardwarePassingId` round-trips through `SendEventsAsync` into `agent.passing`; a null value inserts
  fine; a re-send with the same UUID stays a single row and keeps the first stored value.
- **Commit:** a separate atomic commit in the same PR, `feat(agent): store hardware passing ID on agent
  passings`.

## Follow-up in the agent repos (out of scope here)

- **P3:** replace the SHA-256 hex `P3EventMapper.CreateId` with
  `PassingId.Create("P3", decoderSourceId, <canonical fields…>)`, using the parts overload rather than
  hashing twice.
- **X2:** `PassingId.Create("X2", x2ServerId, passing.ID.ToString())`.
- Both agents also set `HardwarePassingId` (P3: the decoder passing number; X2: `passing.ID.ToString()`).
- Each agent's tests pin only which identity parts go in.
- **One-time ID change:** after an agent upgrades, its IDs differ from those it sent before. A replay of
  pre-upgrade history would then raise `DuplicateObservation` conflicts. Following the alpha policy, reset
  the agent's local passing table and the affected Timing passings or derived data instead of writing an
  ID-mapping shim. Confirm before deleting data in a shared environment.

## Overlap with the replay-safe statistics refactor

- No code overlap with Phase 2 (Statistics). This touches only `src/Agent` (including a new agent-DB migration),
  `src/Agent.Dummy` and agent tests.
- It strengthens a Phase 1 assumption: "replaying an agent's full history creates zero new rows" only
  holds if agent IDs are deterministic. This helper is what makes that true for real agents.
- `ReplaySafe/` requirement tests build `PassingObservedV1` directly. Changing them is not required. They
  could optionally use the helper for realism, but leave them alone to avoid colliding with Phase 2 edits.

## Confirmed decisions (owner, 2026-11-27)

1. **Namespace scheme:** two-level UUIDv5 (library namespace, then a per-agent-type namespace).
2. **Parts overload:** included (length-prefixed parts).
3. **Dummy agent:** switched to `PassingId.Create` in the same PR.
4. **Hardware passing ID:** a nullable `HardwarePassingId` text column on `agent.passing`, kept on the
   agent only and not sent to Timing.

Estimated size: about 150 lines of code plus a migration, and about 160 lines of tests, in one PR with
two commits.
