namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>A user's Statistics display name; null means that no profile exists.</summary>
/// <param name="UserId">The authenticated subject identifier.</param>
/// <param name="ProfileName">The Statistics profile name, when present.</param>
public sealed record OwnerProfileDTO(Guid UserId, string? ProfileName);

/// <summary>A bounded page of Statistics owner names.</summary>
/// <param name="Profiles">The matching profiles.</param>
/// <param name="NextOffset">The next page offset, or null when complete.</param>
public sealed record OwnerProfilePage(IReadOnlyList<OwnerProfileDTO> Profiles, int? NextOffset);

/// <summary>A bounded batch of owner identifiers to resolve.</summary>
/// <param name="UserIds">The subject identifiers, at most 100 per request.</param>
public sealed record ResolveOwnerProfilesRequest(IReadOnlyList<Guid> UserIds);
