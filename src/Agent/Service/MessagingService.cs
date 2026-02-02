using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Logging;
using VeloTime.Agent.Model;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent.Service;

public class MessagingService(IMessageBus messageBus, AgentDbContext dbContext, Metrics metrics, ILogger<MessagingService> log)
{
    private bool Resend = true;

    public async Task SendEventAsync(InstallationConfig config)
    {
        await messageBus.Publish(config.ToMessage());
    }

    public async Task SendEventsAsync(IEnumerable<LoopConfig> config)
    {
        await messageBus.Publish(config.Select(m => m.ToMessage()));
    }

    public async Task SendEventAsync(LoopStatus status)
    {
        await messageBus.Publish(status.ToMessage());
    }

    public async Task SendEventsAsync(IEnumerable<Passing> passing)
    {
        using var activity = Instrumentation.Source.StartActivity("Send Passings");
        activity?.SetTag("count", passing.Count());
        log.LogSendingEvents("Passing", passing.Count());
        if (Resend)
        {
           var sent = await dbContext.Set<Passing>()
                .Where(p => passing.Select(p => p.Id).Contains(p.Id))
                .ToListAsync();
            if (sent.Any())
            {
                passing = passing.ExceptBy(sent.Select(p => p.Id), p => p.Id);
                await messageBus.Publish(passing.Select(m => m.ToMessage()));
            } 
            
            if (passing.Any())
            {
                Resend = false;
            }
        }
        await dbContext.AddRangeAsync(passing);
        await messageBus.Publish(passing.Select(m => m.ToMessage()));
        await dbContext.SaveChangesAsync();
        activity?.SetStatus(ActivityStatusCode.Ok);
        metrics.ProcessedPassing(passing.Count(), activity);
    }

    public async Task SendEventsAsync(IEnumerable<SegmentConfig> config)
    {
        await messageBus.Publish(config.Select(m => m.ToMessage()));
    }

    public async Task SendEventsAsync(IEnumerable<SystemConfig> config)
    {
        await messageBus.Publish(config.Select(m => m.ToMessage()));
    }
}
