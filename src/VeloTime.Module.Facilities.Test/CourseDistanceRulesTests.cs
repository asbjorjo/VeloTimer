using VeloTime.Module.Facilities.Interface.Course;
using VeloTime.Module.Facilities.Service;

namespace VeloTime.Module.Facilities.Test;

public class CourseDistanceRulesTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    private static CourseLayoutGeometry Loop(Guid id, double ab = 100, double bc = 150, double ca = 0.75) =>
        new(id, [new(1, A, B, ab), new(2, B, C, bc), new(3, C, A, ca)]);

    [Fact]
    public void ClosedCourse_IsDirectedAndWraps()
    {
        var layout = Loop(Guid.NewGuid());
        Assert.Equal(250, CourseDistanceRules.Resolve([layout], A, C)!.Distance);
        Assert.Equal(100.75, CourseDistanceRules.Resolve([layout], C, B)!.Distance);
        var lap = CourseDistanceRules.Resolve([layout], A, A)!;
        Assert.Equal(250.75, lap.Distance);
        Assert.Equal(layout.LayoutId, lap.CourseLayoutId);
        Assert.Equal(250.75, lap.ClosedLoopDistanceMetres);
    }

    [Fact]
    public void OpenCourse_IsSymmetricAndHasNoLoopEvidence()
    {
        var open = new CourseLayoutGeometry(Guid.NewGuid(), [new(1, A, B, 100), new(2, B, C, 150)]);
        Assert.Equal(250, CourseDistanceRules.Resolve([open], C, A)!.Distance);
        Assert.Null(CourseDistanceRules.Resolve([open], A, C)!.CourseLayoutId);
    }

    [Fact]
    public void AmbiguousOrDiscontinuousLayouts_AreRejectedWithReason()
    {
        Assert.Null(CourseDistanceRules.Resolve([Loop(Guid.NewGuid()), Loop(Guid.NewGuid())], A, B, out var ambiguous));
        Assert.Equal(CourseDistanceFailure.NoUniqueLayout, ambiguous);
        Assert.Null(CourseDistanceRules.Resolve([], A, B, out var missing));
        Assert.Equal(CourseDistanceFailure.NoUniqueLayout, missing);
        var broken = new CourseLayoutGeometry(Guid.NewGuid(), [new(1, A, B, 100), new(2, C, A, 150)]);
        Assert.Null(CourseDistanceRules.Resolve([broken], A, B, out var discontinuous));
        Assert.Equal(CourseDistanceFailure.Discontinuous, discontinuous);
    }

    [Fact]
    public void IntervalMath_SubtractAndRemainderHandleUnboundedEdges()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddDays(1);
        var t3 = t1.AddDays(2);
        Assert.Equal([new Interval(null, t1), new Interval(t2, null)],
            IntervalMath.Subtract(new Interval(null, null), [new Interval(t1, t2)]));
        Assert.Empty(IntervalMath.Subtract(new Interval(t1, t3), [new Interval(null, t2), new Interval(t2, null)]));
        Assert.Equal((new Interval(null, t1), new Interval(t2, null)),
            IntervalMath.Remainder(new Interval(null, null), new Interval(t1, t2)));
        Assert.Equal(((Interval?)null, (Interval?)null), IntervalMath.Remainder(new Interval(t1, t2), new Interval(null, null)));
        Assert.False(IntervalMath.Overlaps(new Interval(null, t1), new Interval(t1, null)));
        Assert.Equal(new Interval(null, t3), IntervalMath.Hull([new Interval(null, t1), new Interval(t2, t3)]));
    }
}
