using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class SampleEndpoint
{
    internal static void MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sample = endpoints.MapGroup("sample");
        sample.MapGet("", ListSamples);
    }

    async static Task<Ok<List<SampleDTO>>> ListSamples(TimingDbContext storage, [FromQuery] int count = 50)
    {
        return TypedResults.Ok(
            await storage.Set<Sample>()
                .OrderByDescending(s => s.End.Time)
                .Take(count)
                .Select(s => new SampleDTO(
                    s.Start.TransponderId,
                    s.Start.TimingPoint.Description,
                    s.End.TimingPoint.Description,
                    s.End.Time,
                    s.End.Time - s.Start.Time
                ))
                .ToListAsync()
        );
    }
}
