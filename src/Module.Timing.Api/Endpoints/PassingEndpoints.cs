using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class PassingEndpoints
{
    internal static void MapPassingEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/passings", List)
            .RequireAuthorization(AdministrationAuthorization.TransponderStatusAdminPolicy)
            .WithName("GetTransponderPassings")
            .WithSummary("Get up to 500 raw passings for a transponder in an inclusive UTC window")
            .WithDescription("Requires transponderId (nonempty UUID), from and to (ISO 8601 UTC timestamps with Z or +00:00). The inclusive window may be zero through seven days. Unknown, empty, malformed and duplicate query parameters return 400. Ordered by Time, then Id; Truncated indicates additional matches. Responses are no-store.")
            .Produces<TransponderPassingsDTO>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
            {
                // Raw parsing is authoritative at runtime; publish the required typed contract
                // so generated clients cannot accidentally omit the identity or either bound.
                // Keep UTC query instants as strings: the existing NSwag default date-time
                // query format drops offsets and fractions. Do not change global generation.
                foreach (var parameter in (operation.Parameters ?? []).OfType<OpenApiParameter>())
                {
                    parameter.Required = true;
                    parameter.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = parameter.Name == "transponderId" ? "uuid" : null
                    };
                    parameter.Description = parameter.Name == "transponderId"
                        ? "Required nonempty transponder UUID."
                        : "Required inclusive UTC instant (Z or +00:00); window at most seven days.";
                }
                return Task.CompletedTask;
            });

    // String parameters avoid framework binding short-circuiting the explicit ValidationProblem.
    // Raw single-valued parsing also catches duplicate and unsupported parameters.
    private static async Task<Results<Ok<TransponderPassingsDTO>, ValidationProblem>> List(
        HttpContext http, TimingDbContext storage, CancellationToken cancellationToken,
        string? transponderId = null, string? from = null, string? to = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var errors = new Dictionary<string, string[]>();
        foreach (var pair in http.Request.Query)
        {
            if (pair.Key is not ("transponderId" or "from" or "to"))
                errors[pair.Key] = ["Unsupported query parameter."];
            else if (pair.Value.Count != 1)
                errors[pair.Key] = ["Supply this query parameter exactly once."];
        }
        if (errors.Count != 0) return TypedResults.ValidationProblem(errors);
        if (!Guid.TryParseExact(transponderId, "D", out var id) || id == Guid.Empty)
            errors["transponderId"] = ["Supply a nonempty transponder UUID."];
        var validFrom = TryParseUtc(from, out var start);
        var validTo = TryParseUtc(to, out var end);
        if (!validFrom) errors["from"] = ["Supply an ISO 8601 UTC timestamp with Z or +00:00."];
        if (!validTo) errors["to"] = ["Supply an ISO 8601 UTC timestamp with Z or +00:00."];
        if (validFrom && validTo)
        {
            if (start > end) errors["to"] = ["To must be greater than or equal to From."];
            else if (end - start > TimeSpan.FromDays(7))
                errors["to"] = ["The window must not exceed seven days."];
        }
        if (errors.Count != 0) return TypedResults.ValidationProblem(errors);

        // Npgsql truncates timestamp parameters to microseconds. Round the lower bound
        // inward to avoid including a stored passing just before a sub-microsecond From.
        var fromUtc = start.UtcDateTime;
        var remainder = fromUtc.Ticks % 10;
        // DateTime.MaxValue is within the last microsecond: no stored instant can match.
        if (remainder != 0 && fromUtc.Ticks > DateTime.MaxValue.Ticks - (10 - remainder))
            return TypedResults.Ok(new TransponderPassingsDTO(id, start, end, [], false));
        if (remainder != 0) fromUtc = fromUtc.AddTicks(10 - remainder);
        var toUtc = end.UtcDateTime;
        const int limit = 500;
        var rows = await storage.Set<Passing>().AsNoTracking()
            .Where(passing => passing.TransponderId == id && passing.Time >= fromUtc && passing.Time <= toUtc)
            .OrderBy(passing => passing.Time).ThenBy(passing => passing.Id)
            .Take(limit + 1)
            .Select(passing => new PassingDTO(passing.Id,
                new DateTimeOffset(DateTime.SpecifyKind(passing.Time, DateTimeKind.Utc)),
                passing.TimingPointId, passing.TimingPoint.Description,
                passing.TimingPoint.InstallationId, passing.TimingPoint.Installation.Description,
                // The facility the installation served when the passing happened.
                passing.TimingPoint.Installation.FacilityAssignments
                    .Where(a => (a.ValidFromUtc == null || a.ValidFromUtc <= passing.Time) && (a.ValidToUtc == null || passing.Time < a.ValidToUtc))
                    .Select(a => (Guid?)a.FacilityId).FirstOrDefault() ?? Guid.Empty, passing.LowBattery, passing.LowStrength, passing.LowHits))
            .ToListAsync(cancellationToken);
        var truncated = rows.Count > limit;
        if (truncated) rows.RemoveAt(limit);
        return TypedResults.Ok(new TransponderPassingsDTO(id, start, end, rows, truncated));
    }

    private static bool TryParseUtc(string? text, out DateTimeOffset value)
    {
        // Require an explicit zero offset; never interpret an offsetless value as server-local.
        value = default;
        if (text is null || !(text.EndsWith("Z", StringComparison.Ordinal) ||
            text.EndsWith("+00:00", StringComparison.Ordinal)) ||
            text.EndsWith(".Z", StringComparison.Ordinal) || text.EndsWith(".+00:00", StringComparison.Ordinal)) return false;
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
        return DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out value) && value.Offset == TimeSpan.Zero;
    }
}
