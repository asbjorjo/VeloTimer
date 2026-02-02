namespace VeloTime.WebUI.Mud.Client.Services;

public interface ITimingService
{
    Task<IEnumerable<InstallationView>> GetInstallationsAsync(CancellationToken cancellationToken = default);
    Task<InstallationView> GetInstallationAsync(Guid Id, CancellationToken cancellationToken = default);
}
