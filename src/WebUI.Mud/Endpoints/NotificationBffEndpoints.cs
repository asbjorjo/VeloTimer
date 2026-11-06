using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using VeloTime.Module.Notifications.Client;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.WebUI.Mud.Notifications;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class NotificationBffEndpoints
{
    internal static void MapNotificationBffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Deliberately separate from the existing mandatory-login /api group.
        var reader = endpoints.MapGroup("/api/notifications");
        var cookie = new AuthorizeAttribute { AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme };
        reader.AddEndpointFilter(Guard);
        // UI-only cookie status probe; never forwards a token/assertion or an ID.
        reader.MapGet("/session", (HttpContext http) => Results.Ok(http.User.Identity?.IsAuthenticated == true));
        reader.MapGet("/active", (int? pageSize, string? continuationToken, INotificationsClient client, CancellationToken ct) =>
            client.GetActiveNotificationsAsync(pageSize, continuationToken, null, ct));
        reader.MapGet("/dismissals", (int? pageSize, string? continuationToken, INotificationsClient client, CancellationToken ct) =>
            client.GetNotificationDismissalsAsync(pageSize, continuationToken, null, ct)).RequireAuthorization(cookie);
        reader.MapPut("/{id:guid}/dismissal", (Guid id, DismissNotificationRequest body, INotificationsClient client, CancellationToken ct) =>
            client.DismissNotificationAsync(id, null, body, ct)).RequireAuthorization(cookie);
        reader.MapDelete("/{id:guid}/dismissal", (Guid id, string scope, INotificationsClient client, CancellationToken ct) =>
            client.RestoreNotificationDismissalAsync(id, scope, null, ct)).RequireAuthorization(cookie);

        var admin = endpoints.MapGroup("/api/admin/notifications").RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
            Policy = "Notifications.Admin"
        });
        admin.AddEndpointFilter(Guard);
        admin.MapGet("", (int? pageSize, string? continuationToken, INotificationsClient client, CancellationToken ct) =>
            client.ListNotificationsAsync(pageSize, continuationToken, null, ct));
        admin.MapGet("/{id:guid}", (Guid id, INotificationsClient client, CancellationToken ct) =>
            client.GetNotificationAsync(id, null, ct));
        admin.MapPost("", async (CreateNotificationRequest body, INotificationsClient client, CancellationToken ct) =>
        {
            var result = await client.CreateNotificationAsync(null, body, ct);
            return Results.Created($"/api/admin/notifications/{result.Id}", result);
        });
        admin.MapPut("/{id:guid}", (Guid id, UpdateNotificationRequest body, INotificationsClient client, CancellationToken ct) =>
            client.UpdateNotificationAsync(id, null, body, ct));
        admin.MapPost("/{id:guid}/end", (Guid id, EndNotificationRequest body, INotificationsClient client, CancellationToken ct) =>
            client.EndNotificationAsync(id, null, body, ct));
    }

    private static async ValueTask<object?> Guard(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var ticket = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.Response.Headers["X-VeloTime-Authenticated"] = ticket.Succeeded ? "true" : "false";
        var cookieName = http.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.Name!;
        // Expired/rejected/malformed credentials MUST NOT downgrade to the public view.
        if ((!ticket.Succeeded && http.Request.Cookies.ContainsKey(cookieName)) || http.Request.Headers.ContainsKey("Authorization"))
            return Problem(401, NotificationErrorCodes.AuthenticationRequired);
        try
        {
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                if (!ticket.Succeeded) return Problem(401, NotificationErrorCodes.AuthenticationRequired);
                await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            }
            return await next(context);
        }
        catch (AntiforgeryValidationException) { return Problem(400, NotificationErrorCodes.RequestFailed); }
        catch (NotificationAuthenticationException e) { return Problem(e.Status, e.Code); }
        catch (ApiException<ProblemDetails> e) { return Problem(e.StatusCode, e.Result.Code); }
        catch (ApiException e)
        {
            return Problem(e.StatusCode is >= 400 and <= 599 ? e.StatusCode : 503,
                e.StatusCode switch
                {
                    401 => NotificationErrorCodes.AuthenticationRequired,
                    403 => NotificationErrorCodes.Forbidden,
                    _ => NotificationErrorCodes.RequestFailed
                });
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException)
        { return Problem(503, NotificationErrorCodes.DependencyUnavailable); }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        { return Problem(503, NotificationErrorCodes.DependencyUnavailable); }
    }
    private static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
