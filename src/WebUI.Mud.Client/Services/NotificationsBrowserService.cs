using System.Text.Json;
using Microsoft.JSInterop;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

// Same browser-origin BFF in BOTH interactive modes. No tokens or ticket properties
// leave the server; no HttpContext captured by a long-lived server circuit.
public sealed class NotificationsBrowserService(IJSRuntime js) : INotificationsService, IAsyncDisposable
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private Task<IJSObjectReference>? _module;
    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>(
        "import", "./_content/VeloTime.WebUI.Mud.Client/notifications.js").AsTask();

    private sealed record BrowserResponse(int Status, string Body, bool Authenticated);
    private bool _authenticated;
    private async Task<T> Request<T>(string path, string method, object? body, CancellationToken ct)
    {
        var module = await Module;
        var id = Guid.NewGuid().ToString("N");
        using var cancel = ct.Register(() => { _ = Abort(module, id); });
        var response = await module.InvokeAsync<BrowserResponse>("request", ct, id, path, method,
            body is null ? null : JsonSerializer.Serialize(body, _json));
        ct.ThrowIfCancellationRequested();
        _authenticated = response.Authenticated;
        if (response.Status is < 200 or >= 300)
        {
            string code = "notifications.request_failed";
            try
            {
                using var problem = JsonDocument.Parse(response.Body);
                if (problem.RootElement.TryGetProperty("code", out var value)) code = value.GetString() ?? code;
            }
            catch (JsonException) { }
            throw new NotificationUiException(response.Status, code);
        }
        return JsonSerializer.Deserialize<T>(response.Body, _json) ?? throw new NotificationUiException(503, "notifications.request_failed");
    }
    private static async Task Abort(IJSObjectReference module, string id)
    {
        try { await module.InvokeVoidAsync("abort", id); }
        catch (Exception e) when (e is JSException or JSDisconnectedException or ObjectDisposedException) { }
    }

    // Only an explicit revision/expiry conflict restarts paging. Never publish partial pages.
    private static async Task<(T Page, IReadOnlyList<TItem> Items)> Drain<T, TItem>(
        Func<string?, Task<T>> fetch, Func<T, IReadOnlyList<TItem>> items, Func<T, string?> token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var all = new List<TItem>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                string? next = null;
                T page;
                do
                {
                    page = await fetch(next);
                    all.AddRange(items(page));
                    next = token(page);
                    if (next is not null && !seen.Add(next))
                        throw new NotificationUiException(503, "notifications.request_failed");
                } while (next is not null);
                return (page, all);
            }
            catch (NotificationUiException e) when (e.Status == 409 &&
                e.Code == "notifications.snapshot_expired" && attempt < 2) { }
        }
    }
    private static string PageQuery(string? token) => "?pageSize=100" +
        (token is null ? "" : "&continuationToken=" + Uri.EscapeDataString(token));
    public async Task<NotificationUiSnapshot> ActiveAsync(CancellationToken ct)
    {
        var result = await Drain(t => Request<ActiveNotificationsDTO>("/api/notifications/active" + PageQuery(t), "GET", null, ct),
            p => p.Items, p => p.ContinuationToken);
        return new(result.Page with { Items = result.Items, ContinuationToken = null }, _authenticated);
    }
    public async Task<IReadOnlyList<NotificationDismissalListItemDTO>> DismissalsAsync(CancellationToken ct) =>
        (await Drain(t => Request<NotificationDismissalsPageDTO>("/api/notifications/dismissals" + PageQuery(t), "GET", null, ct),
            p => p.Items, p => p.ContinuationToken)).Items;
    public async Task<IReadOnlyList<NotificationAdministrationDTO>> AdministrationAsync(CancellationToken ct) =>
        (await Drain(t => Request<NotificationAdministrationPageDTO>("/api/admin/notifications" + PageQuery(t), "GET", null, ct),
            p => p.Items, p => p.ContinuationToken)).Items;
    public Task<NotificationDismissalDTO> DismissAsync(Guid id, NotificationDismissalScope scope, CancellationToken ct) =>
        Request<NotificationDismissalDTO>($"/api/notifications/{id}/dismissal", "PUT", new DismissNotificationRequest(scope), ct);
    public Task<RestoreNotificationDismissalDTO> RestoreAsync(Guid id, NotificationRestoreScope scope, CancellationToken ct) =>
        Request<RestoreNotificationDismissalDTO>($"/api/notifications/{id}/dismissal?scope=" + (scope switch
        {
            NotificationRestoreScope.Account => "account",
            NotificationRestoreScope.CurrentSession => "currentSession",
            NotificationRestoreScope.AccountAndCurrentSession => "accountAndCurrentSession",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        }), "DELETE", null, ct);
    public Task<NotificationAdministrationDTO> GetAsync(Guid id, CancellationToken ct) =>
        Request<NotificationAdministrationDTO>($"/api/admin/notifications/{id}", "GET", null, ct);
    public Task<NotificationAdministrationDTO> CreateAsync(CreateNotificationRequest request, CancellationToken ct) =>
        Request<NotificationAdministrationDTO>("/api/admin/notifications", "POST", request, ct);
    public Task<NotificationAdministrationDTO> UpdateAsync(Guid id, UpdateNotificationRequest request, CancellationToken ct) =>
        Request<NotificationAdministrationDTO>($"/api/admin/notifications/{id}", "PUT", request, ct);
    public Task<NotificationAdministrationDTO> EndAsync(Guid id, long version, CancellationToken ct) =>
        Request<NotificationAdministrationDTO>($"/api/admin/notifications/{id}/end", "POST", new EndNotificationRequest(version), ct);
    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await (await _module).DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
