global using static VeloTime.Workers.Test.StatisticsDomainTestBus;
using System.Reflection;
using SlimMessageBus;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

// Domain-only fixtures do not run a transport. Production SMB/transaction/delivery
// behavior is exercised separately by StatisticsSmbProductionTests.
internal static class StatisticsDomainTestBus
{
    public static StatisticsHeadRelease DomainHeads(StatisticsDbContext db) =>
        new(db, DispatchProxy.Create<IMessageBus, DomainBus>());

    public class DomainBus : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IMessageBus.Publish) ? Task.CompletedTask : throw new NotSupportedException(method?.Name);
    }

    // These adapters intentionally test domain behavior, not broker envelopes.
    // Real production consumers and their mandatory context are covered by the SMB suite.
    public sealed class DomainSampleConsumer(StatisticsService statistics, StatisticsOwnershipService ownership,
        Metrics metrics, ILogger<SampleCompleteHandler> logger)
    {
        public async Task OnHandle(SampleComplete message, CancellationToken ct)
        {
            _ = (metrics, logger);
            await ownership.CreateEntriesFromSourceAsync(message.TransponderId,
                (id, cancel) => statistics.ProcessCompletedAsync(message.TransponderId, message.TimeStart,
                    message.TimeEnd, message.CoursePointStart, message.CoursePointEnd, cancel, id, message.Distance),
                ct, enqueueSingle: true, completion: message);
        }
    }

    public sealed class DomainEntryConsumer(StatisticsService statistics, StatisticsOwnershipService ownership,
        ILogger<EntryCreatedHandler> logger)
    {
        public async Task OnHandle(EntryCreated message, CancellationToken ct)
        {
            _ = logger;
            await ownership.CreateEntriesFromSourceAsync(message.TransponderId,
                (id, cancel) => statistics.ProcessEntryAsync(message.TransponderId, message.StatisticsItemId,
                    message.ConfigItemId, message.TimeStart, message.TimeEnd, cancel, id),
                ct, enqueueSingle: false, parent: message);
        }
    }
}
