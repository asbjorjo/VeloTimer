using Microsoft.EntityFrameworkCore;
using Npgsql;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Handlers;

public class TimingSampleHandler(
    StatisticsDbContext storage,
    IFacilitiesClient facilities,
    Metrics metrics,
    ILogger<TimingSampleHandler> logger) : IConsumer<TimingSampleComplete>
{
    public async Task OnHandle(TimingSampleComplete message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle TimingSampleComplete");
        var start = Normalize(message.TimeStart);
        var end = Normalize(message.TimeEnd);
        var existing = await Find(message, start, end, cancellationToken);
        if (existing is not null) return;

        var distance = await facilities.Distance2Async(message.TimingPointStart, message.TimingPointEnd,
            startAt: new DateTimeOffset(start), endAt: new DateTimeOffset(end), cancellationToken: cancellationToken);
        var duration = end - start;
        var sample = new Sample
        {
            Id = Guid.NewGuid(), TransponderId = message.TransponderId,
            TimingPointStartId = message.TimingPointStart, TimingPointEndId = message.TimingPointEnd,
            TimeStart = start, TimeEnd = end,
            CoursePointStartId = distance.CoursePointStartId, CoursePointEndId = distance.CoursePointEndId,
            Distance = distance.Distance, CourseLayoutId = distance.CourseLayoutId,
            ClosedLoopDistanceMetres = distance.ClosedLoopDistanceMetres, Duration = duration,
            Speed = distance.Distance / duration.TotalSeconds, FacilityId = distance.FacilityId
        };
        var outgoing = new SampleCompleteOutbox
        {
            SampleId = sample.Id, TransponderId = sample.TransponderId, TimeStart = start, TimeEnd = end,
            CoursePointStart = sample.CoursePointStartId, CoursePointEnd = sample.CoursePointEndId,
            Distance = sample.Distance
        };

        var duplicate = false;
        await using var tx = await storage.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            storage.Add(sample);
            storage.Add(outgoing);
            await storage.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_sample_timing_identity" })
        {
            await tx.RollbackAsync(cancellationToken);
            await tx.DisposeAsync();
            storage.Entry(sample).State = EntityState.Detached;
            storage.Entry(outgoing).State = EntityState.Detached;
            duplicate = true;
        }
        if (duplicate)
        {
            if (await Find(message, start, end, cancellationToken) is null)
                throw new InvalidOperationException("A duplicate sample was reported but the winning row could not be reloaded.");
            logger.LogInformation("Concurrent Timing sample replay for transponder {TransponderId}", message.TransponderId);
            return;
        }
        activity?.SetTag("SampleId", sample.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        metrics.TimingSampleProcessed((DateTime.UtcNow - activity?.StartTimeUtc)?.TotalMilliseconds ?? 0);
    }

    private Task<Sample?> Find(TimingSampleComplete m, DateTime start, DateTime end, CancellationToken ct) =>
        storage.Set<Sample>().AsNoTracking().SingleOrDefaultAsync(s =>
            s.TransponderId == m.TransponderId && s.TimingPointStartId == m.TimingPointStart &&
            s.TimingPointEndId == m.TimingPointEnd && s.TimeStart == start && s.TimeEnd == end, ct);

    private static DateTime Normalize(DateTime time) => StatisticsStoragePrecision.Timestamp(
        time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
