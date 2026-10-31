namespace VeloTime.WebUI.Mud.Client.ViewModel;

public sealed record AuthDebugView
{
    public bool HasAccessToken { get; init; }
    public IReadOnlyList<string> Audiences { get; init; } = [];
    public IReadOnlyList<string> RealmRoles { get; init; } = [];
    public string? Issuer { get; init; }
    public string? AuthorizedParty { get; init; }
    public string? Scope { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
