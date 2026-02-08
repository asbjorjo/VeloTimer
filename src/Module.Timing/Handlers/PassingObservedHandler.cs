using Microsoft.Extensions.Caching.Memory;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Handlers;

public class PassingObservedHandler(IAgentService agentService, IMessageBus messageBus) : IConsumer<PassingEvent>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    private MemoryCacheEntryOptions cacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task OnHandle(PassingEvent message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Handle PassingEvent");
        
        activity?.SetTag("TransponderSystemId", message.TransponderId);
        activity?.SetTag("TransponderType", message.TransponderType);
        activity?.SetTag("LoopSystemId", message.LoopId);

        Context.Headers.TryGetValue("AgentId", out object? agentIdObj);
        string AgentId = agentIdObj?.ToString() ?? string.Empty;
        
        if (string.IsNullOrWhiteSpace(AgentId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "AgentId is missing in message properties");
            throw new InvalidOperationException("AgentId is missing in message properties");
        }

        activity?.SetTag("AgentId", AgentId);

        var passing = await agentService.RegisterPassingAsync(
            AgentId,
            message.TransponderId,
            message.LoopId,
            message.Time,
            message.TransponderType,
            cancellationToken
        );

        var sample = await agentService.RegisterSampleAsync(passing, cancellationToken);

        await messageBus.Publish(new PassingSaved(passing.Time, passing.TransponderId, passing.TimingPointId), cancellationToken: cancellationToken);

        if (sample is not null)
        {
            await messageBus.Publish(new TimingSampleComplete(
                TimeStart: sample.Start.Time,
                TimeEnd: sample.End.Time,
                TransponderId: sample.Start.TransponderId,
                TimingPointStart: sample.Start.TimingPointId,
                TimingPointEnd: sample.End.TimingPointId
                ), cancellationToken: cancellationToken);
        }
        
        
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
