using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

/// <summary>
/// Admin "recompute range" action and progress view (plan Phase 5). A job only queues work: the Statistics
/// processor's recompute worker picks it up on its next poll, after live passings, and splits it per transponder.
/// </summary>
internal static class AdminRecomputeEndpoints
{
    internal const int JobListLimit = 20;

    internal static void MapAdminRecomputeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var recompute = endpoints.MapGroup("/admin/recompute")
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy)
            .WithTags(["StatisticsRecompute"]);

        recompute.MapGet("", GetOverview)
            .WithName("GetStatisticsRecomputeOverview")
            .WithSummary("Get the recompute backlog and the most recent recompute jobs")
            .WithDescription("Jobs are newest first, at most 20. Times are UTC. Responses are no-store.")
            .Produces<RecomputeOverviewDTO>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        recompute.MapGet("jobs/{id:guid}", GetJob)
            .WithName("GetStatisticsRecomputeJob")
            .WithSummary("Get one recompute job and its progress")
            .Produces<RecomputeJobDTO>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
        recompute.MapPost("jobs", CreateJob)
            .WithName("CreateStatisticsRecomputeJob")
            .WithSummary("Recompute Statistics over a time range")
            .WithDescription("Null bounds are unbounded, so leaving both out recomputes all history. TransponderId narrows the job to one transponder; InstallationId to transponders with passings recorded at that installation. Bounds are UTC and are widened to whole riding sessions.")
            .Produces<RecomputeJobDTO>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<Ok<RecomputeOverviewDTO>> GetOverview(HttpContext http, StatisticsDbContext storage,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var backlog = await StatisticsOperations.ReadBacklogAsync(storage, cancellationToken);
        var jobs = await StatisticsOperations.ReadJobsAsync(storage, JobListLimit, cancellationToken);
        return TypedResults.Ok(new RecomputeOverviewDTO(backlog.ToDto(), jobs));
    }

    private static async Task<Results<Ok<RecomputeJobDTO>, NotFound>> GetJob(Guid id, HttpContext http,
        StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var jobs = await StatisticsOperations.ReadJobsAsync(storage, 1, cancellationToken, id);
        return jobs.Count == 0 ? TypedResults.NotFound() : TypedResults.Ok(jobs[0]);
    }

    private static async Task<Results<Accepted<RecomputeJobDTO>, ValidationProblem>> CreateJob(
        CreateRecomputeJobRequest? request, ClaimsPrincipal user, StatisticsDbContext storage,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count != 0) return TypedResults.ValidationProblem(errors);
        var requestedBy = Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
            && actor != Guid.Empty ? actor : (Guid?)null;
        var now = DateTime.UtcNow;
        var job = StatisticsOperations.CreateJob(storage, request!.TransponderId, request.InstallationId,
            request.FromUtc?.UtcDateTime, request.ToUtc?.UtcDateTime, request.Note, requestedBy, now);
        await storage.SaveChangesAsync(cancellationToken);
        loggerFactory.CreateLogger("StatisticsRecompute").LogInformation(
            "Recompute job {JobId} queued by {UserId}: transponder {TransponderId}, installation {InstallationId}, {From:o}..{To:o}",
            job.Id, requestedBy, job.TransponderId, job.InstallationId, job.FromUtc, job.ToUtc);
        var created = (await StatisticsOperations.ReadJobsAsync(storage, 1, cancellationToken, job.Id))[0];
        return TypedResults.Accepted($"/api/statistics/admin/recompute/jobs/{job.Id}", created);
    }

    internal static Dictionary<string, string[]> Validate(CreateRecomputeJobRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["A request body is required."];
            return errors;
        }
        if (request.FromUtc is { } from && request.ToUtc is { } to && from >= to)
            errors[nameof(request.ToUtc)] = ["ToUtc must be after FromUtc."];
        if (request.TransponderId == Guid.Empty) errors[nameof(request.TransponderId)] = ["TransponderId must not be empty."];
        if (request.InstallationId == Guid.Empty) errors[nameof(request.InstallationId)] = ["InstallationId must not be empty."];
        if (request.TransponderId is not null && request.InstallationId is not null)
            errors[nameof(request.InstallationId)] = ["Give either TransponderId or InstallationId, not both."];
        if (request.Note is { Length: > StatisticsOperations.NoteMaxLength })
            errors[nameof(request.Note)] = [$"Note must be at most {StatisticsOperations.NoteMaxLength} characters."];
        return errors;
    }
}
