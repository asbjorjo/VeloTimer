using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using Directory = VeloTime.WebUI.KeycloakAdmin;

namespace VeloTime.WebUI.Mud.Services;

/// <summary>Composes Statistics display names and read-only Keycloak identities for transponder owners.</summary>
/// <param name="statistics">The delegated Statistics client.</param>
/// <param name="keycloak">The delegated Keycloak administrator client.</param>
/// <param name="httpContextAccessor">Provides the current request principal when available.</param>
/// <param name="authenticationStateProvider">Provides the principal inside interactive server circuits.</param>
/// <param name="authorization">Enforces administrator permissions even when no BFF route is involved.</param>
public sealed class TransponderOwnerService(
    IStatisticsClient statistics,
    Directory.IKeycloakAdminClient keycloak,
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorization) : ITransponderOwnerService
{
    private const int SourcePageSize = 10;

    /// <inheritdoc />
    public async Task<OwnTransponderProfile> GetOwnProfileAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync();
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            throw new HttpRequestException("A valid signed-in user is required.", null, HttpStatusCode.Forbidden);
        return await ExecuteAsync(async () =>
        {
            var profile = await statistics.GetOwnOwnerProfileAsync(cancellationToken);
            if (profile is null || profile.UserId != id)
                throw new HttpRequestException("The Statistics profile response did not match the signed-in user.", null, HttpStatusCode.BadGateway);
            return new OwnTransponderProfile(id, profile.ProfileName);
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransponderOwnerView>> ResolveAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.AuthorizeAsync(await GetUserAsync(), null, AdministrationAuthorization.TransponderStatusAdminPolicy)).Succeeded)
            throw new HttpRequestException("Administrator access is required to resolve owners.", null, HttpStatusCode.Forbidden);
        if (userIds is null || userIds.Count > 100 || userIds.Any(id => id == Guid.Empty))
            throw new HttpRequestException("Provide at most 100 valid owner IDs.", null, HttpStatusCode.BadRequest);
        return await ExecuteAsync<IReadOnlyList<TransponderOwnerView>>(async () =>
        {
            var ids = userIds.Distinct().ToArray();
            if (ids.Length == 0) return [];
            var profiles = await statistics.ResolveOwnerProfilesAsync(new ResolveOwnerProfilesRequest { UserIds = ids }, cancellationToken);
            if (profiles is null || profiles.Any(profile => !ids.Contains(profile.UserId)) ||
                profiles.Select(profile => profile.UserId).Distinct().Count() != profiles.Count)
                throw InvalidResponse();
            var names = profiles.ToDictionary(profile => profile.UserId, profile => profile.ProfileName);
            var owners = new List<TransponderOwnerView>();
            foreach (var id in ids)
            {
                var account = await FindAccountAsync(id, cancellationToken);
                owners.Add(ToView(id, names.GetValueOrDefault(id), account));
            }
            return owners;
        });
    }

    /// <inheritdoc />
    public async Task<TransponderOwnerPage> SearchAsync(string query, int? profileOffset = 0, int? keycloakOffset = 0, CancellationToken cancellationToken = default)
    {
        await RequireAdministrationAsync();
        query = query?.Trim() ?? "";
        if (query.Length is < 2 or > 100 || profileOffset < 0 || keycloakOffset < 0 ||
            profileOffset > int.MaxValue - SourcePageSize || keycloakOffset > int.MaxValue - SourcePageSize)
            throw new HttpRequestException("Owner search requires 2-100 characters and nonnegative offsets.", null, HttpStatusCode.BadRequest);
        return await ExecuteAsync(async () =>
        {
            var profilePage = profileOffset.HasValue
                ? await statistics.SearchOwnerProfilesAsync(query, profileOffset.Value, SourcePageSize, cancellationToken)
                : new OwnerProfilePage { Profiles = [], NextOffset = null };
            if (profilePage is null || profilePage.Profiles is null || profilePage.Profiles.Count > SourcePageSize ||
                profilePage.Profiles.Any(profile => profile.UserId == Guid.Empty) ||
                profilePage.Profiles.Select(profile => profile.UserId).Distinct().Count() != profilePage.Profiles.Count ||
                profilePage.NextOffset.HasValue && profilePage.NextOffset <= profileOffset)
                throw InvalidResponse();
            var accounts = keycloakOffset.HasValue
                ? await keycloak.UsersAll3Async(briefRepresentation: true, createdAfter: null, createdBefore: null,
                    email: null, emailVerified: null, enabled: null, exact: false, first: keycloakOffset.Value,
                    firstName: null, idpAlias: null, idpUserId: null, lastName: null, max: SourcePageSize + 1,
                    q: null, search: query, username: null, realm: "velotime", cancellationToken: cancellationToken)
                : [];
            if (accounts is null || accounts.Count > SourcePageSize + 1) throw InvalidResponse();
            var directoryMatches = accounts.Take(SourcePageSize).ToList();
            var byId = new Dictionary<Guid, Directory.UserRepresentation>();
            foreach (var account in directoryMatches)
                byId[AccountId(account)] = account;
            var profileNames = profilePage.Profiles.ToDictionary(profile => profile.UserId, profile => profile.ProfileName);
            var missingProfileIds = byId.Keys.Except(profileNames.Keys).ToArray();
            if (missingProfileIds.Length > 0)
            {
                var profiles = await statistics.ResolveOwnerProfilesAsync(new ResolveOwnerProfilesRequest { UserIds = missingProfileIds }, cancellationToken);
                if (profiles is null || profiles.Any(profile => !missingProfileIds.Contains(profile.UserId)) ||
                    profiles.Select(profile => profile.UserId).Distinct().Count() != profiles.Count) throw InvalidResponse();
                foreach (var profile in profiles) profileNames[profile.UserId] = profile.ProfileName;
            }
            foreach (var profile in profilePage.Profiles)
            {
                if (byId.ContainsKey(profile.UserId)) continue;
                var account = await FindAccountAsync(profile.UserId, cancellationToken);
                if (account is not null) byId.Add(profile.UserId, account);
            }
            return new TransponderOwnerPage(byId.Select(pair => ToView(pair.Key, profileNames.GetValueOrDefault(pair.Key), pair.Value))
                .OrderBy(owner => owner.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(owner => owner.UserId).ToList(),
                profilePage.NextOffset, accounts.Count > SourcePageSize ? keycloakOffset + SourcePageSize : null);
        });
    }

    private async Task<Directory.UserRepresentation?> FindAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var account = await keycloak.UsersGET2Async(false, "velotime", id.ToString(), cancellationToken);
            if (account is null || AccountId(account) != id) throw InvalidResponse();
            return account;
        }
        catch (Directory.ApiException exception) when (exception.StatusCode == 404) { return null; }
    }

    private static Guid AccountId(Directory.UserRepresentation account) =>
        Guid.TryParse(account.Id, out var id) && id != Guid.Empty ? id : throw InvalidResponse();

    private static TransponderOwnerView ToView(Guid id, string? profileName, Directory.UserRepresentation? account) =>
        new(id, profileName, account?.FirstName, account?.LastName, account is not null);

    private async Task RequireAdministrationAsync()
    {
        if (!(await authorization.AuthorizeAsync(await GetUserAsync(), null, AdministrationAuthorization.TimingPolicy)).Succeeded)
            throw new HttpRequestException("Timing administrator access is required to look up owners.", null, HttpStatusCode.Forbidden);
    }

    private async Task<ClaimsPrincipal> GetUserAsync() =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user : (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

    private static HttpRequestException InvalidResponse() =>
        new("An owner directory returned an invalid response. Please retry.", null, HttpStatusCode.BadGateway);

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Directory.ApiException exception)
        {
            throw new HttpRequestException("Keycloak owner names could not be loaded. Verify read-only user-directory permissions and retry.",
                exception, exception.StatusCode is 401 or 403 ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable);
        }
        catch (ApiException exception)
        {
            throw new HttpRequestException("Statistics profile names could not be loaded. Check access and retry.",
                exception, exception.StatusCode is 401 or 403 ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable);
        }
    }
}
