using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class TransponderManagementEndpoints
{
    internal static void MapTransponderManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Map(endpoints, false);
        Map(endpoints.MapGroup("/admin/timing").RequireAuthorization(AdministrationAuthorization.TimingPolicy), true);
    }

    private static void Map(IEndpointRouteBuilder endpoints, bool administration)
    {
        endpoints.MapGet("/transponders", async (ITransponderManagementService service, CancellationToken cancellationToken,
            int page = 0, int pageSize = 10, string search = "", bool includeHistory = true,
            string? labelFilter = null, string? systemFilter = null, DateOnly? lastSeenFilter = null,
            string? ownerFilter = null, DateOnly? ownedFromFilter = null, DateOnly? ownedToFilter = null,
            TransponderOwnershipStatus? statusFilter = null, string? sortBy = null, bool sortDescending = false,
            DateTimeOffset? lastSeenUtcStart = null, DateTimeOffset? lastSeenUtcEnd = null,
            DateTimeOffset? ownedFromUtcStart = null, DateTimeOffset? ownedFromUtcEnd = null,
            DateTimeOffset? ownedToUtcStart = null, DateTimeOffset? ownedToUtcEnd = null) =>
        {
            if (!TryCreateRange(lastSeenUtcStart, lastSeenUtcEnd, lastSeenFilter, "lastSeen", out var lastSeenRange, out var error) ||
                !TryCreateRange(ownedFromUtcStart, ownedFromUtcEnd, ownedFromFilter, "ownedFrom", out var ownedFromRange, out error) ||
                !TryCreateRange(ownedToUtcStart, ownedToUtcEnd, ownedToFilter, "ownedTo", out var ownedToRange, out error))
                return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);

            var filters = new TransponderColumnFilters(labelFilter, systemFilter, lastSeenFilter, ownerFilter,
                ownedFromFilter, ownedToFilter, statusFilter)
            {
                LastSeenUtcRange = lastSeenRange,
                OwnedFromUtcRange = ownedFromRange,
                OwnedToUtcRange = ownedToRange
            };
            return Results.Ok(await service.GetTranspondersAsync(administration, page, pageSize, search, includeHistory,
                cancellationToken, filters, sortBy, sortDescending));
        });
        endpoints.MapPost("/transponders", async (ClaimTransponderRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.RegisterAsync(request.Code, administration, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapPost("/transponders/register", async (RegisterTransponderRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.RegisterOwnershipAsync(request, administration, cancellationToken)));
        endpoints.MapPost("/ownerships/{id:guid}/end", async (Guid id, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.EndOwnershipAsync(id, administration, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapDelete("/ownerships/{id:guid}", async (Guid id, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteOwnershipAsync(id, administration, cancellationToken);
            return Results.NoContent();
        });
        if (!administration) return;
        endpoints.MapGet("/transponders/lookup", async (ITransponderManagementService service, CancellationToken cancellationToken,
            int page = 0, string search = "") =>
            Results.Ok(await service.LookupTranspondersAsync(page, search, cancellationToken)));
        endpoints.MapPut("/transponders/{id:guid}", async (Guid id, SaveTransponderRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.SaveTransponderAsync(id, request.Label, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapDelete("/transponders/{id:guid}", async (Guid id, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteTransponderAsync(id, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapPost("/ownerships", async (SaveTransponderOwnershipRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.SaveOwnershipAsync(null, request, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapPut("/ownerships/{id:guid}", async (Guid id, SaveTransponderOwnershipRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.SaveOwnershipAsync(id, request, cancellationToken);
            return Results.NoContent();
        });
    }

    private static bool TryCreateRange(DateTimeOffset? start, DateTimeOffset? end, DateOnly? legacyDate,
        string field, out UtcInstantRange? range, out string? error)
    {
        range = null;
        error = null;
        if (start is null && end is null) return true;
        if (start is null || end is null)
        {
            error = $"Both {field}UtcStart and {field}UtcEnd must be provided.";
            return false;
        }
        if (start.Value.Offset != TimeSpan.Zero || end.Value.Offset != TimeSpan.Zero)
        {
            error = $"The {field} UTC range must use UTC (zero offset).";
            return false;
        }
        if (start.Value >= end.Value)
        {
            error = $"The {field} UTC range start must precede its end.";
            return false;
        }
        if (legacyDate.HasValue)
        {
            error = $"The {field} UTC range cannot be combined with its legacy date filter.";
            return false;
        }
        range = new UtcInstantRange(start.Value, end.Value);
        return true;
    }

}
