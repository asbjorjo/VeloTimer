using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.WebUI.Mud.Client.Layout;
using VeloTime.WebUI.Mud.Client;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class MainLayoutCacheIdentityTests
{
    [Fact]
    public void IdentityKeyIncludesExactIssuerSubjectSession_AndAnonymousDoesNotShareSignedInKey()
    {
        var method = typeof(MainLayout).GetMethod("IdentityKey", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? Key(System.Security.Claims.ClaimsPrincipal user) => method.Invoke(null, [new AuthenticationState(user)]);
        var keys = new[]
        {
            Key(WebUiCacheTestHost.Principal("https://one", "same", "sid1")),
            Key(WebUiCacheTestHost.Principal("https://one", "same", "sid2")),
            Key(WebUiCacheTestHost.Principal("https://two", "same", "sid1")),
            Key(WebUiCacheTestHost.Principal("https://one", "other", "sid1"))
        };
        Assert.Equal(4, keys.Distinct().Count());
        Assert.Null(Key(new(new System.Security.Claims.ClaimsIdentity())));
        Assert.Equal(keys[0], Key(WebUiCacheTestHost.Principal("https://one", "same", "sid1", admin: false)));
    }

    [Fact]
    public async Task IdentityTransitionWhileLayoutIsLoading_QueuesNewLoadAndRejectsOldSnapshot()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRazorComponents();
        builder.Services.AddLogging();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.AddMudServices();
        builder.Services.AddAuthorization(AdministrationPolicies.Configure);
        builder.Services.AddCascadingAuthenticationState();
        var authentication = new WebUiCacheTestHost.MutableAuthenticationState
        { User = WebUiCacheTestHost.Principal(subject: "alice") };
        builder.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        builder.Services.AddSingleton<NavigationManager, Navigation>();
        builder.Services.AddSingleton<IJSRuntime, Browser>();
        var preferences = new PendingPreferences();
        builder.Services.AddSingleton<IDisplayPreferenceService>(preferences);
        builder.Services.AddScoped<DisplayPreferenceState>();
        builder.Services.AddScoped<UnsavedChangesTracker>();
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        MainLayout? layout = null;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<LayoutHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(LayoutHost.Capture)] = (Action<MainLayout>)(value => layout = value) })));
        Task? initial = null;
        await renderer.Dispatcher.InvokeAsync(() => { initial = Invoke(layout!, "LoadPreferences", false); });
        var old = await preferences.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        preferences.ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        authentication.User = WebUiCacheTestHost.Principal(subject: "bob");
        await renderer.Dispatcher.InvokeAsync(() => Invoke(layout!, "HandleAuthenticationChange",
            Task.FromResult(new AuthenticationState(authentication.User))));
        var state = scope.ServiceProvider.GetRequiredService<DisplayPreferenceState>();
        Assert.False(state.Effective.IsReady);
        Assert.True(old.Token.IsCancellationRequested);
        old.Completion.SetResult(new("m", "m/s", "browser", null));
        await initial!.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await preferences.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        current.Completion.SetResult(new("ft", "mph", "utc", null));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await state.InitializeAsync(true, default);
            Assert.Equal("ft", state.Effective.Preference.DistanceUnit);
            Assert.Contains("Body for current user", root.ToHtmlString());
        });
        Assert.Equal(2, preferences.Reads);
    }

    private static Task Invoke(MainLayout layout, string method, object argument) =>
        (Task)typeof(MainLayout).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(layout, [argument])!;

    public sealed class LayoutHost : ComponentBase
    {
        [Parameter] public Action<MainLayout>? Capture { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MainLayout>(0);
            builder.AddAttribute(1, nameof(MainLayout.Body), (RenderFragment)(body => body.AddContent(0, "Body for current user")));
            builder.AddComponentReferenceCapture(2, component => Capture?.Invoke((MainLayout)component));
            builder.CloseComponent();
        }
    }

    private sealed record Read(CancellationToken Token, TaskCompletionSource<DisplayPreferenceDTO> Completion);
    private sealed class PendingPreferences : IDisplayPreferenceService
    {
        internal int Reads;
        internal TaskCompletionSource<Read> ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            var completion = new TaskCompletionSource<DisplayPreferenceDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadStarted.SetResult(new(cancellationToken, completion));
            return completion.Task;
        }
        public Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://localhost/", "https://localhost/profile");
    }
    private sealed class Browser : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "browserPresentation")
                return ValueTask.FromResult(System.Text.Json.JsonSerializer.Deserialize<TValue>("""{"TimeZone":"Europe/Oslo","Locale":"nb-NO"}""")!);
            throw new JSDisconnectedException("Static rendering has no browser.");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
