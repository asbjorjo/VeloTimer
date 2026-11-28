using System.Collections.Concurrent;
using System.Reflection;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Timing.Api.Test;

public class RecordingOwnershipMessageBus : DispatchProxy
{
    public IMessageBus Bus { get; private set; } = null!;
    public ConcurrentQueue<TransponderOwnershipChanged> Enqueued { get; } = new();
    public ConcurrentQueue<PassingRecordedV1> Passings { get; } = new();
    public ConcurrentQueue<InstallationFacilityChangedV1> FacilityChanges { get; } = new();
    public ConcurrentQueue<ReportCapabilitiesCommandV1> AgentCommands { get; } = new();
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
        Passings.Clear();
        FacilityChanges.Clear();
        AgentCommands.Clear();
        FailEnqueue = false;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IMessageBus.Publish)) throw new NotSupportedException(targetMethod?.Name);
        if (args?.FirstOrDefault() is PassingRecordedV1 passing)
        {
            Passings.Enqueue(passing);
            return Task.CompletedTask;
        }
        if (args?.FirstOrDefault() is InstallationFacilityChangedV1 facilityChange)
        {
            FacilityChanges.Enqueue(facilityChange);
            return Task.CompletedTask;
        }
        if (args?.FirstOrDefault() is ReportCapabilitiesCommandV1 command)
        {
            AgentCommands.Enqueue(command);
            return Task.CompletedTask;
        }
        if (args?.FirstOrDefault() is not TransponderOwnershipChanged message) throw new NotSupportedException(targetMethod.Name);
        if (FailEnqueue) return Task.FromException(new InvalidOperationException("Simulated enqueue failure."));
        Enqueued.Enqueue(message);
        return Task.CompletedTask;
    }
}
