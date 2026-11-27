using System.Collections.Concurrent;
using VeloTime.Module.Facilities.Api;
using VeloTime.Module.Facilities.Interface.Messages;

namespace VeloTime.Module.Facilities.Api.Test;

/// <summary>Broker-free mapping spy; not a transactional outbox durability substitute.</summary>
public sealed class RecordingTimingAssociationPublisher : ICourseConfigEventPublisher
{
    public ConcurrentQueue<CourseConfigChangedV1> Enqueued { get; } = new();
    public Exception? Failure { get; set; }

    public Task EnqueueAsync(CourseConfigChangedV1 message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) return Task.FromException(Failure);
        Enqueued.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        Enqueued.Clear();
        Failure = null;
    }
}
