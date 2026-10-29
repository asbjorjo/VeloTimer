using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Common;
using Microsoft.Extensions.Caching.Hybrid;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class TransponderEndpoints
{
    internal static void MapTransponderEdnpoints(this IEndpointRouteBuilder builder)
    {
        var installations = builder.MapGroup("/transponders").RequireAuthorization(AdministrationAuthorization.TimingPolicy)
            .AddEndpointFilter<AdministrationDependencyFilter>();

        installations.MapGet("", ListTransponders);
        installations.MapPost("/query", QueryTransponders).WithName("QueryTransponders");
        installations.MapGet("/lookup", LookupTransponders).WithName("LookupTransponders");
        installations.MapGet("{id}", GetTransponderById);
        installations.MapPost("", RegisterTransponder);
        installations.MapPost("{id}/owners", AddTransponderOwner);
        installations.MapPut("{id:guid}", UpdateTransponder).WithName("UpdateTransponder");
        installations.MapDelete("{id:guid}", DeleteTransponder).WithName("DeleteTransponder");
    }

    private static async Task<Results<Ok<Guid>, UnprocessableEntity<List<string>>>> RegisterTransponder(
        InstallationService installationService,
        [FromQuery] string? SystemId,
        [FromQuery] string? SystemLabel,
        [FromQuery] string Type)
    {
        List<string> errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Type))
        {
            errors.Add("Type is required.");
        }
        if (string.IsNullOrWhiteSpace(SystemId) && string.IsNullOrWhiteSpace(SystemLabel))
        {
            errors.Add("Either SystemId or SystemLabel is required.");
        }
        if (!string.IsNullOrWhiteSpace(SystemId) && !string.IsNullOrWhiteSpace(SystemLabel))
        {
            errors.Add("Only one of SystemId or SystemLabel should be provided.");
        }
        if (errors.Count > 0)
        {
            return TypedResults.UnprocessableEntity(errors);
        }

        if (Type == "X2")
        {
            long id;
            try
            {
                id = string.IsNullOrWhiteSpace(SystemLabel) ? long.Parse(SystemId!) : MylapsX2Transponder.CodeToId(SystemLabel.Trim().ToUpperInvariant());
                _ = MylapsX2Transponder.IdToCode(id);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
            {
                return TypedResults.UnprocessableEntity(new List<string> { "Invalid transponder code or system ID." });
            }
            var result = await installationService.RegisterTransponder(new MylapsX2Transponder
            {
                SystemId = id.ToString(),
            });

            return TypedResults.Ok(result.Id);
        } else
        {
            errors.Add($"Unsupported transponder type: {Type}");
            return TypedResults.UnprocessableEntity(errors);
        }
    }

    static Task<Results<Ok<TransponderPage>, ProblemHttpResult>> ListTransponders(TimingDbContext storage, CancellationToken cancellationToken,
        int page = 0, int pageSize = 10, string search = "", bool includeHistory = true) =>
        QueryTransponders(new(page, pageSize, search, includeHistory), storage, cancellationToken);

    static async Task<Results<Ok<TransponderPage>, ProblemHttpResult>> QueryTransponders(
        TransponderPageRequest request, TimingDbContext storage, CancellationToken cancellationToken)
    {
        try { return TypedResults.Ok(await TransponderQuery.ListAsync(storage, request, null, cancellationToken)); }
        catch (ArgumentException exception) { return TypedResults.Problem(detail: exception.Message, statusCode: 400); }
    }

    static async Task<Results<Ok<TransponderLookupPage>, ProblemHttpResult>> LookupTransponders(
        TimingDbContext storage, CancellationToken cancellationToken, int page = 0, int pageSize = 10, string search = "", bool includeOwners = false)
    {
        try { return TypedResults.Ok(await TransponderQuery.LookupAsync(storage, page, pageSize, search, includeOwners, null, cancellationToken)); }
        catch (ArgumentException exception) { return TypedResults.Problem(detail: exception.Message, statusCode: 400); }
    }

    static async Task<Results<Ok<TransponderDTO>, NotFound>> GetTransponderById(Guid id, TimingDbContext storage)
    {
        var transponder = await storage.Set<Transponder>().AsNoTracking()
            .Include(transponder => transponder.Owners)
            .FirstOrDefaultAsync(transponder => transponder.Id == id);
        if (transponder == null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(TransponderOwnershipService.ToDto(transponder));
    }

    static async Task<Results<Ok, Conflict, NotFound, BadRequest, ProblemHttpResult>> AddTransponderOwner(TransponderOwnershipService service, Guid id, TransponderOwnership ownership)
    {
        try
        {
            await service.SaveAsync(null, new(id, ownership.OwnerId,
                new DateTimeOffset(ownership.OwnedFrom.ToUniversalTime()), ownership.OwnedTo.HasValue ? new DateTimeOffset(ownership.OwnedTo.Value.ToUniversalTime()) : null), CancellationToken.None);
            return TypedResults.Ok();
        }
        catch (OwnershipChangeEnqueueException exception) { return TypedResults.Problem(detail: exception.Message, statusCode: 500); }
        catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        catch (ArgumentException) { return TypedResults.BadRequest(); }
        catch (InvalidOperationException)
        {
            return TypedResults.Conflict();
        }
    }

    private static async Task<Results<Ok<TransponderDTO>, NotFound>> UpdateTransponder(Guid id, SaveTransponderRequest request,
        TimingDbContext storage, HybridCache cache, CancellationToken cancellationToken)
    {
        var transponder = await storage.Set<Transponder>().Include(transponder => transponder.Owners).SingleOrDefaultAsync(transponder => transponder.Id == id, cancellationToken);
        if (transponder is null) return TypedResults.NotFound();
        transponder.Label = request.Label?.Trim() ?? "";
        await storage.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"transponder_{transponder.System}_{transponder.SystemId}", cancellationToken);
        return TypedResults.Ok(TransponderOwnershipService.ToDto(transponder));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteTransponder(Guid id,
        TimingDbContext storage, IAdministrationReferences references, HybridCache cache, CancellationToken cancellationToken)
    {
        var transponder = await storage.Set<Transponder>().FindAsync([id], cancellationToken);
        if (transponder is null) return TypedResults.NotFound();
        var owners = await storage.Set<TransponderOwner>().CountAsync(owner => owner.TransponderId == id, cancellationToken);
        var passings = await storage.Set<Passing>().CountAsync(passing => passing.TransponderId == id, cancellationToken);
        if (owners > 0 || passings > 0) return TypedResults.Problem(detail: $"This transponder has {owners} ownership period(s) and {passings} recorded passing(s).", statusCode: 409);
        var dependencies = await references.GetDependenciesAsync("statistics", "transponders", id, cancellationToken);
        if (dependencies.Count > 0) return TypedResults.Problem(detail: dependencies.Description, statusCode: 409);
        storage.Remove(transponder);
        await storage.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"transponder_{transponder.System}_{transponder.SystemId}", cancellationToken);
        return TypedResults.NoContent();
    }
}
