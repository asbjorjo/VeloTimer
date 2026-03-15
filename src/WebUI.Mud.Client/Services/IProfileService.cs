namespace VeloTime.WebUI.Mud.Client.Services;

public interface IProfileService
{
    Task<string> GetUsernameAsync(CancellationToken ct = default);
    Task<IEnumerable<LinkedAccountView>> GetLinkedAccountsAsync(CancellationToken cancellationToken = default);
    Task<string> GetLinkAccountAsync(string RedirectUri, string Provider, CancellationToken cancellation = default);
}
