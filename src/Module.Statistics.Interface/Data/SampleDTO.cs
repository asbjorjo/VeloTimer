namespace VeloTime.Module.Statistics.Interface.Data;

public record SampleDTO(DateTime Time, Guid TransponderId, Guid CoursePointStartId, Guid CoursePointEndId, double Distance, TimeSpan Duration, double Speed);
public record GetSamplesRequest(DateTime? Cursor, bool? IsNextPage, int PageSize = 10);
public record GetSamplesResponse(IEnumerable<SampleDTO> Samples, DateTime? Next, DateTime? Previous, bool IsFirstPage);