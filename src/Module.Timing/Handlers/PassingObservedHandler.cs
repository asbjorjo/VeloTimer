using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Handlers;

public class PassingObservedHandler(InstallationService installationService, TimingDbContext storage, IMessageBus messageBus, IMemoryCache cache, Metrics metrics) : IConsumer<PassingEvent>, IConsumerWithContext
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

        string lastPassingKey = $"LastPassing_{AgentId}_{message.TransponderType}_{message.TransponderId}";
        Passing? last = cache.Get<Passing>(lastPassingKey) ?? await storage.Set<Passing>()
            .AsNoTracking()
            .OrderByDescending(p => p.Time)
            .FirstOrDefaultAsync(p => 
                p.Transponder.SystemId == message.TransponderId 
                && p.Transponder.System == TimingSystem.MyLaps_X2
                && p.TimingPoint.Installation.AgentId == AgentId);

        string cacheKeyLoop = $"Loop_{AgentId}_{message.LoopId}";

        if (cache.TryGetValue(cacheKeyLoop, out TimingPoint? timingPoint))
        {
            storage.Attach(timingPoint!);
            metrics.TimingLoopCacheHit();
        }
        else {
            timingPoint = await installationService.FindOrCreateTimingPoint(AgentId, message.LoopId, cancellationToken: cancellationToken);
            metrics.TimingLoopCacheHit(false);
        }

        string cacheKeyTransponder = $"Transponder_{message.TransponderType}_{message.TransponderId}";
        if (cache.TryGetValue(cacheKeyTransponder, out Transponder? transponder))
        {
            storage.Attach(transponder!);
        } else
        {
            transponder = await storage.Set<Transponder>()
                .SingleOrDefaultAsync(t => t.SystemId == message.TransponderId && t.System == TimingSystem.MyLaps_X2 , cancellationToken: cancellationToken);
            if (transponder == null)
            {
                transponder = new MylapsX2Transponder(message.TransponderId);
                await storage.AddAsync(transponder);
            }
        }

        if (await storage.Set<Passing>()
            .AsNoTracking()
            .AnyAsync(p => p.Time == message.Time && p.TimingPointId == timingPoint!.Id && p.TransponderId == transponder!.Id, cancellationToken: cancellationToken))
        {
            activity?.SetTag("Duplicate", true);
            activity?.SetStatus(ActivityStatusCode.Ok, "Duplicate passing detected, ignoring");
            metrics.PassingProcessed(AgentId, activity, true);
            return;
        }

        Passing passing = new() { Time = message.Time, TimingPoint = timingPoint!, Transponder = transponder! };

        await storage.AddAsync(passing, cancellationToken: cancellationToken);

        TimingSampleComplete? sampleComplete = null;
        if (last != null)
        {
            storage.Entry(last).State = EntityState.Unchanged;
            Sample sample = new() { Start = last, End = passing };
            await storage.AddAsync(sample, cancellationToken: cancellationToken);
            sampleComplete = new(
                TimeStart: sample.Start.Time,
                TimeEnd: sample.End.Time,
                TimingPointStart: sample.Start.TimingPointId,
                TimingPointEnd: sample.End.TimingPointId,
                TransponderId: passing.TransponderId
            );
        }                

        await storage.SaveChangesAsync(cancellationToken: cancellationToken);

        await messageBus.Publish(new PassingSaved(passing.Time, passing.Transponder.Id, passing.TimingPoint.Id), cancellationToken: cancellationToken);
        if (sampleComplete != null)
        {
            await messageBus.Publish(sampleComplete, cancellationToken: cancellationToken);
        }
        
        cache.Set(lastPassingKey, passing, cacheEntryOptions);
        cache.Set(cacheKeyTransponder, passing.Transponder, cacheEntryOptions);
        cache.Set(cacheKeyLoop, passing.TimingPoint, cacheEntryOptions);

        activity?.SetStatus(ActivityStatusCode.Ok);
        metrics.PassingProcessed(AgentId, activity);
    }
}
