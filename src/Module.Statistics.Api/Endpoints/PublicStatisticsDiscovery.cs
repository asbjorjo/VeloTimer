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

/// <summary>Per-request discovery and targeted selection validation; no result-table or cross-service database access.</summary>
internal sealed class PublicStatisticsDiscovery(
    StatisticsDbContext storage,
    ICoursePointReferences points,
    IPublicCoursePointNames names,
    ILogger<PublicStatisticsDiscovery> logger)
{
    public async Task<bool> IsSelectionAvailableAsync(
        GetPublicStatisticsResultsRequest request, CancellationToken cancellationToken)
    {
        var config = await storage.Set<StatisticsItemConfig>().AsNoTracking()
            .SingleOrDefaultAsync(config => config.Id == request.StatisticsItemConfigId, cancellationToken);
        if (config is null || config.StatisticsItemId != request.StatisticsItemId)
        {
            logger.LogWarning("Public statistics selection {ConfigurationId} does not match an existing configuration/item.",
                request.StatisticsItemConfigId);
            return false;
        }

        var parent = config is MultiStatisticsItemConfig multi
            ? await storage.Set<StatisticsItemConfig>().AsNoTracking().OfType<SimpleStatisticsItemConfig>()
                .SingleOrDefaultAsync(simple => simple.Id == multi.ParentConfigId, cancellationToken)
            : null;
        var selectedItemIds = parent is null
            ? new[] { config.StatisticsItemId }
            : new[] { config.StatisticsItemId, parent.StatisticsItemId };
        var itemModes = await storage.Set<StatisticsItem>().AsNoTracking()
            .Where(item => selectedItemIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.DistanceMode, cancellationToken);
        var duplicateLoop = config is SimpleStatisticsItemConfig selectedSimple &&
            itemModes.GetValueOrDefault(config.StatisticsItemId) == StatisticsDistanceMode.CourseLayout &&
            await storage.Set<SimpleStatisticsItemConfig>().AsNoTracking().AnyAsync(other =>
                other.Id != config.Id && other.StatisticsItemId == config.StatisticsItemId &&
                other.CoursePointStart == selectedSimple.CoursePointStart && other.CoursePointEnd == selectedSimple.CoursePointEnd, cancellationToken);
        var reason = ValidateDefinition(config, parent, itemModes, duplicateLoop, out var endpoints);
        if (reason is not null)
        {
            LogUnavailable(config, reason.Value);
            return false;
        }

        // Only selected endpoint membership is needed for results, never display names.
        var resolvedFacilities = new Dictionary<Guid, Guid?>();
        var start = await ResolveFacilityAsync(endpoints!.CoursePointStart, resolvedFacilities, cancellationToken);
        var end = await ResolveFacilityAsync(endpoints.CoursePointEnd, resolvedFacilities, cancellationToken);
        reason = ValidateFacilities(start, end);
        if (reason is not null)
        {
            LogUnavailable(config, reason.Value);
            return false;
        }
        if (start != request.FacilityId)
        {
            logger.LogWarning("Public statistics selection {ConfigurationId} does not match facility {FacilityId}.",
                config.Id, request.FacilityId);
            return false;
        }
        return true;
    }

    public async Task<PublicStatisticsDiscoveryDTO> GetAsync(CancellationToken cancellationToken)
    {
        var items = await storage.Set<StatisticsItem>().AsNoTracking()
            .Select(item => new StatisticsItemDTO(item.Id, item.Name, item.Description, item.Distance, item.MinDuration, item.MaxDuration) { DistanceMode = item.DistanceMode })
            .ToListAsync(cancellationToken);
        var definitions = await storage.Set<StatisticsItemConfig>().AsNoTracking().OrderBy(config => config.Id)
            .ToListAsync(cancellationToken);
        var itemModes = items.ToDictionary(item => item.Id, item => item.DistanceMode ?? StatisticsDistanceMode.Fixed);
        var duplicateLoops = definitions.OfType<SimpleStatisticsItemConfig>()
            .GroupBy(config => (config.StatisticsItemId, config.CoursePointStart, config.CoursePointEnd))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var simpleParents = definitions.OfType<SimpleStatisticsItemConfig>().ToDictionary(config => config.Id);
        var resolvedFacilities = new Dictionary<Guid, Guid?>();
        var resolvedPoints = new Dictionary<Guid, (Guid? Facility, string? Name)>();
        var available = new List<PublicStatisticsConfigurationDTO>();
        var unavailable = new List<UnavailablePublicStatisticsConfigurationDTO>();

        async Task<(Guid? Facility, string? Name)> ResolvePoint(Guid id)
        {
            if (resolvedPoints.TryGetValue(id, out var cached)) return cached;
            var facility = await ResolveFacilityAsync(id, resolvedFacilities, cancellationToken);
            var name = await names.GetNameAsync(id, cancellationToken);
            var result = (facility, name);
            resolvedPoints.Add(id, result);
            return result;
        }

        void Unavailable(StatisticsItemConfig config, PublicStatisticsConfigurationUnavailableReason reason)
        {
            unavailable.Add(new UnavailablePublicStatisticsConfigurationDTO(ToDto(config), reason));
            LogUnavailable(config, reason);
        }

        foreach (var config in definitions)
        {
            var parent = config is MultiStatisticsItemConfig multi
                ? simpleParents.GetValueOrDefault(multi.ParentConfigId) : null;
            var duplicateLoop = config is SimpleStatisticsItemConfig candidate &&
                duplicateLoops.Contains((candidate.StatisticsItemId, candidate.CoursePointStart, candidate.CoursePointEnd));
            var reason = ValidateDefinition(config, parent, itemModes, duplicateLoop, out var simple);
            if (reason is not null)
            {
                Unavailable(config, reason.Value);
                continue;
            }
            var start = await ResolvePoint(simple!.CoursePointStart);
            var end = await ResolvePoint(simple.CoursePointEnd);
            if (start.Name is null || end.Name is null)
            {
                Unavailable(config, PublicStatisticsConfigurationUnavailableReason.MissingCoursePoint);
                continue;
            }
            reason = ValidateFacilities(start.Facility, end.Facility);
            if (reason is not null)
            {
                Unavailable(config, reason.Value);
                continue;
            }
            var dto = ToDto(config);
            available.Add(new PublicStatisticsConfigurationDTO(config.Id, config.StatisticsItemId,
                start.Facility!.Value, dto.Type, simple.CoursePointStart, start.Name, simple.CoursePointEnd,
                end.Name, dto.ParentConfigId, dto.Repetitions));
        }

        // Discovery definitions are small metadata, not result rows. Explicit ordinal name order.
        return new PublicStatisticsDiscoveryDTO(
            items.OrderBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal).ToList(),
            available, unavailable);
    }

    private static PublicStatisticsConfigurationUnavailableReason? ValidateDefinition(
        StatisticsItemConfig config, SimpleStatisticsItemConfig? parent, IReadOnlyDictionary<Guid, StatisticsDistanceMode> itemModes,
        bool duplicateLoop, out SimpleStatisticsItemConfig? endpoints)
    {
        endpoints = null;
        if (config.Id == Guid.Empty || config.StatisticsItemId == Guid.Empty)
            return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
        if (!itemModes.TryGetValue(config.StatisticsItemId, out var mode))
            return PublicStatisticsConfigurationUnavailableReason.MissingItem;
        if (mode is not (StatisticsDistanceMode.Fixed or StatisticsDistanceMode.CourseLayout))
            return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
        if (config is SimpleStatisticsItemConfig simple)
        {
            if (mode == StatisticsDistanceMode.CourseLayout && (simple.CoursePointStart != simple.CoursePointEnd || duplicateLoop))
                return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
            endpoints = simple;
        }
        else if (config is MultiStatisticsItemConfig multi)
        {
            if (mode == StatisticsDistanceMode.CourseLayout || multi.ParentConfigId == Guid.Empty || multi.Repetitions <= 0)
                return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
            if (parent is null)
                return PublicStatisticsConfigurationUnavailableReason.MissingSimpleParent;
            if (parent.StatisticsItemId == Guid.Empty)
                return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
            if (!itemModes.TryGetValue(parent.StatisticsItemId, out var parentMode))
                return PublicStatisticsConfigurationUnavailableReason.MissingItem;
            if (parentMode != StatisticsDistanceMode.Fixed)
                return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
            endpoints = parent;
        }
        else return PublicStatisticsConfigurationUnavailableReason.InvalidDefinition;
        return endpoints.CoursePointStart == Guid.Empty || endpoints.CoursePointEnd == Guid.Empty
            ? PublicStatisticsConfigurationUnavailableReason.InvalidDefinition : null;
    }

    private async Task<Guid?> ResolveFacilityAsync(
        Guid id, Dictionary<Guid, Guid?> resolvedFacilities, CancellationToken cancellationToken)
    {
        if (resolvedFacilities.TryGetValue(id, out var cached)) return cached;
        var facility = await points.GetFacilityAsync(id, cancellationToken);
        if (facility == Guid.Empty) throw new HttpRequestException("Facilities returned an invalid facility ID.");
        resolvedFacilities.Add(id, facility);
        return facility;
    }

    private static PublicStatisticsConfigurationUnavailableReason? ValidateFacilities(Guid? start, Guid? end) =>
        start is null || end is null ? PublicStatisticsConfigurationUnavailableReason.MissingFacility :
        start != end ? PublicStatisticsConfigurationUnavailableReason.DifferentFacilities : null;

    private void LogUnavailable(StatisticsItemConfig config, PublicStatisticsConfigurationUnavailableReason reason) =>
        logger.LogWarning("Public statistics configuration {ConfigurationId} unavailable: {Reason}", config.Id, reason);

    private static StatisticsConfigurationDTO ToDto(StatisticsItemConfig config) => config switch
    {
        SimpleStatisticsItemConfig simple => new(config.Id, config.StatisticsItemId,
            StatisticsConfigurationType.Simple, simple.CoursePointStart, simple.CoursePointEnd, null, null, config.MinDuration, config.MaxDuration)
        { RequiredCoursePoints = simple.RequiredCoursePointIds.Length == 0 ? null : new(simple.RequiredCourseLayoutId, simple.RequiredCoursePointIds.ToArray()) },
        MultiStatisticsItemConfig multi => new(config.Id, config.StatisticsItemId,
            StatisticsConfigurationType.Multi, null, null, multi.ParentConfigId, multi.Repetitions, config.MinDuration, config.MaxDuration),
        _ => throw new InvalidOperationException("Unknown statistics configuration type.")
    };
}
