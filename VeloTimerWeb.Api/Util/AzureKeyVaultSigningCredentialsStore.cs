using IdentityServer4.Models;
using IdentityServer4.Stores;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace VeloTimerWeb.Api.Util
{
    public sealed class AzureKeyVaultSigningCredentialsStore : ISigningCredentialStore, IValidationKeysStore
    {
        private const string MemoryCacheKey = "OAuthCerts";
        private const string SigningAlgorithm = SecurityAlgorithms.RsaSha256;

        private readonly SemaphoreSlim _cacheLock;
        private readonly KeyVaultClient _keyVaultClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AzureKeyVaultSigningCredentialsStore> _logger;
        private readonly IKeyVaultConfig _keyVaultConfig;

        public AzureKeyVaultSigningCredentialsStore(
            KeyVaultClient keyVaultClient, 
            IKeyVaultConfig keyVaultConfig, 
            IMemoryCache cache,
            ILogger<AzureKeyVaultSigningCredentialsStore> logger)
        {
            _keyVaultClient = keyVaultClient;
            _keyVaultConfig = keyVaultConfig;
            _cache = cache;
            _logger = logger;

            // MemoryCache.GetOrCreateAsync does not appear to be thread safe:
            // https://github.com/aspnet/Caching/blob/56447f941b39337947273476b2c366b3dffde565/src/Microsoft.Extensions.Caching.Abstractions/MemoryCacheExtensions.cs#L92-L106
            _cacheLock = new SemaphoreSlim(1);
        }

        public async Task<SigningCredentials> GetSigningCredentialsAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                var (active, _) = await _cache.GetOrCreateAsync(MemoryCacheKey, RefreshCacheAsync);
                _logger.LogInformation($"Active key: {active}");
                return active;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                var (_, secondary) = await _cache.GetOrCreateAsync(MemoryCacheKey, RefreshCacheAsync);
                _logger.LogInformation($"Secondary key: {secondary}");
                return secondary;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task<(SigningCredentials active, IEnumerable<SecurityKeyInfo> secondary)> RefreshCacheAsync(ICacheEntry cache)
        {
            _logger.LogInformation($"Refresh cache.");
            cache.AbsoluteExpiration = DateTime.Now.AddDays(1);
            var enabledCertificateVersions = await GetAllEnabledCertificateVersionsAsync(_keyVaultClient, _keyVaultConfig.KeyVaultName, _keyVaultConfig.KeyVaultCertificateName);
            var active = await GetActiveCertificateAsync(_keyVaultClient, _keyVaultConfig.KeyVaultRolloverHours, enabledCertificateVersions);
            var secondary = await GetSecondaryCertificatesAsync(_keyVaultClient, enabledCertificateVersions);

            return (active, secondary);

            static async Task<List<CertificateItem>> GetAllEnabledCertificateVersionsAsync(KeyVaultClient keyVaultClient, string keyVaultName, string certName)
            {
                // Get all the certificate versions
                var certificateVersions = await keyVaultClient.GetCertificateVersionsAsync($"https://{keyVaultName}.vault.azure.net/", certName);

                // Find all enabled versions of the certificate and sort them by creation date in decending order
                return certificateVersions
                    .Where(certVersion => certVersion.Attributes.Enabled == true)
                    .Where(certVersion => certVersion.Attributes.Created.HasValue)
                    .OrderByDescending(certVersion => certVersion.Attributes.Created)
                    .ToList();
            }

            static async Task<SigningCredentials> GetActiveCertificateAsync(KeyVaultClient keyVaultClient, int rollOverHours, List<CertificateItem> enabledCertificateVersions)
            {
                // Find the first certificate version that is older than the rollover duration
                var rolloverTime = DateTimeOffset.UtcNow.AddHours(-rollOverHours);
                var filteredEnabledCertificateVersions = enabledCertificateVersions
                    .Where(certVersion => certVersion.Attributes.Created < rolloverTime)
                    .ToList();
                if (filteredEnabledCertificateVersions.Any())
                {
                    return new SigningCredentials(
                        await GetCertificateAsync(keyVaultClient, filteredEnabledCertificateVersions.First()),
                        SigningAlgorithm);
                }
                else if (enabledCertificateVersions.Any())
                {
                    // If no certificates older than the rollover duration was found, pick the first enabled version of the certificate (this can happen if it's a newly created certificate)
                    return new SigningCredentials(
                        await GetCertificateAsync(keyVaultClient, enabledCertificateVersions.First()),
                        SigningAlgorithm);
                }
                else
                {
                    // No certificates found
                    return default;
                }
            }

            static async Task<List<SecurityKeyInfo>> GetSecondaryCertificatesAsync(KeyVaultClient keyVaultClient, List<CertificateItem> enabledCertificateVersions)
            {
                var keys = await Task.WhenAll(enabledCertificateVersions.Select(item => GetCertificateAsync(keyVaultClient, item)));
                return keys
                    .Select(key => new SecurityKeyInfo { Key = key, SigningAlgorithm = SigningAlgorithm })
                    .ToList();
            }

            static async Task<X509SecurityKey> GetCertificateAsync(KeyVaultClient keyVaultClient, CertificateItem item)
            {
                var certificateVersionBundle = await keyVaultClient.GetCertificateAsync(item.Identifier.Identifier);
                var certificatePrivateKeySecretBundle = await keyVaultClient.GetSecretAsync(certificateVersionBundle.SecretIdentifier.Identifier);
                var privateKeyBytes = Convert.FromBase64String(certificatePrivateKeySecretBundle.Value);
                var certificateWithPrivateKey = new X509Certificate2(privateKeyBytes, (string)null, X509KeyStorageFlags.MachineKeySet);
                return new X509SecurityKey(certificateWithPrivateKey);
            }
        }
    }
}
