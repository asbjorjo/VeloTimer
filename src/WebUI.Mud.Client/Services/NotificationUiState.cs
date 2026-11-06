namespace VeloTime.WebUI.Mud.Client.Services;

// Circuit/WASM scoped, presentation-only signals. Never contains a login identity.
public sealed class NotificationUiState
{
    public bool? Authenticated { get; private set; }
    public event Action? AuthenticationChanged;
    public event Action? RefreshRequested;
    public void SetAuthenticated(bool authenticated)
    {
        if (Authenticated == authenticated) return;
        Authenticated = authenticated;
        AuthenticationChanged?.Invoke();
    }
    public void Refresh() => RefreshRequested?.Invoke();
}
