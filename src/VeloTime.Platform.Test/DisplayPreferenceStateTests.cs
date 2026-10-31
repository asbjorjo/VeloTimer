using System.Text.Json;
using Microsoft.JSInterop;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
namespace VeloTime.Platform.Test;
public sealed class DisplayPreferenceStateTests
{
    [Fact]
    public async Task InitializationIsShared_OnlyPersistedSuccessBecomesEffective_FailureDoesNotNotify()
    {
        var service = new Preferences(); var browser = new Browser(); var state = new DisplayPreferenceState(service, browser); var changes = 0; state.Changed += () => changes++;
        await Task.WhenAll(state.InitializeAsync(true, default), state.InitializeAsync(true, default));
        Assert.Equal(1, service.Reads); Assert.Equal(1, browser.Imports); Assert.Equal(1, changes); Assert.True(state.Effective.IsReady);
        Assert.Equal("Europe/Oslo", state.Effective.ZoneLabel); Assert.Equal("nb-NO", state.Effective.NumberCulture.Name);
        var before = state.Effective; service.Failure = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => state.SaveAsync(new("ft", "mph", "utc", null), default));
        Assert.Same(before, state.Effective); Assert.Equal(1, changes);
        service.Failure = false; service.Saved = new("m", "m/s", "utc", null);
        await state.SaveAsync(new("ft", "mph", "utc", null), default);
        Assert.Equal(service.Saved, state.Effective.Preference); Assert.Equal(2, changes); Assert.Equal("UTC", state.Effective.ZoneLabel);
    }
    [Fact]
    public async Task AnonymousInitializationDoesNotReadProfile_AndFailedInitializationCanRetry()
    {
        var service = new Preferences { Failure = true }; var state = new DisplayPreferenceState(service, new Browser());
        await Assert.ThrowsAsync<HttpRequestException>(() => state.InitializeAsync(true, default)); Assert.False(state.Effective.IsReady);
        service.Failure = false; await state.RetryAsync(true, default); Assert.True(state.Effective.IsReady); Assert.Equal(2, service.Reads);
        var anonymous = new DisplayPreferenceState(service, new Browser()); await anonymous.InitializeAsync(false, default); Assert.Equal(2, service.Reads); Assert.Equal("browser", anonymous.Effective.Preference.TimeMode);
    }
    private sealed class Preferences : IDisplayPreferenceService
    {
        public int Reads; public bool Failure; public DisplayPreferenceDTO Saved = new("ft", "mph", "utc", null);
        public Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default) { Reads++; return Failure ? Task.FromException<DisplayPreferenceDTO>(new HttpRequestException()) : Task.FromResult(new DisplayPreferenceDTO("m", "km/h", "browser", null)); }
        public Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request, CancellationToken cancellationToken = default) => Failure ? Task.FromException<DisplayPreferenceDTO>(new HttpRequestException()) : Task.FromResult(Saved);
        public Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Browser : IJSRuntime, IJSObjectReference
    {
        public int Imports;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "import") { Imports++; Assert.Equal("./registrationDates.js", Assert.Single(args!)); return ValueTask.FromResult((TValue)(object)this); }
            Assert.Equal("browserPresentation", identifier);
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>("{\"TimeZone\":\"Europe/Oslo\",\"Locale\":\"nb-NO\"}")!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
