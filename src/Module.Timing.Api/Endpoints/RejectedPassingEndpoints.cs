using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class RejectedPassingEndpoints
{
    internal static void MapRejectedPassingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var rejected = endpoints.MapGroup("/rejected-passings").RequireAuthorization(AdministrationAuthorization.TimingPolicy)
            .WithTags(["RejectedPassings"]);
        rejected.MapGet("", List).WithName("GetRejectedPassings")
            .WithSummary("List agent passings Timing could not resolve, most recently seen first");
        rejected.MapPost("reprocess", Reprocess).WithName("ReprocessRejectedPassings")
            .WithSummary("Re-submit matching rejected passings; rows that now record, duplicate or conflict are deleted");
        rejected.MapDelete("{id:guid}", Dismiss).WithName("DismissRejectedPassing");
    }

    private static async Task<Results<Ok<RejectedPassingPage>, ProblemHttpResult>> List([FromServices] RejectedPassingService service,
        CancellationToken cancellationToken, string? agentId = null, string? reason = null, int page = 0, int pageSize = 20)
    {
        if (!TryParseReason(reason, out var parsed)) return InvalidReason();
        try { return TypedResults.Ok(await service.ListAsync(agentId, parsed, page, pageSize, cancellationToken)); }
        catch (ArgumentException exception) { return TypedResults.Problem(detail: exception.Message, statusCode: 400); }
    }

    private static async Task<Results<Ok<ReprocessRejectedPassingsResult>, ProblemHttpResult>> Reprocess(
        ReprocessRejectedPassingsRequest request, [FromServices] RejectedPassingService service, CancellationToken cancellationToken)
    {
        if (!TryParseReason(request.Reason, out var parsed)) return InvalidReason();
        return TypedResults.Ok(await service.ReprocessAsync(request.AgentId, parsed, cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound>> Dismiss(Guid id, [FromServices] RejectedPassingService service,
        CancellationToken cancellationToken) =>
        await service.DismissAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static bool TryParseReason(string? value, out PassingRejectionReason? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Enum.TryParse<PassingRejectionReason>(value.Trim(), true, out var parsed) || !Enum.IsDefined(parsed)
            || int.TryParse(value, out _)) return false;
        reason = parsed;
        return true;
    }

    private static ProblemHttpResult InvalidReason() => TypedResults.Problem(
        detail: $"Reason must be one of {string.Join(", ", Enum.GetNames<PassingRejectionReason>())}.", statusCode: 400);
}
