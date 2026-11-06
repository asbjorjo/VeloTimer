using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class TransponderStatusEndpoints
{
    internal static void MapTransponderStatusEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/transponder-status/{id:guid}", GetStatus).RequireAuthorization()
            .WithName("GetTransponderStatus");

    private static async Task<Results<Ok<TransponderStatus>, NotFound, ForbidHttpResult, ProblemHttpResult>> GetStatus(
        Guid id, ClaimsPrincipal user, IAuthorizationService authorization, TimingDbContext storage,
        TimeProvider clock, HttpContext http, CancellationToken cancellationToken,
        int ownershipPage = 0, int warningPage = 0, int pageSize = 10)
    {
        http.Response.Headers.CacheControl = "no-store";
        var administration = (await authorization.AuthorizeAsync(user, null, AdministrationAuthorization.TransponderStatusAdminPolicy)).Succeeded;
        Guid? ownerId = null;
        if (!administration)
        {
            if (!Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var subject) || subject == Guid.Empty)
                return TypedResults.Forbid();
            ownerId = subject;
        }
        try
        {
            var result = await TransponderStatusQuery.GetAsync(storage, id, ownerId, clock.GetUtcNow(),
                ownershipPage, warningPage, pageSize, cancellationToken);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.Problem(detail: exception.Message, statusCode: 400);
        }
    }
}
