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
    [Fact]
    public async Task AuthenticationResetCancelsOldLoad_AndLateCompletionCannotOverwriteNewUser()
    {
        var service = new PendingPreferences();
        var browser = new Browser();
        var state = new DisplayPreferenceState(service, browser);
        var first = state.InitializeAsync(true, default);
        var old = await service.NextRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.ResetForAuthenticationChange();
        Assert.False(state.Effective.IsReady);
        Assert.True(old.Token.IsCancellationRequested);
        service.NextRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = state.InitializeAsync(true, default);
        var current = await service.NextRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        current.Completion.SetResult(new("ft", "mph", "utc", null));
        await second;
        old.Completion.SetResult(new("m", "m/s", "browser", null)); // intentionally ignores cancellation
        await first;
        Assert.Equal("ft", state.Effective.Preference.DistanceUnit);
        Assert.Equal("UTC", state.Effective.ZoneLabel);
        Assert.Equal("nb-NO", state.Effective.NumberCulture.Name);
        Assert.Equal(1, browser.Imports);
        state.ResetForAuthenticationChange();
        await state.InitializeAsync(false, default);
        Assert.Equal("m", state.Effective.Preference.DistanceUnit);
        Assert.Equal("browser", state.Effective.Preference.TimeMode);
        Assert.Equal("Europe/Oslo", state.Effective.ZoneLabel);
    }

    [Fact]
    public async Task AuthenticationResetDuringSaveRejectsLateResponseWithoutRestoringOldSettings()
    {
        var service = new PendingPreferences();
        var state = new DisplayPreferenceState(service, new Browser());
        await state.InitializeAsync(false, default);
        var save = state.SaveAsync(new("ft", "mph", "utc", null), default);
        state.ResetForAuthenticationChange();
        await state.InitializeAsync(false, default);
        service.SaveCompletion.SetResult(new("ft", "mph", "utc", null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        Assert.Equal("m", state.Effective.Preference.DistanceUnit);
        Assert.Equal("browser", state.Effective.Preference.TimeMode);
    }

    [Fact]
    public async Task RetryCancelsPendingInitialization_OnlyNewAttemptMayApply()
    {
        var service = new PendingPreferences();
        var state = new DisplayPreferenceState(service, new Browser());
        var first = state.InitializeAsync(true, default);
        var old = await service.NextRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.NextRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = state.RetryAsync(true, default);
        var current = await service.NextRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(old.Token.IsCancellationRequested);
        current.Completion.SetResult(new("ft", "mph", "utc", null));
        await retry;
        old.Completion.SetResult(new("m", "m/s", "browser", null));
        await first;
        Assert.Equal("ft", state.Effective.Preference.DistanceUnit);
    }

    [Fact]
    public async Task SuccessfulSaveSupersedesPendingInitialization_AndLateReadCannotRestoreOldSettings()
    {
        var service = new PendingPreferences();
        var state = new DisplayPreferenceState(service, new Browser());
        var load = state.InitializeAsync(true, default);
        var pending = await service.NextRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var save = state.SaveAsync(new("ft", "mph", "utc", null), default);
        service.SaveCompletion.SetResult(new("ft", "mph", "utc", null));
        await save;

        Assert.True(pending.Token.IsCancellationRequested);
        Assert.Equal("ft", state.Effective.Preference.DistanceUnit);
        pending.Completion.SetResult(new("m", "m/s", "browser", null)); // deliberately ignores cancellation
        await load;

        Assert.Equal("ft", state.Effective.Preference.DistanceUnit);
        Assert.Equal("UTC", state.Effective.ZoneLabel);
    }

    private sealed record PendingRead(CancellationToken Token, TaskCompletionSource<DisplayPreferenceDTO> Completion);
    private sealed class PendingPreferences : IDisplayPreferenceService
    {
        internal TaskCompletionSource<PendingRead> NextRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<DisplayPreferenceDTO> SaveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<DisplayPreferenceDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
            NextRead.SetResult(new(cancellationToken, completion));
            return completion.Task;
        }
        public Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request, CancellationToken cancellationToken = default) =>
            SaveCompletion.Task;
        public Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
