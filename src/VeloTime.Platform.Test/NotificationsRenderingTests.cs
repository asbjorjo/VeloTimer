using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class NotificationsRenderingTests
{
    private static NotificationPresentationDTO Notice(NotificationSeverity severity = NotificationSeverity.Warning,
        long version = 1) => new(Guid.Parse("10000000-0000-0000-0000-000000000001"), version,
            new(new("English <script>", "<b>plain text</b>"), new("Norsk <script>", "<b>ren tekst</b>")),
            severity, DateTimeOffset.UtcNow.AddHours(-1), null,
            severity == NotificationSeverity.Info ?
                new([NotificationDismissalScope.Session, NotificationDismissalScope.Permanent], NotificationDismissalScope.Permanent, null) :
                new([NotificationDismissalScope.Duration, NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
                    NotificationDismissalScope.Duration, severity == NotificationSeverity.Error ? 3600 : 86400));

    [Theory]
    [InlineData("en", NotificationSeverity.Warning, "Hide for 1 day(s)")]
    [InlineData("en", NotificationSeverity.Error, "Hide for 1 hour(s)")]
    [InlineData("en", NotificationSeverity.Info, "Hide permanently")]
    [InlineData("nb-NO", NotificationSeverity.Warning, "Skjul i 1 dag(er)")]
    public async Task SnackbarLocalizesPlainTextAndAllAllowedActions(string culture, NotificationSeverity severity, string expected)
    {
        await using var provider = Services().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var root = await renderer.RenderComponentAsync<NotificationSnackbar>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(NotificationSnackbar.Notice)] = Notice(severity),
                [nameof(NotificationSnackbar.HasSession)] = true,
                [nameof(NotificationSnackbar.Settings)] = DisplayTestRendering.Utc,
                [nameof(NotificationSnackbar.Dismiss)] = (Func<NotificationDismissalScope, Task>)(_ => Task.CompletedTask)
            }));
            return root.ToHtmlString();
        });
        Assert.Contains(expected, html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>plain", html);
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains(culture == "nb-NO" ? "Skjul for denne innloggingen" : "Hide for this login", html);
    }

    [Fact]
    public async Task AnonymousHasTabActionEvenWhenPermanentIsForbidden()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var notice = Notice() with { DismissalPolicy = new([NotificationDismissalScope.Session], NotificationDismissalScope.Session, null) };
            var root = await renderer.RenderComponentAsync<NotificationSnackbar>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(NotificationSnackbar.Notice)] = notice, [nameof(NotificationSnackbar.Anonymous)] = true }));
            return root.ToHtmlString();
        });
        Assert.Contains("Hide for this browser tab", html);
        Assert.DoesNotContain("Hide permanently", html);
        Assert.DoesNotContain("Hide for this login", html);
    }

    [Fact]
    public async Task MissingSessionHidesSessionActionsWithoutSavingFallback()
    {
        var saves = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            NotificationSnackbar? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationSnackbar>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Captured"] = (Action<NotificationSnackbar>)(value => component = value),
                ["Parameters"] = new Dictionary<string, object?>
                {
                    [nameof(NotificationSnackbar.Notice)] = Notice() with
                    {
                        DismissalPolicy = new([NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
                            NotificationDismissalScope.Session, null)
                    },
                    [nameof(NotificationSnackbar.Dismiss)] = (Func<NotificationDismissalScope, Task>)(_ => { saves++; return Task.CompletedTask; })
                }
            }));
            Assert.DoesNotContain("Hide for this login", root.ToHtmlString());
            Assert.Contains("Hide permanently", root.ToHtmlString());
            Assert.Equal(0, saves);
            await Call(component!, "Save", NotificationDismissalScope.Session);
            Assert.Equal(0, saves);
            await Call(component!, "Save", NotificationDismissalScope.Permanent);
            Assert.Equal(1, saves);
        });
    }

    [Fact]
    public async Task MissingSessionKeepsAccountRestoreButBlocksCombinedRestore()
    {
        var item = new NotificationDismissalListItemDTO(Notice(),
            new(NotificationDismissalScope.Permanent, DateTimeOffset.UtcNow, null),
            new(NotificationDismissalScope.Session, DateTimeOffset.UtcNow, null));
        var restores = new List<NotificationRestoreScope>();
        var services = Services();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((method, args) => method.Name switch
        {
            nameof(INotificationsService.DismissalsAsync) => (IReadOnlyList<NotificationDismissalListItemDTO>)new[] { item },
            nameof(INotificationsService.RestoreAsync) => Restore(args),
            _ => throw new NotSupportedException(method.Name)
        }));
        RestoreNotificationDismissalDTO Restore(object?[] args)
        {
            restores.Add((NotificationRestoreScope)args[1]!);
            return new(new(item.Notification.Id, DateTimeOffset.UtcNow, null, item.CurrentSession), false);
        }
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NotificationUiState>().SetAuthenticated(true, false);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            NotificationDismissals? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationDismissals>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationDismissals>)(value => component = value) }));
            await Call(component!, "Load"); Rerender(component!);
            Assert.Contains("Clear account dismissal", root.ToHtmlString());
            Assert.DoesNotContain("Clear current-login dismissal", root.ToHtmlString());
            Assert.DoesNotContain("Clear account and current-login dismissals", root.ToHtmlString());
            await Call(component!, "Restore", item, NotificationRestoreScope.AccountAndCurrentSession);
            Assert.Empty(restores);
            Assert.Single((IReadOnlyList<NotificationDismissalListItemDTO>)Field(component!, "_items")!);
            await Call(component!, "Restore", item, NotificationRestoreScope.Account);
            Assert.Equal(new[] { NotificationRestoreScope.Account }, restores);
        });
    }

    [Fact]
    public async Task FailedSaveStaysVisibleAndAllowsRetryWithoutFalseSavedMessage()
    {
        var tries = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationSnackbar? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationSnackbar>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Captured"] = (Action<NotificationSnackbar>)(value => component = value),
                ["Parameters"] = new Dictionary<string, object?>
                {
                    [nameof(NotificationSnackbar.Notice)] = Notice(),
                    [nameof(NotificationSnackbar.HasSession)] = true,
                    [nameof(NotificationSnackbar.Dismiss)] = (Func<NotificationDismissalScope, Task>)(_ =>
                    {
                        if (++tries == 1) throw new NotificationUiException(503, "notifications.dependency_unavailable");
                        return Task.CompletedTask;
                    })
                }
            }));
            await Call(component!, "Save", NotificationDismissalScope.Duration);
            Rerender(component!);
            Assert.Contains("Nothing was dismissed", root.ToHtmlString());
            Assert.Contains("English", root.ToHtmlString());
            await Call(component!, "Save", NotificationDismissalScope.Session);
            Rerender(component!);
            Assert.DoesNotContain("Nothing was dismissed", root.ToHtmlString());
            Assert.Equal(2, tries);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SnackbarJsFailureReflectsModeAtSaveStart(bool anonymous, bool nextAnonymous)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = Services().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            NotificationSnackbar? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationSnackbar>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Captured"] = (Action<NotificationSnackbar>)(value => component = value),
                ["Parameters"] = new Dictionary<string, object?>
                {
                    [nameof(NotificationSnackbar.Notice)] = Notice(),
                    [nameof(NotificationSnackbar.Anonymous)] = anonymous,
                    [nameof(NotificationSnackbar.Dismiss)] = (Func<NotificationDismissalScope, Task>)(_ => pending.Task)
                }
            }));
            var save = Call(component!, "Save", NotificationDismissalScope.Permanent);
            typeof(NotificationSnackbar).GetProperty(nameof(NotificationSnackbar.Anonymous))!.SetValue(component, nextAnonymous);
            pending.SetException(new JSException("network or storage failure"));
            await save;
            Rerender(component!);
            var html = root.ToHtmlString();
            Assert.Contains(anonymous ? "Tab storage is unavailable" : "Notifications could not be updated", html);
            Assert.DoesNotContain(anonymous ? "Notifications could not be updated" : "Tab storage is unavailable", html);
        });
    }

    [Fact]
    public async Task AuthenticatedBrowserSnapshotUsesAccountDismissalWithoutSession()
    {
        var notice = Notice();
        var now = DateTimeOffset.UtcNow;
        var js = new NotificationsUiTransportTests.Browser();
        js.Replies.Enqueue((200, new ActiveNotificationsDTO(now, now, now.AddMinutes(5), null, [notice], null), true, false));
        js.Replies.Enqueue((200, new NotificationDismissalDTO(notice.Id, now,
            new(NotificationDismissalScope.Permanent, now, null), null), true, false));
        js.Replies.Enqueue((200, new ActiveNotificationsDTO(now, now, now.AddMinutes(5), null, [], null), true, false));
        var services = Services();
        services.AddSingleton<INotificationsService>(new NotificationsBrowserService(js));
        services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthentication());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationsHost? host = null;
            await renderer.RenderComponentAsync<Capture<NotificationsHost>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationsHost>)(value => host = value) }));
            await host!.Refresh();
            Assert.False((bool)Field(host, "_anonymous")!);
            Assert.False(provider.GetRequiredService<NotificationUiState>().HasSession);
            Assert.Single(provider.GetRequiredService<ISnackbar>().ShownSnackbars);
            await Call(host, "Dismiss", notice, NotificationDismissalScope.Permanent, (int)Field(host, "_epoch")!);
            Assert.Contains(js.Requests, r => r.Method == "PUT" && r.Path.EndsWith("/dismissal") &&
                r.Body == "{\"scope\":\"permanent\"}");
        });
    }

    [Fact]
    public async Task HostDeduplicatesByUuidVersionAndRemovesEndedOrLoggedOutNotices()
    {
        var current = Notice();
        var services = Services();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((method, _) =>
        {
            Assert.Equal(nameof(INotificationsService.ActiveAsync), method.Name);
            var now = DateTimeOffset.UtcNow;
            // Fresh object/collection each time, just as after JSON deserialization.
            return new NotificationUiSnapshot(new(now, now, now.AddMinutes(5), null, [current with { }], null), true);
        }));
        var authentication = new AnonymousAuthentication();
        services.AddSingleton<AuthenticationStateProvider>(authentication);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationsHost? component = null;
            await renderer.RenderComponentAsync<Capture<NotificationsHost>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationsHost>)(value => component = value) }));
            var snackbar = provider.GetRequiredService<ISnackbar>();
            await component!.Refresh();
            var first = Assert.Single(snackbar.ShownSnackbars);
            await component.Refresh();
            Assert.Same(first, Assert.Single(snackbar.ShownSnackbars));
            current = current with { Version = 2 };
            await component.Refresh();
            Assert.NotSame(first, Assert.Single(snackbar.ShownSnackbars));
            current = current with { EndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
            await component.Refresh();
            Assert.Empty(snackbar.ShownSnackbars);
            current = Notice();
            await component.Refresh();
            Assert.Single(snackbar.ShownSnackbars);
            await Call(component, "AuthenticationChanged", authentication.GetAuthenticationStateAsync());
            Assert.Empty(snackbar.ShownSnackbars);
        });
    }

    [Fact]
    public async Task HostSubscribesOnlyOnInteractiveRenderMarshalsCallbacksRestartsForAuthAndDisposes()
    {
        var source = new RecordingNotificationRefreshSource();
        var authenticated = true;
        var calls = 0;
        var services = Services();
        services.AddSingleton<INotificationRefreshSource>(source);
        services.AddSingleton<IJSRuntime>(new NotificationsUiTransportTests.Browser());
        services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthentication());
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((method, _) =>
        {
            Assert.Equal(nameof(INotificationsService.ActiveAsync), method.Name);
            calls++;
            var now = DateTimeOffset.UtcNow;
            return new NotificationUiSnapshot(new(now, now, now.AddMinutes(5), null,
                authenticated ? [Notice()] : [], null), authenticated);
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        NotificationsHost? host = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<Capture<NotificationsHost>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationsHost>)(value => host = value) }));
            Assert.Equal(0, source.Subscriptions);
            Assert.Equal(0, calls);
            await Call(host!, "OnAfterRenderAsync", true);
            await Call(host!, "OnAfterRenderAsync", false);
            Assert.Equal(1, source.Subscriptions);
            Assert.Equal([true], source.Activations);
            Assert.Equal(1, calls);
            Assert.Single(provider.GetRequiredService<ISnackbar>().ShownSnackbars);
        });
        // Real transport callbacks arrive outside the circuit/render dispatcher.
        await Task.Run(() => source.Callback!(NotificationRefreshKind.Refresh));
        Assert.Equal(2, calls);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await Call(host!, "AuthenticationChanged", Task.FromResult(new AuthenticationState(
                new(new System.Security.Claims.ClaimsIdentity()))));
            Assert.Equal([true, false, true], source.Activations);
            Assert.Empty(provider.GetRequiredService<ISnackbar>().ShownSnackbars);
            Assert.False(provider.GetRequiredService<NotificationUiState>().Authenticated);
            Assert.True((bool)Field(host!, "_awaitingAnonymous")!);
            // A still-authenticated cookie snapshot cannot resurrect content
            // after a provider logout. Normal snapshots (not probes) repair it.
            authenticated = false;
            await source.Callback!(NotificationRefreshKind.Refresh);
            Assert.False((bool)Field(host!, "_awaitingAnonymous")!);
            await host!.DisposeAsync();
            Assert.True(source.Disposed);
        });
        var before = calls;
        await Task.Run(() => source.Callback!(NotificationRefreshKind.Connected));
        Assert.Equal(before, calls);
    }

    [Fact]
    public async Task WasmNegotiationStallDoesNotDelayFirstBffSnapshot()
    {
        using var server = new TestServer(new WebHostBuilder().Configure(_ => { }));
        var negotiationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new WasmNotificationRefreshSource(() => new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ =>
                    new StallingNegotiationHandler(server.CreateHandler(), negotiationEntered);
            }).Build());
        var snapshots = 0;
        var services = Services();
        services.AddSingleton<INotificationRefreshSource>(source);
        services.AddSingleton<IJSRuntime>(new NotificationsUiTransportTests.Browser());
        services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthentication());
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((method, _) =>
        {
            Assert.Equal(nameof(INotificationsService.ActiveAsync), method.Name);
            Interlocked.Increment(ref snapshots);
            var now = DateTimeOffset.UtcNow;
            return new NotificationUiSnapshot(new(now, now, now.AddMinutes(5), null, [], null), false);
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        NotificationsHost? host = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<Capture<NotificationsHost>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationsHost>)(value => host = value) }));
            await Call(host!, "OnAfterRenderAsync", true);
        });

        await negotiationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref snapshots));
        Assert.True((bool)Field(host!, "_hasSnapshot")!);
        await renderer.Dispatcher.InvokeAsync(async () => await host!.DisposeAsync());
    }

    [Theory]
    [InlineData(NotificationRefreshKind.Connected)]
    [InlineData(NotificationRefreshKind.TransportFailed)]
    public async Task FirstTransportEventOwnsExactlyOneSnapshotAndFailureDoesNotBlockContent(NotificationRefreshKind kind)
    {
        var source = new RecordingNotificationRefreshSource { ActivationKind = kind };
        var calls = 0;
        var services = Services();
        services.AddSingleton<INotificationRefreshSource>(source);
        services.AddSingleton<IJSRuntime>(new NotificationsUiTransportTests.Browser());
        services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthentication());
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((_, _) =>
        {
            calls++;
            var now = DateTimeOffset.UtcNow;
            return new NotificationUiSnapshot(new(now, now, now.AddMinutes(5), null, [Notice()], null), true);
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationsHost? host = null;
            await renderer.RenderComponentAsync<Capture<NotificationsHost>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationsHost>)(value => host = value) }));
            await Call(host!, "OnAfterRenderAsync", true);
            Assert.Equal(1, calls);
            Assert.Single(provider.GetRequiredService<ISnackbar>().ShownSnackbars);
            Assert.Equal(kind == NotificationRefreshKind.TransportFailed, Field(host!, "_transportFailed"));
            await source.Callback!(NotificationRefreshKind.Connected);
            Assert.Equal(2, calls);
            Assert.False((bool)Field(host!, "_transportFailed")!);
        });
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task HostAuthorizationFailureClearsPrivateSnapshotAndShowsError(int status)
    {
        var rejected = false;
        var services = Services();
        services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthentication());
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((_, _) =>
        {
            if (rejected) throw new NotificationUiException(status, "notifications.forbidden");
            var now = DateTimeOffset.UtcNow;
            return new NotificationUiSnapshot(new(now, now, now.AddMinutes(5), null, [Notice()], null), true);
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationsHost? host = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationsHost>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationsHost>)(value => host = value) }));
            await host!.Refresh();
            Assert.Single(provider.GetRequiredService<ISnackbar>().ShownSnackbars);
            rejected = true;
            await host.Refresh();
            Assert.Empty(provider.GetRequiredService<ISnackbar>().ShownSnackbars);
            Assert.False(provider.GetRequiredService<NotificationUiState>().Authenticated);
            Assert.False((bool)Field(host, "_hasSnapshot")!);
            Assert.Contains("role=\"alert\"", root.ToHtmlString());
        });
    }

    [Fact]
    public async Task ProfileRestoresExactlySelectedLayerAndClearsImmediatelyOnLogout()
    {
        var notice = Notice();
        var session = new NotificationDismissalStateDTO(NotificationDismissalScope.Session, DateTimeOffset.UtcNow, null);
        var item = new NotificationDismissalListItemDTO(notice,
            new(NotificationDismissalScope.Permanent, DateTimeOffset.UtcNow, null), session);
        var services = Services();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((method, args) => method.Name switch
        {
            nameof(INotificationsService.DismissalsAsync) => (IReadOnlyList<NotificationDismissalListItemDTO>)new[] { item },
            nameof(INotificationsService.RestoreAsync) => Restore(args),
            _ => throw new NotSupportedException()
        }));
        RestoreNotificationDismissalDTO Restore(object?[] args)
        {
            Assert.Equal(notice.Id, args[0]); Assert.Equal(NotificationRestoreScope.Account, args[1]);
            return new(new(notice.Id, DateTimeOffset.UtcNow, null, session), false);
        }
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            NotificationDismissals? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationDismissals>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationDismissals>)(value => component = value) }));
            await Call(component!, "Load"); Rerender(component!);
            Assert.Contains("English", root.ToHtmlString());
            await Call(component!, "Restore", item, NotificationRestoreScope.Account); Rerender(component!);
            var remaining = Assert.Single((IReadOnlyList<NotificationDismissalListItemDTO>)Field(component!, "_items")!);
            Assert.Null(remaining.Account); Assert.Equal(session, remaining.CurrentSession);
            Assert.Contains("another dismissal still applies", root.ToHtmlString());
            provider.GetRequiredService<NotificationUiState>().SetAuthenticated(false);
            Rerender(component!);
            Assert.DoesNotContain("English", root.ToHtmlString());
        });
    }

    [Fact]
    public async Task DismissalRefreshRetryAndRestoreAreBlockedWhenLoggedOut()
    {
        var serviceCalls = new List<string>();
        var services = Services();
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<INotificationsService>((method, _) =>
        {
            serviceCalls.Add(method.Name);
            throw new InvalidOperationException($"Unexpected request: {method.Name}");
        }));
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NotificationUiState>().SetAuthenticated(false);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationDismissals? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationDismissals>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationDismissals>)(value => component = value) }));
            await Call(component!, "Load"); // Refresh callback
            Rerender(component!);
            var html = root.ToHtmlString();
            Assert.True(html.Split("disabled", StringSplitOptions.None).Length - 1 >= 2); // Refresh and Retry.
            Assert.Empty(serviceCalls);

            await Call(component!, "Load"); // Retry callback
            await Call(component!, "Restore",
                new NotificationDismissalListItemDTO(Notice(), null, null), NotificationRestoreScope.Account);
            Assert.Empty(serviceCalls);
            Assert.Equal("SignInRequired", Field(component!, "_error"));
            Assert.False((bool)Field(component!, "_busy")!);
            Assert.False((bool)Field(component!, "_pending")!);
        });
    }

    [Fact]
    public async Task InFlightDismissalLoadCannotRepublishPrivateItemsAfterLogout()
    {
        var notice = Notice();
        var item = new NotificationDismissalListItemDTO(notice,
            new(NotificationDismissalScope.Permanent, DateTimeOffset.UtcNow, null), null);
        var pending = new TaskCompletionSource<IReadOnlyList<NotificationDismissalListItemDTO>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingService = new BlockingDismissalsService(pending);
        var services = Services();
        services.AddSingleton<INotificationsService>(blockingService);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NotificationDismissals? component = null;
            var root = await renderer.RenderComponentAsync<Capture<NotificationDismissals>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Captured"] = (Action<NotificationDismissals>)(value => component = value) }));
            var load = Call(component!, "Load");
            Assert.True((bool)Field(component!, "_busy")!);

            provider.GetRequiredService<NotificationUiState>().SetAuthenticated(false);
            Rerender(component!);
            Assert.Empty((IReadOnlyList<NotificationDismissalListItemDTO>)Field(component!, "_items")!);
            Assert.DoesNotContain("English", root.ToHtmlString());

            pending.SetResult([item]);
            await load;
            await Call(component!, "Load"); // A manual Refresh/Retry after logout is still rejected.
            await Call(component!, "Restore", item, NotificationRestoreScope.Account);
            Rerender(component!);
            Assert.Empty((IReadOnlyList<NotificationDismissalListItemDTO>)Field(component!, "_items")!);
            Assert.DoesNotContain("English", root.ToHtmlString());
            Assert.Equal("SignInRequired", Field(component!, "_error"));
            Assert.Equal(1, blockingService.DismissalCalls);
        });
    }

    [Fact]
    public void AuthoringAppliesSeverityDefaultsOnlyToNewNoticesAndKeepsExplicitOverrideOnEdit()
    {
        var editor = new VeloTime.WebUI.Mud.Client.Pages.Admin.Notifications();
        typeof(PresentationComponentBase).GetProperty(nameof(PresentationComponentBase.Display))!.SetValue(editor, DisplayTestRendering.Utc);
        Invoke(editor, "New");
        Assert.Equal(NotificationDismissalScope.Permanent, Field(editor, "_default"));
        Assert.False((bool)Field(editor, "_duration")!);
        Invoke(editor, "SeverityChanged", NotificationSeverity.Warning);
        Assert.Equal(86400, Field(editor, "_seconds"));
        Assert.Equal(NotificationDismissalScope.Duration, Field(editor, "_default"));
        Invoke(editor, "SeverityChanged", NotificationSeverity.Error);
        Assert.Equal(3600, Field(editor, "_seconds"));
        var notice = Notice() with { DismissalPolicy = new([NotificationDismissalScope.Session], NotificationDismissalScope.Session, null) };
        Invoke(editor, "Edit", new NotificationAdministrationDTO(notice.Id, 9, notice.Content, notice.Severity,
            new(NotificationAudienceKind.User, RecipientIssuer: "https://Exact.invalid/", RecipientSubject: "Subject "),
            notice.StartsAtUtc, notice.EndsAtUtc, notice.DismissalPolicy, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Invoke(editor, "SeverityChanged", NotificationSeverity.Info);
        Assert.Equal(NotificationDismissalScope.Session, Field(editor, "_default"));
        Assert.False((bool)Field(editor, "_permanent")!);
        Assert.Equal("https://Exact.invalid/", Field(editor, "_issuer"));
        Assert.Equal("Subject ", Field(editor, "_subject"));
        Assert.Equal(new[] { NotificationDismissalScope.Session }, Invoke(editor, "Allowed"));
        Assert.Equal(notice.StartsAtUtc, ((ZonedInstantDraft)Field(editor, "_start")!).Resolve(DisplayTestRendering.Utc));
    }

    private static object? Field(object component, string name) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component);
    private static object? Invoke(object component, string name, params object[] args) =>
        component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddMudServices();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddSingleton<NotificationUiState>();
        services.AddSingleton<INotificationRefreshSource, RecordingNotificationRefreshSource>();
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(PlatformServiceTests.TestProxy.Create<IJSRuntime>((_, _) => null));
        return services;
    }
    private static Task Call(object component, string method, params object[] args) =>
        (Task)component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args)!;
    private static void Rerender(ComponentBase component) =>
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
    private sealed class AnonymousAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new(new System.Security.Claims.ClaimsIdentity())));
    }
    private sealed class StallingNegotiationHandler(HttpMessageHandler inner,
        TaskCompletionSource negotiationEntered) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal))
            {
                negotiationEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://ui.invalid/", "https://ui.invalid/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    public sealed class Capture<T> : ComponentBase where T : IComponent
    {
        [Parameter] public Action<T>? Captured { get; set; }
        [Parameter] public Dictionary<string, object?> Parameters { get; set; } = [];
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<T>(0);
            builder.AddMultipleAttributes(1, Parameters.Select(p => new KeyValuePair<string, object>(p.Key, p.Value!)));
            builder.AddComponentReferenceCapture(2, value => Captured?.Invoke((T)value));
            builder.CloseComponent();
        }
    }
    private sealed class BlockingDismissalsService(
        TaskCompletionSource<IReadOnlyList<NotificationDismissalListItemDTO>> pending) : INotificationsService
    {
        public int DismissalCalls { get; private set; }
        public Task<IReadOnlyList<NotificationDismissalListItemDTO>> DismissalsAsync(CancellationToken ct)
        { DismissalCalls++; return pending.Task; }
        public Task<NotificationUiSnapshot> ActiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<NotificationAdministrationDTO>> AdministrationAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<NotificationDismissalDTO> DismissAsync(Guid id, NotificationDismissalScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<RestoreNotificationDismissalDTO> RestoreAsync(Guid id, NotificationRestoreScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<NotificationAdministrationDTO> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<NotificationAdministrationDTO> CreateAsync(CreateNotificationRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<NotificationAdministrationDTO> UpdateAsync(Guid id, UpdateNotificationRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<NotificationAdministrationDTO> EndAsync(Guid id, long version, CancellationToken ct) => throw new NotSupportedException();
    }
}
