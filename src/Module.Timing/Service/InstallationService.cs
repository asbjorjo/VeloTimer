using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public class InstallationService (TimingDbContext storage)
{
    private static readonly SemaphoreSlim installationSemaphore = new(1, 1);
    private static readonly SemaphoreSlim timingPointSemaphore = new(1, 1);

    public async Task<Installation> FindOrCreateInstallationForAgent(string agentId, CancellationToken cancellationToken = default)
    {
        await installationSemaphore.WaitAsync();
        using var activity = Instrumentation.Source.StartActivity("FindOrCreateInstallationForAgent");
        activity?.SetTag("agentId", agentId);

        try
        {
            var installations = storage.Set<Installation>()
                .Where(i => i.AgentId == agentId);

            Installation installation;


            if (installations.Any())
            {
                installation = installations.First();
            }
            else
            {
                installation = new()
                {
                    AgentId = agentId,
                    TimingSystem = TimingSystem.MyLaps_X2
                };
                await storage.AddAsync(installation, cancellationToken: cancellationToken);
                await storage.SaveChangesAsync();
            }

            activity?.SetTag("installationId", installation.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return installation;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            installationSemaphore.Release();
        }
    }

    public async Task<TimingPoint> FindOrCreateTimingPoint(string agentId, string loopId, CancellationToken cancellationToken = default)
    {
        await timingPointSemaphore.WaitAsync();
        using var activity = Instrumentation.Source.StartActivity("FindOrCreateTimingPoint");
        activity?.SetTag("agentId", agentId);
        activity?.SetTag("loopId", loopId);

        try
        {
            var timingPoint = storage.Set<TimingPoint>()
                .SingleOrDefault(tp => tp.Installation.AgentId == agentId && tp.SystemId == loopId);
            if (timingPoint == null)
            {
                var installation = await FindOrCreateInstallationForAgent(agentId, cancellationToken: cancellationToken);
                storage.Attach(installation);
                timingPoint = new()
                {
                    SystemId = loopId,
                    Description = "",
                    Installation = installation
                };
                await storage.AddAsync(timingPoint, cancellationToken: cancellationToken);
                await storage.SaveChangesAsync(cancellationToken: cancellationToken);
            }

            activity?.SetTag("timingPointId", timingPoint.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return timingPoint;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            timingPointSemaphore.Release();
        }
    }
}
