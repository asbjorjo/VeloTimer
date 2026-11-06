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
        js.Replies.Enqueue((200, Page("opaque", first), true));
        js.Replies.Enqueue((200, Page(null, second), true));
        await using var service = new NotificationsBrowserService(js);
        var result = await service.ActiveAsync(default);
        Assert.Equal(new[] { first.Id, second.Id }, result.Snapshot.Items.Select(n => n.Id));
        Assert.Null(result.Snapshot.ContinuationToken);
        Assert.True(result.Authenticated);
        Assert.Contains("continuationToken=opaque", js.Requests[1].Path);
    }

    [Fact]
    public async Task ExplicitRevisionConflictRestartsAndDiscardsPartialItems()
    {
        var stale = Notice(); var fresh = Notice();
        var js = new Browser();
        js.Replies.Enqueue((200, Page("old", stale), false));
        js.Replies.Enqueue((409, new { code = NotificationErrorCodes.SnapshotExpired }, false));
        js.Replies.Enqueue((200, Page(null, fresh), false));
        await using var service = new NotificationsBrowserService(js);
        var result = await service.ActiveAsync(default);
        Assert.Equal(fresh.Id, Assert.Single(result.Snapshot.Items).Id);
        Assert.DoesNotContain("continuationToken", js.Requests[2].Path);
    }

    [Fact]
    public async Task RevisionRetriesAreBoundedToThreeSnapshots()
    {
        var js = new Browser();
        for (var i = 0; i < 3; i++) js.Replies.Enqueue((409, new { code = NotificationErrorCodes.SnapshotExpired }, false));
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
        js.Replies.Enqueue((200, Page("next", Notice()), true));
        js.Replies.Enqueue((status, new { code }, true));
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
        js.Replies.Enqueue((200, state, true));
        js.Replies.Enqueue((200, new RestoreNotificationDismissalDTO(state, false), true));
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
        js.Replies.Enqueue((200, new NotificationAdministrationPageDTO(Now, Now, Now.AddMinutes(5), [admin], "next"), true));
        js.Replies.Enqueue((200, new NotificationAdministrationPageDTO(Now, Now, Now.AddMinutes(5), [], null), true));
        js.Replies.Enqueue((200, new NotificationDismissalsPageDTO(Now, Now, Now.AddMinutes(5), [], "next"), true));
        js.Replies.Enqueue((200, new NotificationDismissalsPageDTO(Now, Now, Now.AddMinutes(5),
            [new(n, null, new(NotificationDismissalScope.Session, Now, null))], null), true));
        await using var service = new NotificationsBrowserService(js);
        Assert.Single(await service.AdministrationAsync(default));
        Assert.Single(await service.DismissalsAsync(default));
        Assert.Equal(4, js.Requests.Count);
    }

    private sealed class Browser : IJSRuntime, IJSObjectReference
    {
        public Queue<(int Status, object Body, bool Authenticated)> Replies { get; } = [];
        public List<(string Path, string Method, string? Body)> Requests { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            ct.ThrowIfCancellationRequested();
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "abort") return ValueTask.FromResult(default(TValue)!);
            Assert.Equal("request", identifier);
            Requests.Add(((string)args![1]!, (string)args[2]!, (string?)args[3]));
            var reply = Replies.Dequeue();
            var json = JsonSerializer.Serialize(new { status = reply.Status,
                body = JsonSerializer.Serialize(reply.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                authenticated = reply.Authenticated });
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
