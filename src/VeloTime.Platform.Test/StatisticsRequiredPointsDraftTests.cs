using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

public sealed class StatisticsRequiredPointsDraftTests
{
    [Theory]
    [InlineData(false, 0, 3, new[] { 1, 2 })]
    [InlineData(false, 1, 2, new int[0])]
    [InlineData(true, 3, 2, new[] { 0, 1 })]
    [InlineData(true, 2, 2, new[] { 3, 0, 1 })]
    public void PathUsesSegmentOrderAndForwardWrap(bool closed, int start, int end, int[] expected)
    {
        var layout = Layout(closed);
        layout.Segments = layout.Segments.Reverse(); // Enumeration is not course order.
        var path = StatisticsRequiredPointsDraft.IntermediatePath(layout, Id(start), Id(end));
        Assert.NotNull(path);
        Assert.Equal(expected.Select(Id), path.Select(point => point.Id));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    [InlineData(0, 9)]
    public void OpenReversedEqualOrMissingEndpointsAreNotSelectable(int start, int end) =>
        Assert.Null(StatisticsRequiredPointsDraft.IntermediatePath(Layout(), Id(start), Id(end)));

    [Theory]
    [InlineData("duplicate-order")]
    [InlineData("disconnected")]
    [InlineData("repeated-point")]
    [InlineData("empty-id")]
    [InlineData("empty")]
    public void AmbiguousGeometryIsNotSelectable(string kind)
    {
        var layout = Layout();
        var segments = layout.Segments.ToArray();
        switch (kind)
        {
            case "duplicate-order": segments[1].Order = segments[0].Order; break;
            case "disconnected": segments[1].StartPoint = Point(9); break;
            case "repeated-point": segments[1].EndPoint = Point(0); segments[2].StartPoint = Point(0); break;
            case "empty-id": segments[0].StartPoint = new() { Id = Guid.Empty, Name = "Invalid" }; break;
            case "empty": layout.Segments = []; break;
        }
        Assert.Null(StatisticsRequiredPointsDraft.IntermediatePath(layout, Id(0), Id(3)));
    }

    [Fact]
    public void SelectionIsDeterministicAndClearIsExplicit()
    {
        var layout = Layout();
        var path = StatisticsRequiredPointsDraft.IntermediatePath(layout, Id(0), Id(3))!;
        var draft = new StatisticsRequiredPointsDraft();
        draft.Toggle(Id(2), path);
        draft.Toggle(Id(1), path);
        Assert.Equal(new[] { Id(1), Id(2) }, draft.Replacement(layout.Id)!.CoursePointIds);
        draft.Toggle(Id(0), path); // Endpoints and off-path points cannot be added.
        Assert.Equal(new[] { Id(1), Id(2) }, draft.Selected);
        draft.Toggle(Id(1), path);
        Assert.Equal(new[] { Id(2) }, draft.Selected);
        var saved = new StatisticsRequiredPointsDraft(new(layout.Id, [Id(1), Id(2)]));
        Assert.Null(saved.Replacement(layout.Id));
        saved.Clear();
        var clear = Assert.IsType<StatisticsRequiredCoursePoints>(saved.Replacement(layout.Id));
        Assert.Null(clear.CourseLayoutId);
        Assert.Empty(clear.CoursePointIds!);
    }

    [Fact]
    public void LoadingAndPathChangesNeverClearOrReinterpretSnapshot()
    {
        var layout = Layout();
        var ids = new[] { Id(1), Id(2) };
        var draft = new StatisticsRequiredPointsDraft(new(layout.Id, ids));
        ids[0] = Id(9); // The draft owns a copy, not the cached transport array.
        Assert.Null(draft.Replacement(layout.Id));
        Assert.False(draft.MatchesPath(layout.Id, null));
        var shortened = StatisticsRequiredPointsDraft.IntermediatePath(layout, Id(1), Id(3))!;
        Assert.False(draft.MatchesPath(layout.Id, shortened));
        Assert.Equal(new[] { Id(1), Id(2) }, draft.Selected);
        Assert.Null(draft.Replacement(layout.Id));
        Assert.NotNull(draft.Replacement(Id(9))); // Layout changes are structural even with the same points.
        draft.Toggle(Id(2), shortened);
        Assert.Equal(new[] { Id(1) }, draft.Selected); // Unavailable points are not silently removed.
    }

    internal static Guid Id(int index) => new(index + 1, 0, 0, new byte[8]);
    private static CoursePointView Point(int index) => new() { Id = Id(index), Name = $"Point {index}" };
    internal static CourseLayoutDetailView Layout(bool closed = false, Guid? id = null)
    {
        var points = Enumerable.Range(0, 4).Select(Point).ToArray();
        return new()
        {
            Id = id ?? Id(100),
            Segments = Enumerable.Range(0, closed ? 4 : 3).Select(index => new SegmentDetailView
            {
                Id = Id(200 + index), Distance = 100, Order = index,
                StartPoint = points[index], EndPoint = points[(index + 1) % points.Length]
            }).ToArray()
        };
    }
}
