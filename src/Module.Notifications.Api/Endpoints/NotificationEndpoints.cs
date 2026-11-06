using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var reader = app.MapGroup("").WithTags("Notifications");
        reader.MapGet(NotificationRoutes.Active, Active).AllowAnonymous().WithName("GetActiveNotifications");
        reader.MapGet(NotificationRoutes.Dismissals, Dismissals).RequireAuthorization().WithName("GetNotificationDismissals");
        reader.MapPut(NotificationRoutes.Dismissal, Dismiss).RequireAuthorization().WithName("DismissNotification");
        reader.MapDelete(NotificationRoutes.Dismissal, Restore).RequireAuthorization().WithName("RestoreNotificationDismissal");
        var admin = app.MapGroup("").WithTags("NotificationsAdministration")
            .RequireAuthorization(NotificationAuthorization.AdministrationPolicy);
        // Even admin reads require a validated application-login assertion.
        admin.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.RequestServices.GetRequiredService<INotificationReaderIdentityAccessor>().GetCurrent();
            return await next(context);
        });
        admin.MapGet(NotificationRoutes.Administration, List).WithName("ListNotifications");
        admin.MapGet(NotificationRoutes.AdministrationItem, Get).WithName("GetNotification");
        admin.MapPost(NotificationRoutes.Administration, Create).WithName("CreateNotification");
        admin.MapPut(NotificationRoutes.AdministrationItem, Update).WithName("UpdateNotification");
        admin.MapPost(NotificationRoutes.End, End).WithName("EndNotification");
        foreach (var group in new[] { reader, admin })
            group.ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
                .ProducesProblem(409).ProducesProblem(503);
    }

    private static NotificationPageRequest Page(int? pageSize, string? continuationToken) =>
        new(pageSize ?? NotificationLimits.DefaultPageSize, continuationToken);
    private static NotificationReaderIdentity Required(INotificationReaderIdentityAccessor identity) =>
        identity.GetCurrent() ?? throw new NotificationException(401, "notifications.authentication_required");

    private static async Task<Ok<ActiveNotificationsDTO>> Active(NotificationSnapshotService service,
        INotificationReaderIdentityAccessor identity, [FromQuery] int? pageSize, [FromQuery] string? continuationToken,
        CancellationToken ct) => TypedResults.Ok(await service.ActiveAsync(identity.GetCurrent(), Page(pageSize, continuationToken), ct));

    private static async Task<Ok<NotificationDismissalsPageDTO>> Dismissals(NotificationSnapshotService service,
        INotificationReaderIdentityAccessor identity, [FromQuery] int? pageSize, [FromQuery] string? continuationToken,
        CancellationToken ct) => TypedResults.Ok(await service.DismissalsAsync(Required(identity), Page(pageSize, continuationToken), ct));

    private static async Task<Ok<NotificationDismissalDTO>> Dismiss(Guid id, DismissNotificationRequest request,
        NotificationService service, INotificationReaderIdentityAccessor identity, CancellationToken ct) =>
        TypedResults.Ok(await service.DismissAsync(id, Required(identity), request, ct));

    private static async Task<Ok<RestoreNotificationDismissalDTO>> Restore(Guid id, [FromQuery] string? scope,
        NotificationService service, INotificationReaderIdentityAccessor identity, CancellationToken ct)
    {
        var parsed = scope switch
        {
            "account" => NotificationRestoreScope.Account,
            "currentSession" => NotificationRestoreScope.CurrentSession,
            "accountAndCurrentSession" => NotificationRestoreScope.AccountAndCurrentSession,
            _ => throw new NotificationException(400, NotificationErrorCodes.InvalidRestoreScope)
        };
        return TypedResults.Ok(await service.RestoreAsync(id, Required(identity), parsed, ct));
    }

    private static async Task<Ok<NotificationAdministrationPageDTO>> List(NotificationSnapshotService service,
        INotificationReaderIdentityAccessor identity, [FromQuery] int? pageSize, [FromQuery] string? continuationToken,
        CancellationToken ct) => TypedResults.Ok(await service.AdministrationAsync(Required(identity), Page(pageSize, continuationToken), ct));

    private static async Task<Ok<NotificationAdministrationDTO>> Get(Guid id, NotificationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(id, ct));

    private static async Task<Created<NotificationAdministrationDTO>> Create(CreateNotificationRequest request,
        NotificationService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/admin/notifications/{result.Id}", result);
    }

    private static async Task<Ok<NotificationAdministrationDTO>> Update(Guid id, UpdateNotificationRequest request,
        NotificationService service, CancellationToken ct) => TypedResults.Ok(await service.UpdateAsync(id, request, ct));

    private static async Task<Ok<NotificationAdministrationDTO>> End(Guid id, EndNotificationRequest request,
        NotificationService service, CancellationToken ct) => TypedResults.Ok(await service.EndAsync(id, request, ct));
}
