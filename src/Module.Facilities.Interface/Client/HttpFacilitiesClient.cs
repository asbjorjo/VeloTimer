using System.Diagnostics;
using System.Net.Http.Json;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Instrumentation;

namespace VeloTime.Module.Facilities.Interface.Client;

public class HttpFacilitiesClient(HttpClient httpClient) : IFacitiliesClient
{
    public Task CreateCourseLayout(CourseLayoutDTO Layout)
    {
        throw new NotImplementedException();
    }

    public async Task<double> DistanceBetweenCoursePoints(Guid Start, Guid End)
    {
        using var activity = Activities.Source.StartActivity("DistanceBetweenCoursePoints");
        activity?.SetTag("StartCoursePointId", Start);
        activity?.SetTag("EndCoursePointId", End);

        try
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return await httpClient.GetFromJsonAsync<double>($"coursepoint/{Start}/distance/{End}");
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new Exception("Error retrieving distance between course points", ex);
        }
    }

    public async Task<CoursePointDistance?> DistanceBetweenTimingPoints(Guid Start, Guid End)
    {
        using var activity = Activities.Source.StartActivity("DistanceBetweenTimingPoints");
        activity?.SetTag("StartTimingPointId", Start);
        activity?.SetTag("EndTimingPointId", End);

        try
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return await httpClient.GetFromJsonAsync<CoursePointDistance>($"timingpoint/{Start}/distance/{End}");
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new Exception("Error retrieving distance between timing points", ex);
        }
    }

    public async Task<ICollection<FacilityDTO>> GetAllFacilitiesAsync(CancellationToken token = default)
    {
        return await httpClient.GetFromJsonAsync<ICollection<FacilityDTO>>("facility", cancellationToken: token);
    }

    public async Task<CourseLayoutDTO> GetCourseLayoutAsync(Guid layoutId, CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<CourseLayoutDTO>($"layouts/{layoutId}", cancellationToken);
    }

    public async Task<CoursePointDTO> GetCoursePointById(Guid coursePointId)
    {
        using var activity = Activities.Source.StartActivity("GetCoursePointById");
        activity?.SetTag("CoursePointId", coursePointId);

        try
        {
            return await httpClient.GetFromJsonAsync<CoursePointDTO>($"coursepoint/{coursePointId}");
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new Exception("Error retrieving coursepoint", ex);
        }
    }

    public Task<CoursePointDTO> GetCoursePointByTimingPointId(Guid timingPointId)
    {
        throw new NotImplementedException();
    }

    public async Task<FacilityDTO> GetFacilityAsync(Guid facilityId)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<FacilityDTO>($"facility/{facilityId}");
        }
        catch (HttpRequestException ex)
        {
            throw new Exception("Error retrieving facility", ex);
        }
    }

    public async Task<ICollection<CourseLayoutDTO>> GetFacilityCourseLayoutsAsync(Guid facilityId, CancellationToken token)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<ICollection<CourseLayoutDTO>>($"facility/{facilityId}/layouts", cancellationToken: token);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception("Error retrieving course layouts", ex);
        }
    }
}
