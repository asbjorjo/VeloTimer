using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Handlers;

public class InstallationLayoutHandler(InstallationService installationService, TimingDbContext storage, ILogger<InstallationLayoutHandler> log) : IConsumer<InstallationLayoutEvent>, IConsumerWithContext
{
    public required IConsumerContext Context { get; set; }

    public async Task OnHandle(InstallationLayoutEvent message, CancellationToken cancellationToken)
    {
        object? agentIdObj = Context.GetTransportMessage().ApplicationProperties.GetValueOrDefault("AgentId", string.Empty);
        string AgentId = agentIdObj?.ToString() ?? string.Empty;

        Installation installation =
            await installationService.GetInstallationForAgent(AgentId, cancellationToken: cancellationToken) ??
            await installationService.CreateInstallationForAgent(AgentId, cancellationToken: cancellationToken);


        IEnumerable<SystemConfigEvent> systems = message.Systems.Where(s => s.IsActive);

        if (systems.Count() > 1)
        {
            log.LogWarning("Multiple active systems found for AgentId {AgentId}. Only the first one will be used.", AgentId);
        }

        var system = systems.First();

        if (string.IsNullOrWhiteSpace(installation.Description))
        {
            installation.Description = system.Description;
            storage.Entry(installation).State = EntityState.Modified;
        }

        if (installation.TimingSystem == TimingSystem.Unknown)
        {
            installation.TimingSystem = TimingSystem.MyLaps_X2;
            storage.Entry(installation).State = EntityState.Modified;
        }

        foreach (LoopConfigEvent loop in message.TimingLoops.Where(l => l.SystemId == system.SystemId))
        {
            TimingPoint timingPoint = 
                await installationService.GetTimingPoint(installation, loop.LoopId, cancellationToken: cancellationToken) ??
                await installationService.CreateTimingPoint(installation, loop.LoopId, cancellationToken: cancellationToken);


            if (string.IsNullOrWhiteSpace(timingPoint.Description))
            {
                timingPoint.Description = loop.Name;
            }
            storage.Entry(timingPoint).State = EntityState.Modified;
        }

        await storage.SaveChangesAsync(cancellationToken: cancellationToken);
    }
}