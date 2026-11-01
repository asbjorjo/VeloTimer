using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

/// <summary>Resolves personal profile names and administrator-only owner choices.</summary>
public interface ITransponderOwnerService
{
    /// <summary>Gets only the authenticated user's Statistics profile name.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The authenticated user's profile, including an explicit missing name.</returns>
    Task<OwnTransponderProfile> GetOwnProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves historical owner IDs for administrators.</summary>
    /// <param name="userIds">At most 100 owner identifiers.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Display details including unavailable historical accounts.</returns>
    Task<IReadOnlyList<TransponderOwnerView>> ResolveAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Searches Statistics profile names and Keycloak names for timing administrators.</summary>
    /// <param name="query">A search term of 2 to 100 characters.</param>
    /// <param name="profileOffset">The Statistics continuation offset, or null to skip an exhausted source.</param>
    /// <param name="keycloakOffset">The Keycloak continuation offset, or null to skip an exhausted source.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A bounded page with independent continuation offsets.</returns>
    Task<TransponderOwnerPage> SearchAsync(string query, int? profileOffset = 0, int? keycloakOffset = 0, CancellationToken cancellationToken = default);
}
