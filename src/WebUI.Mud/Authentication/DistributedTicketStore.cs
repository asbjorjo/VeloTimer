using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using ZiggyCreatures.Caching.Fusion;

namespace VeloTime.WebUI.Mud;

public class DistributedTicketStore(IFusionCache cache, IDataProtectionProvider dataProtection) : ITicketStore
{
    private const string KeyPrefix = "velotime:webui:ticket:";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);
    // Short L1 lifetime bounds staleness if a backplane message is missed.
    private static readonly TimeSpan MemoryLifetime = TimeSpan.FromMinutes(1);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("VeloTime.WebUI.TicketStore");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var payload = Convert.ToBase64String(_protector.Protect(TicketSerializer.Default.Serialize(ticket)));
        var lifetime = ticket.Properties.ExpiresUtc is { } expires
            ? expires - DateTimeOffset.UtcNow
            : DefaultLifetime;

        if (lifetime <= TimeSpan.Zero)
        {
            await cache.RemoveAsync(KeyPrefix + key, token: cancellationToken);
            return;
        }

        await cache.SetAsync(KeyPrefix + key, payload, options =>
        {
            options.Duration = lifetime;
            options.MemoryCacheDuration = lifetime < MemoryLifetime ? lifetime : MemoryLifetime;
        }, token: cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        var payload = await cache.TryGetAsync<string>(KeyPrefix + key, token: cancellationToken);
        if (!payload.HasValue)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(Convert.FromBase64String(payload.Value)));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(KeyPrefix + key, token: cancellationToken);
    }
}
