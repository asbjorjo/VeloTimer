using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Handlers;

internal class SampleCompleteHandler(
    StatisticsDbContext storage,
    IFacitiliesClient facilities,
    IMemoryCache cache,
    IMessageBus messageBus,
    Metrics metrics,
    ILogger<SampleCompleteHandler> logger
    ) : IConsumer<SampleComplete>
{
    public async Task OnHandle(SampleComplete message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle SampleComplete");

        var statsItems = await storage.Set<SimpleStatisticsItem>().Where(s => s.CoursePointEnd == message.CoursePointEnd).Include(s => s.StatisticsItem).ToListAsync(cancellationToken);

        if (statsItems.Any())
        {
            foreach (var statsItem in statsItems)
            {
                var start = await storage.Set<Sample>()
                    .Where(s => s.TransponderId == message.TransponderId && s.CoursePointStartId == message.CoursePointStart && s.TimeEnd < message.TimeStart)
                    .OrderByDescending(s => s.TimeEnd)
                    .FirstOrDefaultAsync(cancellationToken);
                if (start != null)
                {
                    StatisticsEntry entry = new()
                    {
                        TransponderId = message.TransponderId,
                        TimeStart = start.TimeStart,
                        TimeEnd = message.TimeEnd,
                        StatisticsItem = statsItem.StatisticsItem,
                        Duration = message.TimeEnd - start.TimeStart,
                        Speed = statsItem.StatisticsItem.Distance / (message.TimeEnd - start.TimeStart).TotalSeconds
                    };
                    await storage.AddAsync(entry, cancellationToken);
                }
            }
            await storage.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Updated {Count} statistics items for completed sample from {Start} to {End} covering {Distance} meters", statsItems.Count, message.TimeStart, message.TimeEnd, message.Distance);
        }

        metrics.SampleCompleted((DateTime.UtcNow - activity?.StartTimeUtc)?.TotalMilliseconds ?? 0);
    }
}
