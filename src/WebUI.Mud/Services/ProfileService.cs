using VeloTime.WebUI.Keycloak;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Services;

public class ProfileService(IKeycloakClient keycloak) : IProfileService
{
    public async Task<string> GetUsernameAsync()
    {
        var account = await keycloak.GetAccountAsync(false, "velotime");

        return account.Username;
    }
}
