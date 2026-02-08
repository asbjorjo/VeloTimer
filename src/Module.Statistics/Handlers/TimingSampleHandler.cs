using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Handlers;

public class TimingSampleHandler(
    StatisticsDbContext storage,
    IFacitiliesClient facilities,
    IMessageBus messageBus,
    Metrics metrics,
    ILogger<TimingSampleHandler> logger
    ) : IConsumer<TimingSampleComplete>
{
    public async Task OnHandle(TimingSampleComplete message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle TimingSampleComplete");

        activity?.SetTag("TransponderId", message.TransponderId);

        CoursePointDistance distance = await facilities.DistanceBetweenTimingPoints(message.TimingPointStart, message.TimingPointEnd);

        Sample sample = new()
        {
            TransponderId = message.TransponderId,
            TimeStart = message.TimeStart,
            CoursePointStartId = distance.CoursePointStartId,
            CoursePointEndId = distance.CoursePointEndId,
            TimeEnd = message.TimeEnd,
            Distance = distance.Distance,
            Duration = message.TimeEnd - message.TimeStart,
            Speed = distance.Distance / (message.TimeEnd - message.TimeStart).TotalSeconds,
        };

        await storage.AddAsync(sample, cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);

        await messageBus.Publish(new SampleComplete
        (
            TransponderId: sample.TransponderId,
            TimeStart: sample.TimeStart,
            TimeEnd: sample.TimeEnd,
            CoursePointStart: sample.CoursePointStartId,
            CoursePointEnd: sample.CoursePointEndId,
            Distance: sample.Distance
        ), cancellationToken: cancellationToken);

        activity?.SetTag("SampleId", sample.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        metrics.TimingSampleProcessed((DateTime.UtcNow - activity?.StartTimeUtc)?.TotalMilliseconds ?? 0);
    }
}
