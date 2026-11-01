using System.Collections.Concurrent;
using System.Reflection;
using SlimMessageBus;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Timing.Api.Test;

public class RecordingOwnershipMessageBus : DispatchProxy
{
    public IMessageBus Bus { get; private set; } = null!;
    public ConcurrentQueue<TransponderOwnershipChanged> Enqueued { get; } = new();
    public bool FailEnqueue { get; set; }

    public static RecordingOwnershipMessageBus Create()
    {
        var bus = Create<IMessageBus, RecordingOwnershipMessageBus>();
        var recorder = (RecordingOwnershipMessageBus)(object)bus;
        recorder.Bus = bus;
        return recorder;
    }

    public void Reset()
    {
        Enqueued.Clear();
        FailEnqueue = false;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IMessageBus.Publish) || args?.FirstOrDefault() is not TransponderOwnershipChanged message)
            throw new NotSupportedException(targetMethod?.Name);
        if (FailEnqueue) return Task.FromException(new InvalidOperationException("Simulated enqueue failure."));
        Enqueued.Enqueue(message);
        return Task.CompletedTask;
    }
}
