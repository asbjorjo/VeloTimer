using Microsoft.AspNetCore.Identity;
using VeloTime.Users.Migration.Keycloak;
using VeloTime.Users.Migration.Models.Identity;

namespace VeloTime.Users.Migration.Services;

internal sealed class KeycloakUserMigrationWriter(IKeycloakClient keycloak, ILogger logger)
{
    private const string Realm = "velotime";
    private readonly List<string> issues = [];
    private readonly List<string> warnings = [];
    private int created, existing, identitiesLinked, usersVerifiedClean;

    internal IReadOnlyList<string> Issues => issues;
    internal IReadOnlyList<string> Warnings => warnings;

    internal async Task<Guid?> EnsureUserAsync(long riderId, User user, IList<UserLoginInfo> logins,
        string? firstName, string? lastName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var selectedLogins = new List<UserLoginInfo>();
            var hasMultipleProviderLogins = false;
            foreach (var providerGroup in logins.GroupBy(login => login.LoginProvider.ToLowerInvariant()))
            {
                var providerLogins = providerGroup.ToList();
                var selectedLogin = providerLogins[0];
                selectedLogins.Add(selectedLogin);
                var seenKeys = new HashSet<string>(StringComparer.Ordinal) { selectedLogin.ProviderKey };
                foreach (var login in providerLogins.Skip(1))
                {
                    if (!seenKeys.Add(login.ProviderKey))
                        continue;
                    hasMultipleProviderLogins = true;
                    Warn($"Rider {riderId}: source has multiple '{providerGroup.Key}' logins; Keycloak supports one link per provider, so key '{login.ProviderKey}' was not linked.");
                }
            }

            var matches = await FindUsersAsync(user.UserName, ct);
            if (matches.Count > 1)
            {
                ReportIssue($"Rider {riderId}: multiple Keycloak users exactly match username '{user.UserName}'.");
                return null;
            }

            if (matches.Count == 0)
            {
                var normalizedFirstName = KeycloakNameNormalizer.Normalize(firstName);
                var normalizedLastName = KeycloakNameNormalizer.Normalize(lastName);
                if (normalizedFirstName != firstName || normalizedLastName != lastName)
                    Warn($"Rider {riderId}: normalized name from '{firstName} {lastName}' to '{normalizedFirstName} {normalizedLastName}' for Keycloak.");
                await keycloak.UsersPOSTAsync(new UserRepresentation
                {
                    Username = user.UserName, Enabled = true, EmailVerified = true, Email = user.Email,
                    FirstName = normalizedFirstName, LastName = normalizedLastName,
                    FederatedIdentities = selectedLogins.Select(ToFederatedIdentity).ToList()
                }, Realm, ct);
                created++;
                identitiesLinked += selectedLogins.Count;
            }
            else
            {
                existing++;
                var existingUser = matches.Single();
                if (string.IsNullOrWhiteSpace(existingUser.Id))
                {
                    ReportIssue($"Rider {riderId}: the matching Keycloak user has no ID.");
                    return null;
                }

                var links = await TryGetIdentitiesAsync(riderId, existingUser.Id, ct);
                if (links is not null)
                {
                    foreach (var login in selectedLogins)
                    {
                        var provider = login.LoginProvider.ToLowerInvariant();
                        var providerLinks = links.Where(link => ProviderEquals(link.IdentityProvider, provider)).ToList();
                        if (providerLinks.Count > 0)
                            continue;

                        try
                        {
                            await keycloak.FederatedIdentityPOSTAsync(provider, ToFederatedIdentity(login), Realm, existingUser.Id, ct);
                            identitiesLinked++;
                        }
                        catch (Keycloak.ApiException exception) when (exception.StatusCode == 409)
                        {
                            Warn($"Rider {riderId}: Keycloak reported HTTP 409 while linking provider '{provider}'; the link will be verified.");
                        }
                        catch (Exception exception) when (!MigrationFailure.IsFatalOrStopping(exception, ct))
                        {
                            ReportIssue($"Rider {riderId}: could not link provider '{provider}' in Keycloak: {ExceptionDetail(exception)}");
                        }
                    }
                }
            }

            var verifiedUsers = await FindUsersAsync(user.UserName, ct);
            if (verifiedUsers.Count != 1)
            {
                ReportIssue(verifiedUsers.Count == 0
                    ? $"Rider {riderId}: no exact Keycloak user was found after create/link."
                    : $"Rider {riderId}: multiple Keycloak users exactly match username '{user.UserName}' after create/link.");
                return null;
            }

            var verifiedUser = verifiedUsers.Single();
            if (!Guid.TryParse(verifiedUser.Id, out var userId))
            {
                ReportIssue($"Rider {riderId}: Keycloak returned an invalid user ID '{verifiedUser.Id}'.");
                return null;
            }

            var verifiedLinks = await TryGetIdentitiesAsync(riderId, verifiedUser.Id!, ct);
            if (verifiedLinks is null)
                return userId;
            var clean = !hasMultipleProviderLogins;
            foreach (var login in selectedLogins)
            {
                var provider = login.LoginProvider.ToLowerInvariant();
                var providerLinks = verifiedLinks.Where(link => ProviderEquals(link.IdentityProvider, provider)).ToList();
                if (providerLinks.Any(link => string.Equals(link.UserId, login.ProviderKey, StringComparison.Ordinal)))
                    continue;
                clean = false;
                var reason = providerLinks.Count > 0 ? "is linked to a different key" : "is missing";
                Warn($"Rider {riderId}: Keycloak federated identity '{provider}' for source key '{login.ProviderKey}' {reason} after verification.");
            }
            if (clean)
                usersVerifiedClean++;
            return userId;
        }
        catch (Exception exception) when (!MigrationFailure.IsFatalOrStopping(exception, ct))
        {
            ReportIssue($"Rider {riderId}: Keycloak user could not be created or verified: {ExceptionDetail(exception)}");
            return null;
        }
    }

    internal void LogSummary() => logger.LogInformation(
        "Keycloak user migration finished: {CreatedCount} created, {ExistingCount} existing, {IdentitiesLinkedCount} identities linked, {UsersVerifiedCleanCount} users verified clean, {WarningCount} warnings, {IssueCount} issues.",
        created, existing, identitiesLinked, usersVerifiedClean, warnings.Count, issues.Count);

    private async Task<ICollection<UserRepresentation>> FindUsersAsync(string? username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("The identity user has no username for Keycloak lookup.");
        var users = await keycloak.UsersAll3Async(briefRepresentation: false, createdAfter: "", createdBefore: "", email: "",
            emailVerified: null, enabled: null, exact: true, first: null, firstName: "", idpAlias: "", idpUserId: "",
            lastName: "", max: null, q: "", search: "", username: username, realm: Realm, cancellationToken: ct);
        return users ?? throw new InvalidOperationException("Keycloak returned a null user collection.");
    }

    private async Task<ICollection<FederatedIdentityRepresentation>?> TryGetIdentitiesAsync(long riderId, string userId, CancellationToken ct)
    {
        try
        {
            var identities = await keycloak.FederatedIdentityAllAsync(Realm, userId, ct);
            if (identities is null)
            {
                ReportIssue($"Rider {riderId}: Keycloak returned a null federated identity collection for user {userId}.");
                return null;
            }
            return identities;
        }
        catch (Exception exception) when (!MigrationFailure.IsFatalOrStopping(exception, ct))
        {
            ReportIssue($"Rider {riderId}: Keycloak federated identities could not be read for user '{userId}': {ExceptionDetail(exception)}");
            return null;
        }
    }

    private static FederatedIdentityRepresentation ToFederatedIdentity(UserLoginInfo login) => new()
    {
        IdentityProvider = login.LoginProvider.ToLowerInvariant(), UserId = login.ProviderKey,
        UserName = login.ProviderDisplayName ?? login.ProviderKey
    };

    private static bool ProviderEquals(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ExceptionDetail(Exception exception) => exception is Keycloak.ApiException apiException
        ? $"HTTP {apiException.StatusCode}: {apiException.Response}"
        : exception.Message;

    private void ReportIssue(string issue)
    {
        issues.Add(issue);
        logger.LogError("Unresolved Keycloak user migration: {Issue}", issue);
    }

    private void Warn(string warning)
    {
        warnings.Add(warning);
        logger.LogWarning("Keycloak user migration warning: {Warning}", warning);
    }
}
