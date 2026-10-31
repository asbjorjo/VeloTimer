using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

public interface IPublicCoursePointNames
{
    Task<string?> GetNameAsync(Guid pointId, CancellationToken cancellationToken);
}

internal sealed class PublicCoursePointNames(HttpClient client) : IPublicCoursePointNames
{
    public async Task<string?> GetNameAsync(Guid pointId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/facilities/coursepoint/{pointId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var point = await response.Content.ReadFromJsonAsync<PointName>(cancellationToken);
        if (point is null || point.Id != pointId || point.Name is null)
            throw new HttpRequestException("Facilities returned an invalid coursepoint response.");
        return point.Name;
    }

    private sealed record PointName(Guid Id, string Name);
}

/// <summary>Per-request discovery; no result-table or cross-service database access.</summary>
internal sealed class PublicStatisticsDiscovery(
    StatisticsDbContext storage,
    ICoursePointReferences points,
    IPublicCoursePointNames names,
    ILogger<PublicStatisticsDiscovery> logger)
{
    public async Task<PublicStatisticsDiscoveryDTO> GetAsync(CancellationToken cancellationToken)
    {
        var items = await storage.Set<StatisticsItem>().AsNoTracking()
            .Select(item => new StatisticsItemDTO(item.Id, item.Name, item.Description, item.Distance))
            .ToListAsync(cancellationToken);
        var definitions = await storage.Set<StatisticsItemConfig>().AsNoTracking().OrderBy(config => config.Id)
            .ToListAsync(cancellationToken);
        var itemIds = items.Select(item => item.Id).ToHashSet();
        var simpleParents = definitions.OfType<SimpleStatisticsItemConfig>().ToDictionary(config => config.Id);
        var resolvedPoints = new Dictionary<Guid, (Guid? Facility, string? Name)>();
        var available = new List<PublicStatisticsConfigurationDTO>();
        var unavailable = new List<UnavailablePublicStatisticsConfigurationDTO>();

        async Task<(Guid? Facility, string? Name)> ResolvePoint(Guid id)
        {
            if (resolvedPoints.TryGetValue(id, out var cached)) return cached;
            var facility = await points.GetFacilityAsync(id, cancellationToken);
            if (facility == Guid.Empty) throw new HttpRequestException("Facilities returned an invalid facility ID.");
            var name = await names.GetNameAsync(id, cancellationToken);
            var result = (facility, name);
            resolvedPoints.Add(id, result);
            return result;
        }

        void Unavailable(StatisticsItemConfig config, PublicStatisticsConfigurationUnavailableReason reason)
        {
            unavailable.Add(new UnavailablePublicStatisticsConfigurationDTO(ToDto(config), reason));
            logger.LogWarning("Public statistics configuration {ConfigurationId} unavailable: {Reason}", config.Id, reason);
        }

        foreach (var config in definitions)
        {
            if (config.Id == Guid.Empty || config.StatisticsItemId == Guid.Empty)
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.InvalidDefinition);
                continue;
            }
            if (!itemIds.Contains(config.StatisticsItemId))
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.MissingItem);
                continue;
            }
            SimpleStatisticsItemConfig simple;
            if (config is SimpleStatisticsItemConfig direct) simple = direct;
            else if (config is MultiStatisticsItemConfig multi)
            {
                if (multi.ParentConfigId == Guid.Empty || multi.Repetitions <= 0)
                {
                    Unavailable(config, PublicStatisticsConfigurationUnavailableReason.InvalidDefinition);
                    continue;
                }
                if (!simpleParents.TryGetValue(multi.ParentConfigId, out var parent))
                {
                    Unavailable(config, PublicStatisticsConfigurationUnavailableReason.MissingSimpleParent);
                    continue;
                }
                if (parent.StatisticsItemId == Guid.Empty)
                {
                    Unavailable(config, PublicStatisticsConfigurationUnavailableReason.InvalidDefinition);
                    continue;
                }
                if (!itemIds.Contains(parent.StatisticsItemId))
                {
                    Unavailable(config, PublicStatisticsConfigurationUnavailableReason.MissingItem);
                    continue;
                }
                simple = parent;
            }
            else
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.InvalidDefinition);
                continue;
            }
            if (simple.CoursePointStart == Guid.Empty || simple.CoursePointEnd == Guid.Empty)
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.InvalidDefinition);
                continue;
            }
            var start = await ResolvePoint(simple.CoursePointStart);
            var end = await ResolvePoint(simple.CoursePointEnd);
            if (start.Name is null || end.Name is null)
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.MissingCoursePoint);
                continue;
            }
            if (start.Facility is null || end.Facility is null)
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.MissingFacility);
                continue;
            }
            if (start.Facility != end.Facility)
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.DifferentFacilities);
                continue;
            }
            var dto = ToDto(config);
            available.Add(new PublicStatisticsConfigurationDTO(config.Id, config.StatisticsItemId,
                start.Facility.Value, dto.Type, simple.CoursePointStart, start.Name, simple.CoursePointEnd,
                end.Name, dto.ParentConfigId, dto.Repetitions));
        }

        // Discovery definitions are small metadata, not result rows. Explicit ordinal name order.
        return new PublicStatisticsDiscoveryDTO(
            items.OrderBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal).ToList(),
            available, unavailable);
    }

    private static StatisticsConfigurationDTO ToDto(StatisticsItemConfig config) => config switch
    {
        SimpleStatisticsItemConfig simple => new(config.Id, config.StatisticsItemId,
            StatisticsConfigurationType.Simple, simple.CoursePointStart, simple.CoursePointEnd, null, null),
        MultiStatisticsItemConfig multi => new(config.Id, config.StatisticsItemId,
            StatisticsConfigurationType.Multi, null, null, multi.ParentConfigId, multi.Repetitions),
        _ => throw new InvalidOperationException("Unknown statistics configuration type.")
    };
}
