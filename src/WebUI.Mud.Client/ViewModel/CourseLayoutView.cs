namespace VeloTime.WebUI.Mud.Client.ViewModel;

public class CourseLayoutView
{
    public required Guid Id { get; init; }
    public required IEnumerable<SegmentView> Segments { get; set; } = Enumerable.Empty<SegmentView>();
    public bool IsSelected { get; set; } = false;
}

public class SegmentView
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public required double Distance { get; set; }
    public required int Order { get; set; }
}

public class CourseLayoutDetailView
{
    public required Guid Id { get; init; }
    public required IEnumerable<SegmentDetailView> Segments { get; set; } = Enumerable.Empty<SegmentDetailView>();
}

public class SegmentDetailView
{
    public required Guid Id { get; init; }
    public required double Distance { get; set; }
    public required int Order { get; set; }
    public required CoursePointView StartPoint { get; set; }
    public required CoursePointView EndPoint { get; set; }
}

public class  CoursePointView
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public Guid TimingPoint { get; set; } = Guid.Empty;
}
