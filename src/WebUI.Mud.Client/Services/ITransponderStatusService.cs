using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface ITransponderStatusService
{
    Task<TransponderStatusView> GetAsync(Guid id, int ownershipPage = 0, int warningPage = 0,
        int pageSize = 10, CancellationToken cancellationToken = default);
}
