# Replay-safe statistics requirement tests

Phase 0 of [the replay-safe statistics plan](../../../docs/replay-safe-statistics-plan.md). These tests pin the
plan's requirements (§1) and acceptance criteria (§8) end to end, from agent passing to stored samples and
entries, on real PostgreSQL databases.

## How they are built

- **`IPipelineWorld`** (`PipelineWorld.cs`) is the only surface the tests use. It speaks in names (riders, loops,
  course points, items) and exposes configuration, passing delivery, draining, redelivery, manual invalidation
  and a snapshot of stored rows. Each world gets fresh Timing, Facilities and Statistics databases.
- **`CurrentPipelineWorld`** wires the code as of Phase 1: the Timing passing handler records
  `PassingObservedV1` facts and publishes `PassingRecordedV1`, which nothing in Statistics consumes yet, so no
  samples or entries are derived until Phase 2. Messages go through an in-memory FIFO bus with retries and
  dead-lettering. Configuration that cannot be expressed yet (dated layout revisions, installation assignments,
  statistics config versions, manual decision reviews) throws `NotSupportedException` naming the plan problem ID.
- **`PipelineWorlds.CreateAsync`** picks the implementation. Each phase rewires the driver (or replaces it) and
  leaves the tests alone.
- **`Velodrome`** is the scenario: a 250 m closed loop (Finish → A 100 m → B 50 m → Finish 100 m) with loops
  L1–L3, the items Lap, Opening 100 and Two laps, and riders doing sessions of laps.

Comparisons use `PipelineSnapshot.Content()` (row content without IDs, rider labels optional) across worlds or
riders, and `WithIds()` where the same world must keep the same rows and IDs.

## Pending requirements

A test that fails on the current code is marked `[RequirementFact(PendingPhase = "Phase N (...)")]` and is skipped
in CI. The phase that makes it pass removes `PendingPhase`. To see what still fails:

```sh
export VELOTIME_TEST_STATISTICS_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=..."
VELOTIME_RUN_PENDING_REQUIREMENTS=1 dotnet test src/VeloTime.Workers.Test --filter "FullyQualifiedName~ReplaySafe"
```

"Fails on v2" is the failure seen on `v2` before Phase 1. On Phase 1 every Statistics test fails because no
samples are derived.

| Test | Pending | Fails on v2 because |
|---|---|---|
| `Timing_RecordsEachPassingOnce_UnderShuffleDuplicationAndReplay` | – (passes on Phase 1) | |
| `InOrderArrival_OneSession_ProducesEveryLapAndSplit` | Phase 2 | – (passed on v2; the driver sanity check) |
| `InOrderArrival_TwoSessions_NoSampleOrLapSpansTheGap` | Phase 2 | an hour-long sample and Lap join the sessions |
| `MultiWindows_NeverSpanTwoSessions` | Phase 2 | Two laps chains the last lap of one session to the next |
| `LatePassing_P1P3P2_MatchesInOrderArrival` | Phase 2 | Timing throws "mismatched passings" (T2); laps are not rebuilt (S2) |
| `ShuffledArrival_MatchesInOrderArrival_ForEveryPermutation` | Phase 2 | every permutation differs |
| `EveryMessageDeliveredTwice_MatchesSingleDelivery` | Phase 2 | duplicate samples and invalid Multi entries (S1, S4) |
| `LateRedeliveryOfEveryConsumedMessage_ChangesNoRowOrId` | Phase 2 | every sample and entry is inserted again |
| `AgentReplayOfAFullDay_CreatesNoNewRows` | Phase 2 | Timing dedupes, Statistics duplicates (S1) |
| `ItemDefinedAfterTheData_CountsHistoryLikeAFreshRun` | Phase 2 | a new item counts only new data |
| `MissingAssociationAddedLater_...` | Phase 3 | the association change is not consumed (C4) |
| `LoopsSwappedFromX_RecomputeOnlyResultsFromX` | Phase 3 | the association change is not consumed (C4) |
| `SegmentLengthsRevisedFromX_...` | Phase 3 | layouts have no dated revisions (C1) |
| `InstallationMovedToAnotherFacilityFromX_...` | Phase 4 | installation facility is not time-bound (C2) |
| `LapThresholdTightenedFromX_InvalidatesOnlyLaterLaps` | Phase 4 | thresholds are not versioned (C3) |
| `ManualInvalidation_SurvivesReplayRedelivery...` | Phase 2 | replays duplicate the invalidated lap |
| `ManualInvalidationOnAnEntryALatePassingReplaces_IsSurfacedForReview` | Phase 2 | the merged lap is never replaced; there is no review |
