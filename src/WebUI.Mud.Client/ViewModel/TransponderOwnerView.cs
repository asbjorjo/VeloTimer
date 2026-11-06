namespace VeloTime.WebUI.Mud.Client.ViewModel;

/// <summary>The authenticated owner's display name, without directory identity details.</summary>
/// <param name="UserId">The authenticated subject identifier.</param>
/// <param name="ProfileName">The Statistics profile name, or null when no profile exists.</param>
public sealed record OwnTransponderProfile(Guid UserId, string? ProfileName)
{
    /// <summary>Gets the name displayed when registering a personal transponder.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ProfileName) ? "No statistics profile" : ProfileName;
}

/// <summary>Owner identity details restricted to administrators.</summary>
/// <param name="UserId">The stable identifier used for ownership mutations.</param>
/// <param name="ProfileName">The Statistics profile name, when present.</param>
/// <param name="FirstName">The Keycloak first name, when present.</param>
/// <param name="LastName">The Keycloak last name, when present.</param>
/// <param name="AccountAvailable">Whether the Keycloak account still exists.</param>
public sealed record TransponderOwnerView(Guid UserId, string? ProfileName, string? FirstName, string? LastName, bool AccountAvailable)
{
    /// <summary>Gets a human-readable label without using the UUID as the name.</summary>
    public string DisplayName
    {
        get
        {
            var profile = string.IsNullOrWhiteSpace(ProfileName) ? "No statistics profile" : ProfileName;
            if (!AccountAvailable) return $"{profile} (User unavailable)";
            var fullName = string.Join(" ", new[] { FirstName, LastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
            return $"{profile} ({(fullName.Length == 0 ? "Name not set" : fullName)})";
        }
    }
}

/// <summary>A bounded page combining Statistics and Keycloak owner search results.</summary>
/// <param name="Owners">At most 20 distinct owner choices.</param>
/// <param name="NextProfileOffset">The next Statistics search offset, or null when exhausted.</param>
/// <param name="NextKeycloakOffset">The next Keycloak search offset, or null when exhausted.</param>
public sealed record TransponderOwnerPage(IReadOnlyList<TransponderOwnerView> Owners, int? NextProfileOffset, int? NextKeycloakOffset);
