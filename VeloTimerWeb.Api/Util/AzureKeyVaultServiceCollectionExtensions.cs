using Duende.IdentityServer.Stores;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace VeloTimerWeb.Api.Util
{
    public static class AzureKeyVaultServiceCollectionExtensions
    {
        // Uses MSI (Managed Service Identity)
        public static IServiceCollection AddKeyVaultSigningCredentials(this IServiceCollection @this)
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var authenticationCallback = new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback);
            var keyVaultClient = new KeyVaultClient(authenticationCallback);
            @this.AddMemoryCache();
            @this.AddSingleton(keyVaultClient);
            @this.AddSingleton<AzureKeyVaultSigningCredentialsStore>();
            @this.AddSingleton<ISigningCredentialStore>(services => services.GetRequiredService<AzureKeyVaultSigningCredentialsStore>());
            @this.AddSingleton<IValidationKeysStore>(services => services.GetRequiredService<AzureKeyVaultSigningCredentialsStore>());
            return @this;
        }

        // Uses Client Credentials (client id and secret)
        public static IServiceCollection AddKeyVaultSigningCredentials(this IServiceCollection @this, string clientId, string clientSecret)
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var keyVaultClient = new KeyVaultClient(async (authority, resource, scope) =>
            {
                // Taken from https://docs.microsoft.com/en-us/azure/key-vault/secrets/quick-create-net-v3
                var adCredential = new ClientCredential(clientId, clientSecret);
                var authenticationContext = new AuthenticationContext(authority, null);
                return (await authenticationContext.AcquireTokenAsync(resource, adCredential)).AccessToken;
            });

            @this.AddMemoryCache();
            @this.AddSingleton(keyVaultClient);
            @this.AddSingleton<AzureKeyVaultSigningCredentialsStore>();
            @this.AddSingleton<ISigningCredentialStore>(services => services.GetRequiredService<AzureKeyVaultSigningCredentialsStore>());
            @this.AddSingleton<IValidationKeysStore>(services => services.GetRequiredService<AzureKeyVaultSigningCredentialsStore>());
            return @this;
        }
    }
}
