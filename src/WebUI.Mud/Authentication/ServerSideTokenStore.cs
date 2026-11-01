// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using ZiggyCreatures.Caching.Fusion;


namespace VeloTime.WebUI.Mud;

public sealed record TokenCacheEntry(UserToken userToken, string? refreshToken);

/// <summary>
/// Simplified implementation of a server-side token store.
/// Probably want something more robust IRL
/// </summary>
public class ServerSideTokenStore(IFusionCache cache, IDataProtectionProvider dataProtection) : IUserTokenStore
{
    private const string KeyPrefix = "velotime:webui:token:";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("VeloTime.WebUI.UserTokenStore");

    public async Task<TokenResult<TokenForParameters>> GetTokenAsync(ClaimsPrincipal user, UserTokenRequestParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var value = await cache.TryGetAsync<string>(CacheKey(user), token: cancellationToken);

        if (value.HasValue && TryUnprotect(value.Value, out var token))
        {
            return new TokenForParameters(
                token,
                token.RefreshToken == null
                ? null
                : new UserRefreshToken(token.RefreshToken.Value, token.DPoPJsonWebKey)
            );
        }

        return TokenResult.Failure("not found");
    }

    public async Task StoreTokenAsync(ClaimsPrincipal user, UserToken token, UserTokenRequestParameters? parameters = null, CancellationToken ct = default)
    {
        var payload = _protector.Protect(JsonSerializer.Serialize(token));
        await cache.SetAsync(CacheKey(user), payload, options =>
        {
            options.Duration = token.Expiration - DateTimeOffset.Now + TimeSpan.FromDays(7);
        }, token: ct);
    }

    public async Task ClearTokenAsync(ClaimsPrincipal user, UserTokenRequestParameters? parameters = null, CancellationToken ct = default)
    {
        await cache.RemoveAsync(CacheKey(user), token: ct);
    }

    private static string CacheKey(ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("no sub claim");
        var sid = user.FindFirst("sid")?.Value;

        return sid is null ? $"{KeyPrefix}{sub}" : $"{KeyPrefix}{sub}:{sid}";
    }

    private bool TryUnprotect(string payload, [NotNullWhen(true)] out UserToken? token)
    {
        try
        {
            token = JsonSerializer.Deserialize<UserToken>(_protector.Unprotect(payload));
            return token is not null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            token = null;
            return false;
        }
    }
}
