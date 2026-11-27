using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service.Recompute;

namespace VeloTime.Module.Statistics.Test;

/// <summary>The pure pieces of the replay-safe recompute: identities, sessions, samples and diff-apply.</summary>
public sealed class RecomputeBuildingBlockTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Rider = Guid.NewGuid();
    private static readonly Guid Facility = Guid.NewGuid();
    private static readonly Guid Finish = Guid.NewGuid();
    private static readonly Guid Pursuit = Guid.NewGuid();
    private static readonly Guid FinishLoop = Guid.NewGuid();
    private static readonly Guid PursuitLoop = Guid.NewGuid();

    [Fact]
    public void DeterministicIds_AreStableVersion5AndDistinguishKindAndOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal(DeterministicId.Sample(a, b), DeterministicId.Sample(a, b));
        Assert.Equal(5, DeterministicId.Sample(a, b).Version);
        Assert.NotEqual(DeterministicId.Sample(a, b), DeterministicId.Sample(b, a));
        Assert.NotEqual(DeterministicId.SimpleEntry(a, a, b), DeterministicId.MultiEntry(a, a, b));
        // Pinned: a changed key format silently re-identifies every stored row.
        Assert.Equal(Guid.Parse("ac1d0c83-85b2-5e8a-adb2-387ba4cc5180"), // RFC 4122 UUIDv5, matches any standard implementation
            DeterministicId.Sample(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002")));
    }

    [Fact]
    public void Sessions_SplitOnTheGapAndNeverMixFacilities()
    {
        var elsewhere = Guid.NewGuid();
        var passings = new[]
        {
            Passing(0, FinishLoop, Finish), Passing(10, PursuitLoop, Pursuit),
            Passing(10 + 30 * 60, FinishLoop, Finish), // exactly the gap: a new session
            Passing(5, FinishLoop, Finish, elsewhere)
        };

        var sessions = SessionBuilder.Sessions(passings.Reverse(), SessionBuilder.DefaultSessionGap);

        Assert.Equal(3, sessions.Count);
        var here = sessions.Where(s => s.FacilityId == Facility).OrderBy(s => s.Start).ToList();
        Assert.Equal([2, 1], here.Select(s => s.Passings.Count));
        Assert.Single(sessions, s => s.FacilityId == elsewhere);
    }

    [Fact]
    public void Sessions_DuplicateInstantOnTheSameTimingPointKeepsTheLowestPassingId()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var session = Assert.Single(SessionBuilder.Sessions(
            [Passing(0, FinishLoop, Finish, id: high), Passing(0, FinishLoop, Finish, id: low)], SessionBuilder.DefaultSessionGap));

        Assert.Equal(low, Assert.Single(session.Passings).PassingId);
    }

    [Fact]
    public void Samples_DropZeroDurationAndUnresolvableDistancesAsRejections()
    {
        var unknown = Guid.NewGuid();
        var session = Assert.Single(SessionBuilder.Sessions([
            Passing(0, FinishLoop, Finish, id: Id(1)), Passing(10, PursuitLoop, Pursuit, id: Id(2)),
            Passing(10, Guid.NewGuid(), Finish, id: Id(3)), Passing(20, Guid.NewGuid(), unknown, id: Id(4))],
            SessionBuilder.DefaultSessionGap));
        var rejections = new List<DerivationRejection>();

        var sample = Assert.Single(SessionBuilder.Samples(session, Course(), rejections));

        Assert.Equal(DeterministicId.Sample(sample.StartPassingId, sample.EndPassingId), sample.Id);
        Assert.Equal(125, sample.Distance);
        Assert.Equal(12.5, sample.Speed);
        Assert.Equal(250, sample.ClosedLoopDistanceMetres);
        Assert.Equal(["zero-duration sample", "no compatible course layout"], rejections.Select(r => r.Reason));
    }

    [Fact]
    public void CourseHistory_ResolvesAssociationsAtTheirOwnTimeAndRejectsAmbiguity()
    {
        var loop = Guid.NewGuid();
        var course = new FacilityCourseHistory
        {
            FacilityId = Facility,
            Associations =
            [
                new() { TimingPointId = loop, CoursePointId = Finish, ValidToUtc = T0 },
                new() { TimingPointId = loop, CoursePointId = Pursuit, ValidFromUtc = T0 }
            ]
        };

        Assert.Equal(Finish, course.ResolveCoursePoint(loop, T0.AddTicks(-1)));
        Assert.Equal(Pursuit, course.ResolveCoursePoint(loop, T0));
        course.Associations.Add(new() { TimingPointId = loop, CoursePointId = Finish });
        Assert.Null(course.ResolveCoursePoint(loop, T0));
    }

    [Fact]
    public void Diff_InsertsUpdatesInPlaceAndDeletes()
    {
        var kept = Entry(0, 10);
        var moved = Entry(20, 30);
        var gone = Entry(40, 50);
        var derivedKept = Entry(0, 10, kept.Id);
        var derivedMoved = Entry(20, 31, moved.Id);
        var added = Entry(60, 70);

        var plan = RecomputeDiff.Entries([kept, moved, gone], [derivedKept, derivedMoved, added]);

        Assert.Equal([added], plan.Inserts);
        Assert.Equal([gone], plan.Deletes);
        Assert.Equal(1, plan.Updated);
        Assert.Equal(T0.AddSeconds(31), moved.TimeEnd); // Copied onto the tracked row.
        Assert.Equal(3, plan.Changed);
    }

    [Fact]
    public void Diff_RefusesToChangeAnIdentity()
    {
        var stored = Entry(0, 10);
        var derived = new StatisticsEntry
        {
            Id = stored.Id, TransponderId = Guid.NewGuid(), StatisticsItemId = ItemId, StatisticsItemConfigId = ConfigId,
            TimeStart = stored.TimeStart, TimeEnd = stored.TimeEnd, Duration = stored.Duration, Speed = stored.Speed
        };

        Assert.Throws<InvalidOperationException>(() => RecomputeDiff.Entries([stored], [derived]));
    }

    [Fact]
    public void ManualDecisions_CarryWhenUnchangedAndAreReviewedWhenChangedOrRemoved()
    {
        var userId = Guid.NewGuid();
        var unchanged = Manual(Entry(0, 10), userId);
        var changed = Manual(Entry(20, 30), userId);
        var removed = Manual(Entry(40, 50), userId);
        var stored = new[] { unchanged, changed, removed }.ToDictionary(e => e.Id);
        var derivedUnchanged = Entry(0, 10, unchanged.Id);
        var derivedChanged = Entry(20, 31, changed.Id);

        var reviews = RecomputeDiff.CarryManualDecisions(stored, [derivedUnchanged, derivedChanged], T0)
            .Concat(RecomputeDiff.RemovedManualDecisions(stored.Values, new HashSet<Guid> { unchanged.Id, changed.Id }, T0)).ToList();

        Assert.False(derivedUnchanged.IsValid);
        Assert.Equal(StatisticsEntryInvalidReason.ManuallyInvalidated, derivedUnchanged.InvalidReason);
        Assert.Equal(userId, derivedUnchanged.ManualInvalidationChangedByUserId);
        Assert.True(derivedChanged.IsValid); // The decision is lifted, not guessed onto a new measurement.
        Assert.Collection(reviews,
            r => { Assert.Equal(changed.Id, r.EntryId); Assert.Equal(ManualDecisionReviewOutcome.Changed, r.Outcome); Assert.Equal(userId, r.ChangedByUserId); },
            r => { Assert.Equal(removed.Id, r.EntryId); Assert.Equal(ManualDecisionReviewOutcome.Removed, r.Outcome); });
    }

    private static ResolvedPassing Passing(int seconds, Guid timingPoint, Guid coursePoint, Guid? facility = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Rider, timingPoint, T0.AddSeconds(seconds), facility ?? Facility, coursePoint);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static FacilityCourseHistory Course() => new()
    {
        FacilityId = Facility,
        Layouts =
        [
            new()
            {
                LayoutId = Guid.NewGuid(),
                Segments =
                [
                    new() { Order = 0, StartPointId = Finish, EndPointId = Pursuit, Length = 125 },
                    new() { Order = 1, StartPointId = Pursuit, EndPointId = Finish, Length = 125 }
                ]
            }
        ]
    };

    private static readonly Guid ConfigId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    private static StatisticsEntry Entry(int startSeconds, int endSeconds, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TransponderId = Rider,
        FacilityId = Facility,
        StatisticsItemId = ItemId,
        StatisticsItemConfigId = ConfigId,
        TimeStart = T0.AddSeconds(startSeconds),
        TimeEnd = T0.AddSeconds(endSeconds),
        Duration = TimeSpan.FromSeconds(endSeconds - startSeconds),
        Speed = 250d / (endSeconds - startSeconds)
    };

    private static StatisticsEntry Manual(StatisticsEntry entry, Guid userId)
    {
        entry.IsValid = false;
        entry.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
        entry.ManualInvalidationChangedByUserId = userId;
        entry.ManualInvalidationChangedAtUtc = T0;
        entry.ManualInvalidationNote = "Crash";
        return entry;
    }
}
