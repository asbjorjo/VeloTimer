using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class OwnershipEndpoints
{
    internal static void MapOwnershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var personal = endpoints.MapGroup("/my").RequireAuthorization();
        personal.MapGet("/transponders", ListPersonal).WithName("ListMyTransponders").Produces<TransponderPage>();
        personal.MapPost("/transponders/query", QueryPersonal).WithName("QueryMyTransponders").Produces<TransponderPage>();
        personal.MapGet("/transponders/lookup", LookupPersonal).WithName("LookupMyTransponders").Produces<TransponderLookupPage>();
        personal.MapPost("/transponders", Claim).WithName("ClaimMyTransponder").Produces<TransponderDTO>();
        personal.MapPost("/transponders/register", RegisterPersonal).WithName("RegisterMyTransponder").Produces<RegisterTransponderResult>();
        personal.MapPost("/ownerships/{id:guid}/end", EndPersonal).WithName("EndMyTransponderOwnership").Produces(StatusCodes.Status204NoContent);
        personal.MapDelete("/ownerships/{id:guid}", DeletePersonal).WithName("DeleteMyTransponderOwnership").Produces(StatusCodes.Status204NoContent);
        var admin = endpoints.MapGroup("/admin/ownerships").RequireAuthorization(AdministrationAuthorization.TimingPolicy);
        admin.MapPost("/register", RegisterAdministration).WithName("RegisterTransponderOwnership").Produces<RegisterTransponderResult>();
        admin.MapGet("", ListAll).WithName("ListTransponderOwnerships").Produces<List<TransponderOwnershipDTO>>();
        admin.MapPost("", async (SaveTransponderOwnershipRequest request, TransponderOwnershipService service, CancellationToken cancellationToken) =>
            await Execute(async () => { await service.SaveAsync(null, request, cancellationToken); return Results.NoContent(); })).WithName("CreateTransponderOwnership").Produces(StatusCodes.Status204NoContent);
        admin.MapPut("/{id:guid}", async (Guid id, SaveTransponderOwnershipRequest request, TransponderOwnershipService service, CancellationToken cancellationToken) =>
            await Execute(async () => { await service.SaveAsync(id, request, cancellationToken); return Results.NoContent(); })).WithName("UpdateTransponderOwnership").Produces(StatusCodes.Status204NoContent);
        admin.MapPost("/{id:guid}/end", async (Guid id, TransponderOwnershipService service, CancellationToken cancellationToken) =>
            await Execute(async () => { await service.RemoveOrEndAsync(id, null, true, cancellationToken); return Results.NoContent(); })).WithName("EndTransponderOwnership").Produces(StatusCodes.Status204NoContent);
        admin.MapDelete("/{id:guid}", async (Guid id, TransponderOwnershipService service, CancellationToken cancellationToken) =>
            await Execute(async () => { await service.RemoveOrEndAsync(id, null, false, cancellationToken); return Results.NoContent(); })).WithName("DeleteTransponderOwnership").Produces(StatusCodes.Status204NoContent);
    }

    private static Guid? Subject(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;

    private static Task<IResult> ListPersonal(ClaimsPrincipal user, TimingDbContext storage, CancellationToken cancellationToken,
        int page = 0, int pageSize = 10, string search = "", bool includeHistory = true) =>
        QueryPersonal(new(page, pageSize, search, includeHistory), user, storage, cancellationToken);

    private static async Task<IResult> QueryPersonal(TransponderPageRequest request, ClaimsPrincipal user, TimingDbContext storage, CancellationToken cancellationToken)
    {
        if (Subject(user) is not { } ownerId) return Results.Forbid();
        return await Execute(async () => Results.Ok(await TransponderQuery.ListAsync(storage, request, ownerId, cancellationToken)));
    }

    private static async Task<IResult> LookupPersonal(ClaimsPrincipal user, TimingDbContext storage, CancellationToken cancellationToken,
        int page = 0, int pageSize = 10, string search = "", bool includeOwners = false)
    {
        if (Subject(user) is not { } ownerId) return Results.Forbid();
        return await Execute(async () => Results.Ok(await TransponderQuery.LookupAsync(storage, page, pageSize, search, includeOwners, ownerId, cancellationToken)));
    }

    private static async Task<IResult> Claim(ClaimTransponderRequest request, ClaimsPrincipal user, TransponderOwnershipService service, CancellationToken cancellationToken)
    {
        if (Subject(user) is not { } ownerId) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Code)) return Results.Problem(detail: "Transponder code is required.", statusCode: 400);
        return await Execute(async () => Results.Ok(await service.ClaimAsync(request.Code, ownerId, cancellationToken)));
    }

    private static async Task<IResult> RegisterPersonal(RegisterTransponderRequest request, ClaimsPrincipal user,
        TransponderOwnershipService service, CancellationToken cancellationToken)
    {
        if (Subject(user) is not { } ownerId) return Results.Forbid();
        if (request.OwnerId.HasValue) return Results.Problem(detail: "Self-service registration cannot specify an owner.", statusCode: 400);
        return await Execute(async () => Results.Ok(await service.RegisterAsync(request, ownerId, cancellationToken)));
    }

    private static async Task<IResult> RegisterAdministration(RegisterTransponderRequest request,
        TransponderOwnershipService service, CancellationToken cancellationToken)
    {
        if (request.OwnerId is not { } ownerId || ownerId == Guid.Empty)
            return Results.Problem(detail: "A valid owner is required.", statusCode: 400);
        return await Execute(async () => Results.Ok(await service.RegisterAsync(request, ownerId, cancellationToken)));
    }

    private static async Task<IResult> EndPersonal(Guid id, ClaimsPrincipal user, TransponderOwnershipService service, CancellationToken cancellationToken) =>
        await MutatePersonal(id, user, service, true, cancellationToken);

    private static async Task<IResult> DeletePersonal(Guid id, ClaimsPrincipal user, TransponderOwnershipService service, CancellationToken cancellationToken) =>
        await MutatePersonal(id, user, service, false, cancellationToken);

    private static async Task<IResult> MutatePersonal(Guid id, ClaimsPrincipal user, TransponderOwnershipService service, bool end, CancellationToken cancellationToken)
    {
        if (Subject(user) is not { } ownerId) return Results.Forbid();
        return await Execute(async () => { await service.RemoveOrEndAsync(id, ownerId, end, cancellationToken); return Results.NoContent(); });
    }

    private static async Task<IResult> ListAll(TimingDbContext storage, Guid? transponderId, Guid? ownerId, CancellationToken cancellationToken)
    {
        var query = storage.Set<TransponderOwner>().AsNoTracking();
        if (transponderId.HasValue) query = query.Where(owner => owner.TransponderId == transponderId);
        if (ownerId.HasValue) query = query.Where(owner => owner.OwnerId == ownerId);
        return Results.Ok((await query.OrderByDescending(owner => owner.OwnedFrom).ToListAsync(cancellationToken)).Select(TransponderOwnershipService.ToDto).ToList());
    }

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (OwnershipChangeEnqueueException exception) { return Results.Problem(detail: exception.Message, statusCode: 500); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.Problem(detail: exception.Message, statusCode: 400); }
        catch (InvalidOperationException) { return Results.Problem(detail: "The ownership period conflicts with an existing ownership.", statusCode: 409); }
        catch (DbUpdateException) { return Results.Problem(detail: "The transponder changed while saving. Refresh and try again.", statusCode: 409); }
    }
}