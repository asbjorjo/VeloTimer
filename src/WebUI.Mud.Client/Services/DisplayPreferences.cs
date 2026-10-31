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
    private bool _loaded;
    public Task InitializeAsync(bool signedIn, CancellationToken token) => _initialization ??= InitializeCoreAsync(signedIn, token);
    public async Task RetryAsync(bool signedIn, CancellationToken token)
    {
        _initialization = null;
        await InitializeAsync(signedIn, token);
    }
    private async Task InitializeCoreAsync(bool signedIn, CancellationToken token)
    {
        if (_browserZone is null)
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", token, "./registrationDates.js");
            var browser = await module.InvokeAsync<BrowserPresentation>("browserPresentation", token);
            _browserZone = browser.TimeZone;
            _browserLocale = browser.Locale;
        }
        var snapshot = !_loaded && signedIn ? await service.GetAsync(token) : Effective.Preference;
        token.ThrowIfCancellationRequested();
        Apply(snapshot);
        _loaded = true;
    }
    public async Task SaveAsync(UpdateDisplayPreferenceRequest value, CancellationToken token)
    {
        _ = EffectiveDisplaySettings.Create(new(value.DistanceUnit, value.SpeedUnit, value.TimeMode, value.TimeZoneId), _browserZone!, _browserLocale!);
        var saved = await service.SaveAsync(value, token);
        token.ThrowIfCancellationRequested();
        Apply(saved);
    }
    private void Apply(DisplayPreferenceDTO value)
    {
        Effective = EffectiveDisplaySettings.Create(value, _browserZone!, _browserLocale!);
        Changed?.Invoke();
    }
    private sealed record BrowserPresentation(string TimeZone, string Locale);
}
