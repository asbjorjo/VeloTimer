namespace VeloTime.Module.Statistics.Interface.Messages;

public record EntryCreated(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid StatisticsItemId, Guid ConfigItemId);
public record SampleComplete(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid CoursePointStart, Guid CoursePointEnd, double Distance);
