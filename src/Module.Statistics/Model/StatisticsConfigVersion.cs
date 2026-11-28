using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// Every result-affecting value of one statistics configuration over [<see cref="ValidFromUtc"/>,
/// <see cref="ValidToUtc"/>) (plan §4.2, C3): its item's distance, distance mode and duration bounds, its own
/// bounds, its course points and required checkpoints (Simple), or its parent and repetitions (Multi). Null
/// bounds are unbounded and a configuration's versions never overlap. The recompute worker derives an entry
/// with the version that holds at the entry's end time; nothing before a configuration's first version counts.
///
/// <para>Versions are never edited in place, except to close one at an effective instant. The current values on
/// <see cref="StatisticsItem"/> and <see cref="StatisticsItemConfig"/> are what administrators edit and listings
/// show; they always equal this configuration's open-ended version. See
/// <see cref="Service.StatisticsConfigVersioning"/>.</para>
/// </summary>
public class StatisticsConfigVersion
{
    public Guid Id { get; init; }
    public Guid StatisticsItemConfigId { get; init; }
    public StatisticsItemConfig StatisticsItemConfig { get; init; } = null!;
    public Guid StatisticsItemId { get; init; }
    public DateTime? ValidFromUtc { get; init; }
    public DateTime? ValidToUtc { get; set; }
    public DateTime CreatedAtUtc { get; init; }

    public float Distance { get; init; }
    public StatisticsDistanceMode DistanceMode { get; init; }
    public TimeSpan? ItemMinDuration { get; init; }
    public TimeSpan? ItemMaxDuration { get; init; }
    public TimeSpan? MinDuration { get; init; }
    public TimeSpan? MaxDuration { get; init; }

    /// <summary>Simple only; Guid.Empty for Multi.</summary>
    public Guid CoursePointStart { get; init; }
    /// <summary>Simple only; Guid.Empty for Multi.</summary>
    public Guid CoursePointEnd { get; init; }
    public Guid? RequiredCourseLayoutId { get; init; }
    public Guid[] RequiredCoursePointIds { get; init; } = [];

    /// <summary>Multi only.</summary>
    public Guid? ParentConfigId { get; init; }
    /// <summary>Multi only.</summary>
    public int? Repetitions { get; init; }

    public bool Contains(DateTime instant) =>
        (ValidFromUtc is not { } from || from <= instant) && (ValidToUtc is not { } to || instant < to);

    /// <summary>True when the version covers any instant in [from, to] (null bounds are unbounded).</summary>
    public bool Overlaps(DateTime? from, DateTime? to) =>
        (from is not { } start || ValidToUtc is not { } end || end > start) &&
        (to is not { } finish || ValidFromUtc is not { } begin || begin <= finish);
}
