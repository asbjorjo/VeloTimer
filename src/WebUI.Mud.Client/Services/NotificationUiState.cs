namespace VeloTime.WebUI.Mud.Client.Services;

// Circuit/WASM scoped, presentation-only signals. Never contains a login identity.
public sealed class NotificationUiState
{
    public bool? Authenticated { get; private set; }
    public bool HasSession { get; private set; }
    public event Action? AuthenticationChanged;
    public event Action? RefreshRequested;
    public void SetAuthenticated(bool authenticated, bool hasSession = false)
    {
        hasSession &= authenticated;
        if (Authenticated == authenticated && HasSession == hasSession) return;
        Authenticated = authenticated;
        HasSession = hasSession;
        AuthenticationChanged?.Invoke();
    }
    public void Refresh() => RefreshRequested?.Invoke();
}
