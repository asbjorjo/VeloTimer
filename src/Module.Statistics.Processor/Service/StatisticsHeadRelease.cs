using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>
/// Domain eligibility only. Stock SMB is the sole transport dispatcher.
/// Call after saving every receipt/effect, under the transponder transaction lock.
/// </summary>
public sealed class StatisticsHeadRelease(StatisticsDbContext storage, IMessageBus bus)
{
    public const string ReceiptHeader = "StatisticsReceiptId";

    public async Task ReleaseAsync(Guid transponderId, CancellationToken cancellationToken)
    {
        if (storage.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Head release requires the caller's Statistics transaction.");
        await StatisticsTransponderLock.AcquireAsync(storage.Database, transponderId, cancellationToken);
        if (storage.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Save all receipts and completions before releasing heads.");

        var sample = await storage.Set<SampleCompleteOutbox>()
            .Where(r => r.TransponderId == transponderId && r.EntriesCreatedAtUtc == null)
            .OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (sample is not null)
        {
            if (sample.SampleId == Guid.Empty || sample.DeliveredAtUtc.HasValue)
                throw new InvalidOperationException("Inconsistent/legacy pending SampleComplete receipt; reconcile before activation.");
            if (!sample.SmbEnqueuedAtUtc.HasValue)
            {
                await bus.Publish(new SampleComplete(sample.TransponderId, sample.TimeStart, sample.TimeEnd,
                        sample.CoursePointStart, sample.CoursePointEnd, sample.Distance, sample.SampleId),
                    headers: new Dictionary<string, object> { [ReceiptHeader] = sample.SampleId.ToString("D") },
                    cancellationToken: cancellationToken);
                sample.SmbEnqueuedAtUtc = DateTime.UtcNow;
            }
        }

        var entry = await storage.Set<EntryCreatedOutbox>()
            .Where(r => r.TransponderId == transponderId && r.MultiDecisionsCreatedAtUtc == null)
            .OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (entry is not null)
        {
            if (entry.EntryId == Guid.Empty || entry.MultiDecisionVersion != 1 || entry.DeliveredAtUtc.HasValue)
                throw new InvalidOperationException("Inconsistent/legacy pending EntryCreated receipt; reconcile before activation.");
            if (!entry.SmbEnqueuedAtUtc.HasValue)
            {
                var headers = new Dictionary<string, object> { [ReceiptHeader] = entry.EntryId.ToString("D") };
                if (entry.Single) headers["Single"] = true;
                await bus.Publish(new EntryCreated(entry.TransponderId, entry.TimeStart, entry.TimeEnd,
                        entry.StatisticsItemId, entry.ConfigItemId, entry.EntryId),
                    headers: headers, cancellationToken: cancellationToken);
                entry.SmbEnqueuedAtUtc = DateTime.UtcNow;
            }
        }
        await storage.SaveChangesAsync(cancellationToken);
    }
}
