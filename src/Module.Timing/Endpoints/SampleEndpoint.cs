using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Endpoints;

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
