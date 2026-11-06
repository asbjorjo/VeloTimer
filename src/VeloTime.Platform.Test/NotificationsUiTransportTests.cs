using System.Text.Json;
using Microsoft.JSInterop;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class NotificationsUiTransportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
    private static NotificationPresentationDTO Notice() => new(Guid.NewGuid(), 1,
        new(new("English", "<b>plain</b>"), new("Norsk", "Ren tekst")), NotificationSeverity.Info,
        Now, null, new([NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
            NotificationDismissalScope.Permanent, null));
    private static ActiveNotificationsDTO Page(string? next, params NotificationPresentationDTO[] notices) =>
        new(Now, Now, Now.AddMinutes(5), Now.AddMinutes(1), notices, next);

    [Fact]
    public async Task ActiveDrainsAllPagesBeforeReturning()
    {
        var first = Notice(); var second = Notice();
        var js = new Browser();
        js.Replies.Enqueue((200, Page("opaque", first), true, null));
        js.Replies.Enqueue((200, Page(null, second), true, null));
        await using var service = new NotificationsBrowserService(js);
        var result = await service.ActiveAsync(default);
        Assert.Equal(new[] { first.Id, second.Id }, result.Snapshot.Items.Select(n => n.Id));
        Assert.Null(result.Snapshot.ContinuationToken);
        Assert.True(result.Authenticated);
        Assert.True(result.HasSession);
        Assert.Contains("continuationToken=opaque", js.Requests[1].Path);
    }

    [Theory]
    [InlineData(true, true, true, true, false, true)]
    [InlineData(true, true, true, false, false, true)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, false, true, true, false, true)]
    [InlineData(true, true, false, false, true, true)]
    [InlineData(true, true, false, false, true, false)]
    public async Task OverlappingRequestCannotChangeCompletedActiveCapabilities(
        bool authenticated, bool hasSession, bool otherAuthenticated, bool otherHasSession, bool otherFails, bool admin)
    {
        var js = new Browser { DelayRequestNumber = 2 };
        js.Replies.Enqueue((200, Page("next", Notice()), authenticated, hasSession));
        js.Replies.Enqueue((200, Page(null, Notice()), authenticated, hasSession));
        js.Replies.Enqueue((otherFails ? 403 : 200,
            otherFails ? new { code = "notifications.request_failed" } : admin
                ? (object)new NotificationAdministrationPageDTO(Now, Now, Now.AddMinutes(5), [], null)
                : new NotificationDismissalsPageDTO(Now, Now, Now.AddMinutes(5), [], null),
            otherAuthenticated, otherHasSession));
        await using var service = new NotificationsBrowserService(js);
        var context = new QueuedContext();
        var original = SynchronizationContext.Current;
        Task<NotificationUiSnapshot> active;
        Task other;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            active = service.ActiveAsync(default);
            Assert.False(active.IsCompleted);
            SynchronizationContext.SetSynchronizationContext(null);
            js.CompleteDelayed();
            context.RunOne(); // Finish the active request; its paging continuation is still queued.
            Assert.False(active.IsCompleted);
            other = admin ? service.AdministrationAsync(default) : service.DismissalsAsync(default);
            Assert.True(other.IsCompleted);
            context.RunAll();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
        if (otherFails) await Assert.ThrowsAsync<NotificationUiException>(() => other);
        else await other;
        var snapshot = await active;
        Assert.Equal(authenticated, snapshot.Authenticated);
        Assert.Equal(authenticated && hasSession, snapshot.HasSession);
    }

    [Fact]
    public async Task MissingSessionHeaderDoesNotInventASession()
    {
        var js = new Browser { OmitSessionHeader = true };
        js.Replies.Enqueue((200, Page(null, Notice()), true, true));
        await using var service = new NotificationsBrowserService(js);
        var snapshot = await service.ActiveAsync(default);
        Assert.True(snapshot.Authenticated);
        Assert.False(snapshot.HasSession);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public async Task CapabilityChangeRestartsAndDiscardsPartialPages(bool firstAuth, bool firstSession, bool nextAuth, bool nextSession)
    {
        var stale = Notice(); var fresh = Notice();
        var js = new Browser();
        js.Replies.Enqueue((200, Page("next", stale), firstAuth, firstSession));
        js.Replies.Enqueue((200, Page(null, Notice()), nextAuth, nextSession));
        js.Replies.Enqueue((200, Page(null, fresh), nextAuth, nextSession));
        await using var service = new NotificationsBrowserService(js);
        var result = await service.ActiveAsync(default);
        Assert.Equal(fresh.Id, Assert.Single(result.Snapshot.Items).Id);
        Assert.Equal(nextAuth, result.Authenticated);
        Assert.Equal(nextAuth && nextSession, result.HasSession);
        Assert.DoesNotContain("continuationToken", js.Requests[2].Path);
    }

    [Fact]
    public async Task RepeatedCapabilityChangesFailAfterThreeSnapshots()
    {
        var js = new Browser();
        for (var i = 0; i < 3; i++)
        {
            js.Replies.Enqueue((200, Page("next", Notice()), true, true));
            js.Replies.Enqueue((200, Page(null, Notice()), true, false));
        }
        await using var service = new NotificationsBrowserService(js);
        var error = await Assert.ThrowsAsync<NotificationUiException>(() => service.ActiveAsync(default));
        Assert.Equal(409, error.Status);
        Assert.Equal(NotificationErrorCodes.SnapshotExpired, error.Code);
        Assert.Equal(6, js.Requests.Count);
    }

    [Fact]
    public async Task ExplicitRevisionConflictRestartsAndDiscardsPartialItems()
    {
        var stale = Notice(); var fresh = Notice();
        var js = new Browser();
        js.Replies.Enqueue((200, Page("old", stale), false, null));
        js.Replies.Enqueue((409, new { code = NotificationErrorCodes.SnapshotExpired }, false, null));
        js.Replies.Enqueue((200, Page(null, fresh), false, null));
        await using var service = new NotificationsBrowserService(js);
        var result = await service.ActiveAsync(default);
        Assert.Equal(fresh.Id, Assert.Single(result.Snapshot.Items).Id);
        Assert.DoesNotContain("continuationToken", js.Requests[2].Path);
    }

    [Fact]
    public async Task RevisionRetriesAreBoundedToThreeSnapshots()
    {
        var js = new Browser();
        for (var i = 0; i < 3; i++) js.Replies.Enqueue((409, new { code = NotificationErrorCodes.SnapshotExpired }, false, null));
        await using var service = new NotificationsBrowserService(js);
        await Assert.ThrowsAsync<NotificationUiException>(() => service.ActiveAsync(default));
        Assert.Equal(3, js.Requests.Count);
    }

    [Theory]
    [InlineData(409, "notifications.version_conflict")]
    [InlineData(400, "notifications.invalid_continuation_token")]
    [InlineData(503, "notifications.dependency_unavailable")]
    public async Task OtherErrorsNeverReturnPartialOrRestart(int status, string code)
    {
        var js = new Browser();
        js.Replies.Enqueue((200, Page("next", Notice()), true, null));
        js.Replies.Enqueue((status, new { code }, true, null));
        await using var service = new NotificationsBrowserService(js);
        var exception = await Assert.ThrowsAsync<NotificationUiException>(() => service.ActiveAsync(default));
        Assert.Equal(code, exception.Code);
        Assert.Equal(2, js.Requests.Count);
    }

    [Fact]
    public async Task DismissalSendsScopeOnlyAndRestoreHasQueryWithoutBody()
    {
        var id = Guid.NewGuid();
        var state = new NotificationDismissalDTO(id, Now, null, new(NotificationDismissalScope.Session, Now, null));
        var js = new Browser();
        js.Replies.Enqueue((200, state, true, null));
        js.Replies.Enqueue((200, new RestoreNotificationDismissalDTO(state, false), true, null));
        await using var service = new NotificationsBrowserService(js);
        await service.DismissAsync(id, NotificationDismissalScope.Session, default);
        Assert.Equal("{\"scope\":\"session\"}", js.Requests[0].Body);
        await service.RestoreAsync(id, NotificationRestoreScope.CurrentSession, default);
        Assert.Equal("DELETE", js.Requests[1].Method);
        Assert.EndsWith("?scope=currentSession", js.Requests[1].Path);
        Assert.Null(js.Requests[1].Body);
    }

    [Fact]
    public async Task AdministrationAndDismissalsAlsoDrainAllPages()
    {
        var n = Notice();
        var admin = new NotificationAdministrationDTO(n.Id, n.Version, n.Content, n.Severity,
            new(NotificationAudienceKind.Everyone), n.StartsAtUtc, n.EndsAtUtc, n.DismissalPolicy, Now, Now);
        var js = new Browser();
        js.Replies.Enqueue((200, new NotificationAdministrationPageDTO(Now, Now, Now.AddMinutes(5), [admin], "next"), true, null));
        js.Replies.Enqueue((200, new NotificationAdministrationPageDTO(Now, Now, Now.AddMinutes(5), [], null), true, null));
        js.Replies.Enqueue((200, new NotificationDismissalsPageDTO(Now, Now, Now.AddMinutes(5), [], "next"), true, null));
        js.Replies.Enqueue((200, new NotificationDismissalsPageDTO(Now, Now, Now.AddMinutes(5),
            [new(n, null, new(NotificationDismissalScope.Session, Now, null))], null), true, null));
        await using var service = new NotificationsBrowserService(js);
        Assert.Single(await service.AdministrationAsync(default));
        Assert.Single(await service.DismissalsAsync(default));
        Assert.Equal(4, js.Requests.Count);
    }

    internal sealed class Browser : IJSRuntime, IJSObjectReference
    {
        public Queue<(int Status, object Body, bool Authenticated, bool? HasSession)> Replies { get; } = [];
        public List<(string Path, string Method, string? Body)> Requests { get; } = [];
        public int DelayRequestNumber { get; set; }
        public bool OmitSessionHeader { get; set; }
        private Action? _complete;
        public void CompleteDelayed() => _complete!();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            ct.ThrowIfCancellationRequested();
            if (identifier == "import")
            {
                // Used by both the browser service and interactive host fixture.
                // The publish/HTTP regression validates this specifier's route.
                Assert.Equal(NotificationsBrowserService.ModulePath, Assert.Single(args!));
                return ValueTask.FromResult((TValue)(object)this);
            }
            if (identifier == "abort") return ValueTask.FromResult(default(TValue)!);
            if (identifier == "anonymousDismissed") return ValueTask.FromResult((TValue)(object)Array.Empty<string>());
            Assert.Equal("request", identifier);
            Requests.Add(((string)args![1]!, (string)args[2]!, (string?)args[3]));
            var reply = Replies.Dequeue();
            var response = new Dictionary<string, object>
            {
                ["status"] = reply.Status,
                ["body"] = JsonSerializer.Serialize(reply.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            // An error may come from an intermediary without either capability header.
            if (reply.Status is >= 200 and < 300)
            {
                response["authenticated"] = reply.Authenticated;
                if (!OmitSessionHeader) response["hasSession"] = reply.HasSession ?? reply.Authenticated;
            }
            var json = JsonSerializer.Serialize(response);
            var value = JsonSerializer.Deserialize<TValue>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (Requests.Count != DelayRequestNumber) return ValueTask.FromResult(value);
            var pending = new TaskCompletionSource<TValue>();
            _complete = () => pending.SetResult(value);
            return new ValueTask<TValue>(pending.Task);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];
        public override void Post(SendOrPostCallback callback, object? state) => _callbacks.Enqueue((callback, state));
        public void RunOne()
        {
            var (callback, state) = _callbacks.Dequeue();
            callback(state);
        }
        public void RunAll() { while (_callbacks.Count > 0) RunOne(); }
    }
}
