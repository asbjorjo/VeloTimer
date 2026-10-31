using VeloTime.Module.Profiles.Interface.Data;
using Microsoft.JSInterop;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IDisplayPreferenceService
{
    Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default);
    Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Authenticated, antiforgery-protected BFF transport; no direct domain API calls.</summary>
public sealed class DisplayPreferenceHttpService(HttpClient client) : IDisplayPreferenceService
{
    public const string Route = "api/profile/preferences/display";
    public const string ZonesRoute = "api/profile/preferences/time-zones";
    public async Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.CacheControl = new() { NoStore = true };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DisplayPreferenceDTO>(cancellationToken)
            ?? throw new HttpRequestException("Missing preference snapshot.");
    }
    public async Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest value, CancellationToken cancellationToken = default)
    {
        using var response = await client.PutAsJsonAsync(Route, value, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DisplayPreferenceDTO>(cancellationToken)
            ?? throw new HttpRequestException("Missing saved preference snapshot.");
    }
    public async Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ZonesRoute);
        request.Headers.CacheControl = new() { NoStore = true };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TimeZoneOptionDTO>>(cancellationToken)
            ?? throw new HttpRequestException("Missing time-zone catalogue.");
    }
}

/// <summary>One state per interactive session. Only successful persisted snapshots become effective.</summary>
public sealed class DisplayPreferenceState(IDisplayPreferenceService service, IJSRuntime js)
{
    public EffectiveDisplaySettings Effective { get; private set; } = EffectiveDisplaySettings.Unresolved;
    public event Action? Changed;
    private string? _browserZone;
    private string? _browserLocale;
    private Task? _initialization;
    private CancellationTokenSource? _identityCancellation;
    private CancellationTokenSource? _initializationCancellation;
    private int _identityVersion;
    private bool _loaded;
    public Task InitializeAsync(bool signedIn, CancellationToken token)
    {
        if (_initialization is not null) return _initialization;
        _identityCancellation ??= new CancellationTokenSource();
        _initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _identityCancellation.Token);
        return _initialization = InitializeCoreAsync(signedIn, _identityVersion, _initializationCancellation, _initializationCancellation.Token);
    }

    public async Task RetryAsync(bool signedIn, CancellationToken token)
    {
        CancelInitialization();
        await InitializeAsync(signedIn, token);
    }

    private void CancelInitialization()
    {
        var cancellation = _initializationCancellation;
        _initializationCancellation = null;
        _initialization = null;
        // The attempt owns disposal until its async work has unwound.
        cancellation?.Cancel();
    }

    /// <summary>Clears effective settings and cancels work when the authenticated identity changes.</summary>
    public void ResetForAuthenticationChange()
    {
        _identityVersion++;
        _identityCancellation?.Cancel();
        _identityCancellation?.Dispose();
        _identityCancellation = null;
        CancelInitialization();
        _loaded = false;
        Effective = EffectiveDisplaySettings.Unresolved;
        Changed?.Invoke();
    }

    private async Task InitializeCoreAsync(bool signedIn, int identityVersion,
        CancellationTokenSource initializationCancellation, CancellationToken token)
    {
        try
        {
            if (_browserZone is null)
            {
                await using var module = await js.InvokeAsync<IJSObjectReference>("import", token, "./registrationDates.js");
                var browser = await module.InvokeAsync<BrowserPresentation>("browserPresentation", token);
                if (identityVersion != _identityVersion ||
                    !ReferenceEquals(_initializationCancellation, initializationCancellation)) return;
                token.ThrowIfCancellationRequested();
                _browserZone = browser.TimeZone;
                _browserLocale = browser.Locale;
            }
            var snapshot = !_loaded && signedIn ? await service.GetAsync(token) : Effective.Preference;
            if (identityVersion != _identityVersion ||
                !ReferenceEquals(_initializationCancellation, initializationCancellation)) return;
            token.ThrowIfCancellationRequested();
            Apply(snapshot);
            _loaded = true;
        }
        catch (OperationCanceledException) when (identityVersion != _identityVersion ||
            !ReferenceEquals(_initializationCancellation, initializationCancellation))
        {
            // An authentication transition, retry or successful save superseded this load.
        }
        finally
        {
            if (ReferenceEquals(_initializationCancellation, initializationCancellation))
                _initializationCancellation = null;
            initializationCancellation.Dispose();
        }
    }

    public async Task SaveAsync(UpdateDisplayPreferenceRequest value, CancellationToken token)
    {
        var identityVersion = _identityVersion;
        _ = EffectiveDisplaySettings.Create(new(value.DistanceUnit, value.SpeedUnit, value.TimeMode, value.TimeZoneId), _browserZone!, _browserLocale!);
        var saved = await service.SaveAsync(value, token);
        token.ThrowIfCancellationRequested();
        if (identityVersion != _identityVersion) throw new OperationCanceledException("The authenticated identity changed during the preference save.", token);
        // A late pending read must not restore preferences from before this save.
        CancelInitialization();
        _loaded = true;
        Apply(saved);
    }
    private void Apply(DisplayPreferenceDTO value)
    {
        Effective = EffectiveDisplaySettings.Create(value, _browserZone!, _browserLocale!);
        Changed?.Invoke();
    }
    private sealed record BrowserPresentation(string TimeZone, string Locale);
}
