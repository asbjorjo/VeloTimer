using System.Text.Json.Serialization;

namespace VeloTime.Module.Statistics.Interface.Messages;

/// <param name="EntryId">Stable Statistics entry identity, or null for a legacy message.</param>
[method: JsonConstructor]
public record EntryCreated(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid StatisticsItemId, Guid ConfigItemId, Guid? EntryId = null)
{
    // Keep the original CLR constructor and deconstruction available to existing compiled callers.
    public EntryCreated(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid StatisticsItemId, Guid ConfigItemId)
        : this(TransponderId, TimeStart, TimeEnd, StatisticsItemId, ConfigItemId, null) { }

    public void Deconstruct(out Guid TransponderId, out DateTime TimeStart, out DateTime TimeEnd,
        out Guid StatisticsItemId, out Guid ConfigItemId) =>
        (TransponderId, TimeStart, TimeEnd, StatisticsItemId, ConfigItemId) =
        (this.TransponderId, this.TimeStart, this.TimeEnd, this.StatisticsItemId, this.ConfigItemId);
}

/// <param name="SampleId">Stable Statistics sample identity, or null for a legacy message.</param>
[method: JsonConstructor]
public record SampleComplete(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid CoursePointStart, Guid CoursePointEnd, double Distance, Guid? SampleId = null)
{
    public SampleComplete(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid CoursePointStart, Guid CoursePointEnd, double Distance)
        : this(TransponderId, TimeStart, TimeEnd, CoursePointStart, CoursePointEnd, Distance, null) { }

    public void Deconstruct(out Guid TransponderId, out DateTime TimeStart, out DateTime TimeEnd,
        out Guid CoursePointStart, out Guid CoursePointEnd, out double Distance) =>
        (TransponderId, TimeStart, TimeEnd, CoursePointStart, CoursePointEnd, Distance) =
        (this.TransponderId, this.TimeStart, this.TimeEnd, this.CoursePointStart, this.CoursePointEnd, this.Distance);
}
