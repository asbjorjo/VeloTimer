// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Security.Claims;
using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;


namespace VeloTime.WebUI.Mud;

public sealed record TokenCacheEntry(UserToken userToken, string? refreshToken);

/// <summary>
/// Simplified implementation of a server-side token store.
/// Probably want something more robust IRL
/// </summary>
public class ServerSideTokenStore(IFusionCache cache) : IUserTokenStore
{
    //private static readonly ConcurrentDictionary<string, TokenForParameters> _tokens = new();

    public async Task<TokenResult<TokenForParameters>> GetTokenAsync(ClaimsPrincipal user, UserTokenRequestParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var sub = user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("no sub claim");

        var value = await cache.TryGetAsync<UserToken>(sub, token: cancellationToken);

        if (value.HasValue)
        {
            var token = value.Value;
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
        var sub = user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("no sub claim");
        await cache.SetAsync(sub, token, options =>
        {
            options.Duration = token.Expiration - DateTimeOffset.Now + TimeSpan.FromMinutes(5);
        }, token: ct);
    }

    public async Task ClearTokenAsync(ClaimsPrincipal user, UserTokenRequestParameters? parameters = null, CancellationToken ct = default)
    {
        var sub = user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("no sub claim");

        await cache.RemoveAsync(sub, token: ct);
    }
}