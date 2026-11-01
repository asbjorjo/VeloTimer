namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class UnsavedChangesTracker
{
    private readonly Dictionary<Guid, Func<bool>> _checks = [];

    public IDisposable Register(Func<bool> hasChanges)
    {
        var id = Guid.NewGuid();
        _checks[id] = hasChanges;
        return new Registration(() => _checks.Remove(id));
    }

    public bool HasUnsavedChangesExcept(Func<bool> excluded) => _checks.Values.Any(check => check != excluded && check());

    public bool HasUnsavedChanges => _checks.Values.Any(check => check());

    private sealed class Registration(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
