using VeloTime.WebUI.Keycloak;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using System.Net;

namespace VeloTime.WebUI.Mud.Services;

public class ProfileService(IKeycloakClient keycloak, WebUiCache cache) : IProfileService
{
    private const string realm = "velotime";

    public async Task<string> GetUsernameAsync(CancellationToken ct)
    {
        return await cache.GetPersonalAsync(
            "current-username", "string-v1",
            async (IKeycloakClient factoryClient, CancellationToken token) =>
            {
                var account = await factoryClient.GetAccountAsync(false, realm, token);
                if (account is null || string.IsNullOrWhiteSpace(account.Username))
                    throw new HttpRequestException("The current username could not be resolved.", null, HttpStatusCode.BadGateway);
                return account.Username;
            },
            tags: null,
            cancellationToken: ct);
    }

    public async Task<IEnumerable<LinkedAccountView>> GetLinkedAccountsAsync(CancellationToken ct)
    {
        var accounts = await keycloak.GetLinkedAccountsAsync(realm, ct);

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
        await AdministrationClientCall.ExecuteNoContent(() => keycloak.DeleteLinkedProviderAsync(realm, Provider, cancellationToken));
    }
}
