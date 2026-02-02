namespace VeloTime.Module.Statistics.Interface.Messages;

public record SampleComplete(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid CoursePointStart, Guid CoursePointEnd, double Distance);
