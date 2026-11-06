using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Common;

namespace VeloTime.Module.Profiles.Api.Endpoints;

internal static class ProfilePreferenceCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InvalidationTimeout = TimeSpan.FromSeconds(5);

    internal static string LanguagePreferenceKey(string issuer, string subject) =>
        Key("language-preference", issuer, subject);

    internal static string DisplayPreferenceKey(string issuer, string subject) =>
        Key("display-preference", issuer, subject);

    internal static HybridCacheEntryOptions NewEntryOptions() => new()
    {
        Expiration = Lifetime,
        LocalCacheExpiration = Lifetime
    };

    internal static async Task InvalidateAsync(
        HybridCache cache,
        ICacheInvalidationOperations invalidation,
        ILogger logger,
        string key,
        string operation)
    {
        var useFusionOperations = invalidation.Handles(cache);
        if (!useFusionOperations && invalidation.RequiresStrictHandling(cache))
        {
            throw new InvalidOperationException("The configured cache invalidation operations do not own this FusionCache adapter.");
        }

        using var timeout = new CancellationTokenSource(InvalidationTimeout);
        try
        {
            if (useFusionOperations)
            {
                await invalidation.RemoveAsync(key, timeout.Token);
            }
            else
            {
                await cache.RemoveAsync(key, timeout.Token);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Profile preference cache invalidation failed after {Operation} for key {CacheKey}.", operation, key);
            throw;
        }
    }

    private static string Key(string operation, string issuer, string subject) =>
        $"velotime:profiles:{operation}:persisted-v1:{HashIdentity(issuer, subject)}";

    private static string HashIdentity(string issuer, string subject)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WritePart(writer, issuer);
            WritePart(writer, subject);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WritePart(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
