using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Endpoints;

internal static class SampleEndpoint
{
    internal static void MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sample = endpoints.MapGroup("/sample");

        sample.MapGet("", GetSamples);
    }

    static async Task<Ok<GetSamplesResponse>> GetSamples(StatisticsDbContext storage, [AsParameters] GetSamplesRequest samplesRequest)
    {
        IQueryable<Sample> samples = storage.Set<Sample>().OrderByDescending(s => s.TimeEnd);
        int takeAmount = samplesRequest.PageSize + 1;

        if (samplesRequest.Cursor.HasValue)
        {
            if (samplesRequest.IsNextPage == true)
            {
                samples = samples.Where(s => s.TimeEnd < samplesRequest.Cursor.Value);
            }
            else
            {
                samples = samples.Where(s => s.TimeEnd > samplesRequest.Cursor.Value).OrderBy(s => s.TimeEnd);
            }
        }

        samples = samples.Take(takeAmount);

        if (samplesRequest.IsNextPage == false && samplesRequest.Cursor.HasValue)
        {
            samples = samples.Reverse();
        }

        var samplesDto = await samples.Select(s => new SampleDTO(
            s.TimeEnd,
            s.TransponderId,
            s.CoursePointStartId,
            s.CoursePointEndId,
            s.Distance,
            s.Duration,
            s.Speed * 3.6 // m/s to km/h
        )).AsNoTracking().ToListAsync();

        bool isFirstPage = !samplesRequest.Cursor.HasValue 
            || (samplesRequest.Cursor.HasValue && samplesDto.First().Time
            == storage.Set<Sample>().OrderByDescending(s => s.TimeEnd).First().TimeEnd);

        bool hasNaextPage = samplesDto.Count > samplesRequest.PageSize ||
            (samplesRequest.Cursor.HasValue && samplesRequest.IsNextPage == false);

        if (samplesDto.Count > samplesRequest.PageSize)
        {
            samplesDto.RemoveAt(samplesDto.Count - 1);
        }

        DateTime? nextCursor = hasNaextPage ? samplesDto.Last().Time : null;
        DateTime? previousCursor = samplesDto.Count > 0 && !isFirstPage ? samplesDto.First().Time : null;

        var response = new GetSamplesResponse
        (
            Samples: samplesDto,
            Next: nextCursor,
            Previous: previousCursor,
            IsFirstPage: isFirstPage
        );

        return TypedResults.Ok(response);
    }
}
