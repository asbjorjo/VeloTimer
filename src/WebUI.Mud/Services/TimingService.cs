using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Timing.Interface.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class TimingService(ITimingClient timing) : ITimingService
{
    public async Task<InstallationView> GetInstallationAsync(Guid Id, CancellationToken cancellationToken = default)
    {
        var installation = await timing.GetInstallationById(Id, cancellationToken);

        return new InstallationView
        {
            Id = installation.Id,
            AgentId = installation.AgentId,
            TimingSystem = installation.TimingSystem.Name,
            Description = installation.Description,
            TimingPoints = installation.TimingPoints.Select(tp => new TimingPointView
            {
                Id = tp.Id,
                Description = tp.Description,
                SystemId = tp.SystemId
            })
        };
    }

    public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(CancellationToken cancellationToken)
    {
        var installations = await timing.GetAllInstallations(cancellationToken);
       
        return installations.Select(installation => new InstallationView
        {
            Id = installation.Id,
            AgentId = installation.AgentId,
            TimingSystem = installation.TimingSystem.Name,
            Description = installation.Description,
            TimingPoints = installation.TimingPoints.Select(tp => new TimingPointView
            {
                Id = tp.Id,
                Description = tp.Description,
                SystemId = tp.SystemId
            })
        });
    }
}
