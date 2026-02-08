using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

public class StatisticsService(
    StatisticsDbContext storage,
    HybridCache cache)
{
    public async Task<IEnumerable<StatisticsEntry>> ProcessCompletedAsync(Guid transponderId, DateTime startTime, DateTime endTime, Guid startPoint, Guid endPoint, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("ProcessCompletedAsync");
        activity?.SetTag("TransponderId", transponderId);
        IEnumerable<StatisticsEntry> entries = new List<StatisticsEntry>();

        var statsItems = await GetStatisticsItemConfigByEndCoursePoint(endPoint, cancellationToken);

        if (statsItems.Any())
        {
            foreach (var statsItem in statsItems)
            {
                var start = await storage.Set<Sample>()
                    .Where(s => s.TransponderId == transponderId && s.CoursePointStartId == startPoint && s.TimeEnd < startTime)
                    .OrderByDescending(s => s.TimeEnd)
                    .FirstOrDefaultAsync(cancellationToken);
                if (start != null)
                {
                    StatisticsEntry entry = new()
                    {
                        TransponderId = transponderId,
                        TimeStart = start.TimeStart,
                        TimeEnd = endTime,
                        StatisticsItem = statsItem.StatisticsItem,
                        Duration = endTime - start.TimeStart,
                        Speed = statsItem.StatisticsItem.Distance / (endTime - start.TimeStart).TotalSeconds,
                        StatisticsItemConfig = statsItem
                    };
                    entries = entries.Append(entry);
                }
            }
            await storage.AddRangeAsync(entries, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
        }
        activity?.SetTag("EntriesCreated", entries.Count());

        return entries;
    }

    public async Task<IEnumerable<StatisticsEntry>> ProcessEntryAsync(Guid transponderId, Guid statisticsItemId, Guid configId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("ProcessEntryAsync");
        IEnumerable<StatisticsEntry> entries = new List<StatisticsEntry>();
        
        var multiItems = await GetMultiStatisticsItemConfigs(configId, cancellationToken: cancellationToken);
            
        if (multiItems.Any())
        {
            var maxRepetitions = multiItems.First().Repetitions;
            var allEntries = await storage.Set<StatisticsEntry>()
                .Where(s => s.TransponderId == transponderId && s.StatisticsItemConfigId == configId)
                .OrderByDescending(s => s.TimeEnd)
                .Take(maxRepetitions)
                .ToListAsync(cancellationToken);

            foreach (var multiItem in multiItems.Where(i => i.Repetitions <= allEntries.Count)) {
                var relevantEntries = allEntries.Take(multiItem.Repetitions);
                StatisticsEntry entry = new()
                {
                    TransponderId = transponderId,
                    TimeStart = relevantEntries.Last().TimeStart,
                    TimeEnd = relevantEntries.First().TimeEnd,
                    StatisticsItem = multiItem.StatisticsItem,
                    Duration = relevantEntries.First().TimeEnd - relevantEntries.Last().TimeStart,
                    Speed = multiItem.StatisticsItem.Distance / (relevantEntries.First().TimeEnd - relevantEntries.Last().TimeStart).TotalSeconds,
                    StatisticsItemConfig = multiItem
                };
                entries = entries.Append(entry);
            }

            await storage.AddRangeAsync(entries, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
        }

        return entries;
    }

    public async Task<IEnumerable<MultiStatisticsItemConfig>> GetMultiStatisticsItemConfigs(Guid parentConfigId, CancellationToken cancellationToken = default)
    {
        var itemQuery = storage.Set<MultiStatisticsItemConfig>()
            .Where(s => s.ParentConfigId == parentConfigId)
            .Include(s => s.StatisticsItem)
            .OrderByDescending(s => s.Repetitions);

        return await cache.GetOrCreateAsync(
            $"MultiStatisticsItemConfig_{parentConfigId}",
            async cancel => await itemQuery.ToListAsync(cancellationToken: cancel),
            cancellationToken: cancellationToken
            );
    }

    public async Task<IEnumerable<SimpleStatisticsItemConfig>> GetStatisticsItemConfigByEndCoursePoint(Guid coursePointId, CancellationToken cancellationToken = default)
    {
        var itemQuery = storage.Set<SimpleStatisticsItemConfig>()
            .Where(s => s.CoursePointEnd == coursePointId)
            .Include(s => s.StatisticsItem);
        return await cache.GetOrCreateAsync(
            $"StatisticsItemConfig_End_{coursePointId}",
            async cancel => await itemQuery.ToListAsync(cancellationToken: cancel),
            cancellationToken: cancellationToken
            );
    }
}
