using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class SampleEndpoint
{
    internal const int MaxCount = 500;

    internal static void MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sample = endpoints.MapGroup("sample");
        sample.MapGet("", ListSamples);
    }

    /// <summary>
    /// Samples are derived on read and never stored: each passing is paired with the passing immediately before it
    /// for the same transponder at the same installation, ordered by (Time, Id). This equals
    /// <c>LAG() OVER (PARTITION BY installation_id, transponder_id ORDER BY time, id)</c>, expressed as a
    /// correlated lookup so the newest-first limit can use the (installation, transponder, time, id) index
    /// instead of windowing the whole passing table. The result does not depend on arrival order.
    /// </summary>
    async static Task<Ok<List<SampleDTO>>> ListSamples(TimingDbContext storage, [FromQuery] int count = 50)
    {
        count = Math.Clamp(count, 0, MaxCount);
        var passings = storage.Set<Passing>().AsNoTracking();

        var samples = await passings
            .Select(end => new
            {
                End = end,
                Start = passings
                    .Where(start => start.InstallationId == end.InstallationId
                        && start.TransponderId == end.TransponderId
                        && (start.Time < end.Time || start.Time == end.Time && start.Id.CompareTo(end.Id) < 0))
                    .OrderByDescending(start => start.Time)
                    .ThenByDescending(start => start.Id)
                    .FirstOrDefault()
            })
            .Where(pair => pair.Start != null)
            .OrderByDescending(pair => pair.End.Time)
            .ThenByDescending(pair => pair.End.Id)
            .Take(count)
            .Select(pair => new SampleDTO(
                pair.End.TransponderId,
                pair.Start!.TimingPoint.Description,
                pair.End.TimingPoint.Description,
                pair.End.Time,
                pair.End.Time - pair.Start.Time))
            .ToListAsync();

        return TypedResults.Ok(samples);
    }
}
