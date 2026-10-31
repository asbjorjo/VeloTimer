using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface;

namespace VeloTime.WebUI.Mud.Services;

/// <summary>
/// Small WebUI-owned cache boundary for stable lookup snapshots. Keys encode the
/// representation audience and current caller; authorization is evaluated before
/// every protected lookup, including hits.
/// </summary>
public sealed class WebUiCache(
    HybridCache cache,
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorization,
    ICacheInvalidationOperations invalidation,
    ILogger<WebUiCache> logger,
    IServiceScopeFactory scopeFactory,
    WebUiCacheFactoryContext factoryContext)
{
    internal async Task<T> GetPublicAsync<TClient, T>(
        string operation,
        string representation,
        string entity,
        Func<TClient, CancellationToken, Task<T>> factory,
        string[]? tags,
        CancellationToken cancellationToken) where TClient : notnull
    {
        var principal = (await GetCurrentPrincipalAsync(cancellationToken)).Clone();
        return await cache.GetOrCreateAsync<T>(
            Key("public", operation, "v1", representation, entity),
            async token => await factoryContext.RunAsync(scopeFactory, principal, factory, token),
            NewEntryOptions(),
            tags,
            cancellationToken);
    }

    internal async Task<T> GetPersonalAsync<TClient, T>(
        string operation,
        string representation,
        Func<TClient, CancellationToken, Task<T>> factory,
        string[]? tags,
        CancellationToken cancellationToken) where TClient : notnull
    {
        var identity = await GetIdentityAsync(cancellationToken);
        return await cache.GetOrCreateAsync<T>(
            Key("personal", operation, "v1", identity.SessionHash, representation),
            async token => await factoryContext.RunAsync(scopeFactory, identity.Principal, factory, token),
            NewEntryOptions(),
            tags,
            cancellationToken);
    }

    internal async Task<T> GetPreferenceAsync<TClient, T>(
        string kind,
        Func<TClient, CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken) where TClient : notnull
    {
        var identity = await GetIdentityAsync(cancellationToken);
        var tag = PreferenceTag(identity.Issuer, identity.Subject, kind);
        return await cache.GetOrCreateAsync<T>(
            Key("personal", $"{kind}-preference", "persisted-v1", identity.SessionHash),
            async token => await factoryContext.RunAsync(scopeFactory, identity.Principal, factory, token),
            NewEntryOptions(),
            [tag],
            cancellationToken);
    }

    internal async Task<T> GetAuthenticatedReferenceAsync<TClient, T>(
        string operation,
        string representation,
        Func<TClient, CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken) where TClient : notnull
    {
        var identity = await GetIdentityAsync(cancellationToken);
        return await cache.GetOrCreateAsync<T>(
            Key("authenticated-reference", operation, "v1", identity.SessionHash, representation),
            async token => await factoryContext.RunAsync(scopeFactory, identity.Principal, factory, token),
            NewEntryOptions(),
            tags: null,
            cancellationToken);
    }

    internal async Task<T> GetAdminAsync<TClient, T>(
        string policy,
        string operation,
        string representation,
        Func<TClient, CancellationToken, Task<T>> factory,
        string[]? tags,
        CancellationToken cancellationToken) where TClient : notnull
    {
        var principal = await GetAuthorizedPrincipalAsync(policy, cancellationToken);
        var identity = GetIdentity(principal);
        return await cache.GetOrCreateAsync<T>(
            Key("admin", operation, policy, identity.SessionHash, representation),
            async token => await factoryContext.RunAsync(scopeFactory, identity.Principal, factory, token),
            NewEntryOptions(),
            tags,
            cancellationToken);
    }

    internal async Task RequirePolicyAsync(string policy, CancellationToken cancellationToken)
    {
        _ = await GetAuthorizedPrincipalAsync(policy, cancellationToken);
    }

    private async Task<ClaimsPrincipal> GetAuthorizedPrincipalAsync(string policy, CancellationToken cancellationToken)
    {
        var principal = await GetCurrentPrincipalAsync(cancellationToken);
        if (!(await authorization.AuthorizeAsync(principal, resource: null, policy)).Succeeded)
            throw Forbidden("The required administrator policy was not satisfied.");
        return principal;
    }

    internal async Task InvalidateTagAsync(string tag, string operation)
    {
        // A durable upstream write must not lose its eviction because the request
        // was canceled after commit. Keep this bounded and use strict cache eviction.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await invalidation.RemoveByTagAsync(tag, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "WebUI cache invalidation failed after {Operation} for dependency {Dependency}.",
                operation, tag);
            throw;
        }
    }

    internal async Task<T> WriteAndInvalidateAsync<T>(Func<Task<T>> write, string tag, string operation)
    {
        T response;
        try
        {
            response = await write();
        }
        catch (Exception exception) when (IsSuccessfulResponseFailure(exception))
        {
            // Generated clients report malformed/null bodies as ApiException
            // with the successful HTTP status. Persistence is already known.
            await InvalidateTagAsync(tag, operation);
            throw new InvalidDataException("The saved response was invalid.", exception);
        }
        await InvalidateTagAsync(tag, operation);
        return response;
    }

    private static bool IsSuccessfulResponseFailure(Exception exception) => exception switch
    {
        Module.Profiles.Client.ApiException api => api.StatusCode is >= 200 and < 300,
        Module.Facilities.Client.ApiException api => api.StatusCode is >= 200 and < 300,
        Module.Statistics.Client.ApiException api => api.StatusCode is >= 200 and < 300,
        _ => false
    };

    internal static string PreferenceTag(string issuer, string subject, string kind)
    {
        var subjectHash = HashTuple(issuer, subject);
        return $"velotime:webui:personal-preference:{kind}:{subjectHash}";
    }

    internal async Task<string> PreferenceTagAsync(string kind, CancellationToken cancellationToken)
    {
        var identity = await GetIdentityAsync(cancellationToken);
        return PreferenceTag(identity.Issuer, identity.Subject, kind);
    }

    private async Task<Identity> GetIdentityAsync(CancellationToken cancellationToken)
    {
        var principal = await GetCurrentPrincipalAsync(cancellationToken);
        if (principal.Identity?.IsAuthenticated != true)
            throw Forbidden("A signed-in user is required.");
        return GetIdentity(principal);
    }

    private static Identity GetIdentity(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            throw Forbidden("The signed-in identity is missing its validated issuer or subject.");
        var session = principal.FindFirst("sid")?.Value;
        return new Identity(issuer, subject, HashTuple(issuer, subject, session), principal.Clone());
    }

    private async Task<ClaimsPrincipal> GetCurrentPrincipalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = httpContextAccessor.HttpContext;
        // HTTP middleware is authoritative during an active request, including
        // anonymous requests. A BFF request need not have cascading auth state.
        if (context is { Response.HasStarted: false })
            return context.User;

        // Never resurrect a captured HTTP identity after streaming starts. An
        // unavailable circuit provider is an error, not permission to use stale claims.
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return state.User;
    }

    private static string Key(params string[] parts) =>
        $"velotime:webui:{string.Join(':', parts.Select(part => HashTextIfLong(part)))}";

    private static HybridCacheEntryOptions NewEntryOptions() => new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    private static string HashTextIfLong(string value) =>
        value.Length <= 80 ? value : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string HashTuple(params string?[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var value in values)
            {
                if (value is null)
                {
                    writer.Write(-1);
                    continue;
                }

                var bytes = Encoding.UTF8.GetBytes(value);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static HttpRequestException Forbidden(string message) =>
        new(message, null, HttpStatusCode.Forbidden);

    private sealed record Identity(string Issuer, string Subject, string SessionHash, ClaimsPrincipal Principal);
}
