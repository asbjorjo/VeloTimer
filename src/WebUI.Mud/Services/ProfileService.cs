using VeloTime.WebUI.Keycloak;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class ProfileService(IKeycloakClient keycloak) : IProfileService
{
    private const string realm = "velotime";

    public async Task<string> GetUsernameAsync(CancellationToken ct)
    {
        var account = await keycloak.GetAccountAsync(false, realm);

        return account.Username;
    }

    public async Task<IEnumerable<LinkedAccountView>> GetLinkedAccountsAsync(CancellationToken ct)
    {
        var accounts = await keycloak.GetLinkedAccountsAsync(realm);

        return accounts.Select(a => new LinkedAccountView {
            IsConnected = a.Connected,
            IsSocial = a.Social,
            DisplayName = a.DisplayName,
            ProviderName = a.ProviderName
        });
    }

    public async Task<string> GetLinkAccountAsync(string RedirectUri, string Provider, CancellationToken cancellation = default)
    {
        var link = await keycloak.BuildLinkingUriAsync(RedirectUri, realm, Provider, cancellation);

        return link.AccountLinkUri;
    }

    public async Task DeleteLinkedAccountAsync(string Provider, CancellationToken cancellationToken = default)
    {
        await keycloak.DeleteLinkedProviderAsync(realm, Provider, cancellationToken);
    }
}
