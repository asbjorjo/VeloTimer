using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IAuthDebugService
{
    /// <summary>Returns null when auth debugging is unavailable (outside Development).</summary>
    Task<AuthDebugView?> GetAccessTokenInfoAsync(CancellationToken cancellationToken = default);
}
