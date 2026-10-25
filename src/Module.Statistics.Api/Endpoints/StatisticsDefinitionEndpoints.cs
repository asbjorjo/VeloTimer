using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class StatisticsDefinitionEndpoints
{
    internal static void MapStatisticsDefinitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var definitions = endpoints.MapGroup("/admin").RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);
        definitions.MapGet("/items", ListItems).WithName("ListStatisticsItems");
        definitions.MapPost("/items", CreateItem).WithName("CreateStatisticsItem");
        definitions.MapPut("/items/{id:guid}", UpdateItem).WithName("UpdateStatisticsItem");
        definitions.MapDelete("/items/{id:guid}", DeleteItem).WithName("DeleteStatisticsItem");
        definitions.MapGet("/configurations", ListConfigurations).WithName("ListStatisticsConfigurations");
        definitions.MapPost("/configurations", CreateConfiguration).WithName("CreateStatisticsConfiguration");
        definitions.MapPut("/configurations/{id:guid}", UpdateConfiguration).WithName("UpdateStatisticsConfiguration");
        definitions.MapDelete("/configurations/{id:guid}", DeleteConfiguration).WithName("DeleteStatisticsConfiguration");
    }

    private static async Task<Ok<List<StatisticsItemDTO>>> ListItems(StatisticsDbContext storage, CancellationToken cancellationToken) =>
        TypedResults.Ok(await storage.Set<StatisticsItem>().AsNoTracking().OrderBy(item => item.Name)
            .Select(item => new StatisticsItemDTO(item.Id, item.Name, item.Description, item.Distance)).ToListAsync(cancellationToken));

    private static string? ValidateItem(SaveStatisticsItemRequest request) =>
        string.IsNullOrWhiteSpace(request.Name) ? "Name is required." :
        !float.IsFinite(request.Distance) || request.Distance <= 0 ? "Distance must be a finite positive number of metres." : null;

    private static async Task<Results<Created<StatisticsItemDTO>, ProblemHttpResult>> CreateItem(
        SaveStatisticsItemRequest request, StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        if (ValidateItem(request) is { } error)
        {
            return Invalid(error);
        }

        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = request.Name.Trim(), Description = request.Description ?? "", Distance = request.Distance };
        storage.Add(item);
        await storage.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/statistics/admin/items/{item.Id}", ToDto(item));
    }

    private static async Task<Results<Ok<StatisticsItemDTO>, NotFound, ProblemHttpResult>> UpdateItem(
        Guid id, SaveStatisticsItemRequest request, StatisticsDbContext storage, HybridCache cache, CancellationToken cancellationToken)
    {
        var item = await storage.Set<StatisticsItem>().FindAsync([id], cancellationToken);
        if (item is null) return TypedResults.NotFound();
        if (ValidateItem(request) is { } error) return Invalid(error);
        var configIds = storage.Set<StatisticsItemConfig>().Where(config => config.StatisticsItemId == id).Select(config => config.Id);
        if (item.Distance != request.Distance && await storage.Set<StatisticsEntry>()
            .AnyAsync(entry => entry.StatisticsItemId == id || configIds.Contains(entry.StatisticsItemConfigId), cancellationToken))
        {
            return Conflict("Recorded results depend on this distance. Create a new definition instead.");
        }

        item.Name = request.Name.Trim();
        item.Description = request.Description ?? "";
        item.Distance = request.Distance;
        await storage.SaveChangesAsync(cancellationToken);
        var configs = await storage.Set<StatisticsItemConfig>().Where(config => config.StatisticsItemId == id).ToListAsync(cancellationToken);
        foreach (var config in configs) await Invalidate(config, cache, cancellationToken);
        return TypedResults.Ok(ToDto(item));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteItem(
        Guid id, StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        var item = await storage.Set<StatisticsItem>().FindAsync([id], cancellationToken);
        if (item is null) return TypedResults.NotFound();
        var configs = await storage.Set<StatisticsItemConfig>().CountAsync(config => config.StatisticsItemId == id, cancellationToken);
        var results = await storage.Set<StatisticsEntry>().CountAsync(entry => entry.StatisticsItemId == id, cancellationToken);
        if (configs > 0 || results > 0) return Conflict($"This item is referenced by {configs} configuration(s) and {results} result(s).");
        storage.Remove(item);
        await storage.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<StatisticsConfigurationDTO>>> ListConfigurations(
        StatisticsDbContext storage, Guid? itemId, CancellationToken cancellationToken)
    {
        var query = storage.Set<StatisticsItemConfig>().AsNoTracking();
        if (itemId.HasValue) query = query.Where(config => config.StatisticsItemId == itemId);
        return TypedResults.Ok((await query.OrderBy(config => config.Id).ToListAsync(cancellationToken)).Select(ToDto).ToList());
    }

    private static async Task<string?> ValidateConfiguration(SaveStatisticsConfigurationRequest request,
        StatisticsDbContext storage, ICoursePointReferences points, CancellationToken cancellationToken)
    {
        if (!await storage.Set<StatisticsItem>().AnyAsync(item => item.Id == request.StatisticsItemId, cancellationToken)) return "Select an existing statistics item.";
        if (request.Type == StatisticsConfigurationType.Simple)
        {
            if (request.CoursePointStart.GetValueOrDefault() == Guid.Empty ||
                request.CoursePointEnd.GetValueOrDefault() == Guid.Empty) return "Start and end coursepoints are required.";
            if (request.ParentConfigId is not null || request.Repetitions is not null) return "Simple configurations cannot have a parent or repetitions.";
            var startFacility = await points.GetFacilityAsync(request.CoursePointStart!.Value, cancellationToken);
            var endFacility = request.CoursePointStart == request.CoursePointEnd ? startFacility : await points.GetFacilityAsync(request.CoursePointEnd!.Value, cancellationToken);
            if (startFacility is null || endFacility is null || startFacility != endFacility) return "Both coursepoints must exist in the same facility.";
        }
        else if (request.Type == StatisticsConfigurationType.Multi)
        {
            if (request.CoursePointStart is not null || request.CoursePointEnd is not null) return "Multi configurations inherit their coursepoints from their parent.";
            if (request.Repetitions is null or <= 0) return "Repetitions must be a positive integer.";
            if (!await storage.Set<SimpleStatisticsItemConfig>().AnyAsync(config => config.Id == request.ParentConfigId, cancellationToken)) return "Select an existing Simple configuration as the parent.";
        }
        else return "Unknown configuration type.";
        return null;
    }

    private static async Task<Results<Created<StatisticsConfigurationDTO>, ProblemHttpResult>> CreateConfiguration(
        SaveStatisticsConfigurationRequest request, StatisticsDbContext storage, ICoursePointReferences points, HybridCache cache, CancellationToken cancellationToken)
    {
        if (await ValidateConfiguration(request, storage, points, cancellationToken) is { } error) return Invalid(error);
        StatisticsItemConfig config = request.Type == StatisticsConfigurationType.Simple
            ? new SimpleStatisticsItemConfig { StatisticsItem = null!, CoursePointStart = request.CoursePointStart!.Value, CoursePointEnd = request.CoursePointEnd!.Value }
            : new MultiStatisticsItemConfig { StatisticsItem = null!, ParentConfig = null!, ParentConfigId = request.ParentConfigId!.Value, Repetitions = request.Repetitions!.Value };
        config.Id = Guid.NewGuid();
        config.StatisticsItemId = request.StatisticsItemId;
        storage.Add(config);
        await storage.SaveChangesAsync(cancellationToken);
        await Invalidate(config, cache, cancellationToken);
        return TypedResults.Created($"/api/statistics/admin/configurations/{config.Id}", ToDto(config));
    }

    private static async Task<Results<Ok<StatisticsConfigurationDTO>, NotFound, ProblemHttpResult>> UpdateConfiguration(
        Guid id, SaveStatisticsConfigurationRequest request, StatisticsDbContext storage, ICoursePointReferences points, HybridCache cache, CancellationToken cancellationToken)
    {
        var config = await storage.Set<StatisticsItemConfig>().FindAsync([id], cancellationToken);
        if (config is null) return TypedResults.NotFound();
        if (ToDto(config).Type != request.Type) return Invalid("Configuration type cannot be changed.");
        if (await ValidateConfiguration(request, storage, points, cancellationToken) is { } error) return Invalid(error);
        var proposed = new StatisticsConfigurationDTO(id, request.StatisticsItemId, request.Type, request.CoursePointStart, request.CoursePointEnd, request.ParentConfigId, request.Repetitions);
        var childIds = storage.Set<MultiStatisticsItemConfig>().Where(child => child.ParentConfigId == id).Select(child => child.Id);
        if (ToDto(config) != proposed && await storage.Set<StatisticsEntry>()
            .AnyAsync(entry => entry.StatisticsItemConfigId == id || childIds.Contains(entry.StatisticsItemConfigId), cancellationToken))
        {
            return Conflict("Recorded results depend on this configuration. Create a new definition instead.");
        }

        await Invalidate(config, cache, cancellationToken);
        config.StatisticsItemId = request.StatisticsItemId;
        if (config is SimpleStatisticsItemConfig simple)
        {
            simple.CoursePointStart = request.CoursePointStart!.Value;
            simple.CoursePointEnd = request.CoursePointEnd!.Value;
        }
        else if (config is MultiStatisticsItemConfig multi)
        {
            multi.ParentConfigId = request.ParentConfigId!.Value;
            multi.Repetitions = request.Repetitions!.Value;
        }
        await storage.SaveChangesAsync(cancellationToken);
        await Invalidate(config, cache, cancellationToken);
        return TypedResults.Ok(ToDto(config));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteConfiguration(
        Guid id, StatisticsDbContext storage, HybridCache cache, CancellationToken cancellationToken)
    {
        var config = await storage.Set<StatisticsItemConfig>().FindAsync([id], cancellationToken);
        if (config is null) return TypedResults.NotFound();
        var children = await storage.Set<MultiStatisticsItemConfig>().CountAsync(child => child.ParentConfigId == id, cancellationToken);
        var results = await storage.Set<StatisticsEntry>().CountAsync(entry => entry.StatisticsItemConfigId == id, cancellationToken);
        if (children > 0 || results > 0) return Conflict($"This configuration is referenced by {children} child configuration(s) and {results} result(s).");
        storage.Remove(config);
        await storage.SaveChangesAsync(cancellationToken);
        await Invalidate(config, cache, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task Invalidate(StatisticsItemConfig config, HybridCache cache, CancellationToken cancellationToken)
    {
        if (config is SimpleStatisticsItemConfig simple)
        {
            await cache.RemoveAsync($"StatisticsItemConfig_End_{simple.CoursePointEnd}", cancellationToken);
            await cache.RemoveAsync($"MultiStatisticsItemConfig_{simple.Id}", cancellationToken);
        }
        else if (config is MultiStatisticsItemConfig multi)
        {
            await cache.RemoveAsync($"MultiStatisticsItemConfig_{multi.ParentConfigId}", cancellationToken);
        }
    }

    private static StatisticsItemDTO ToDto(StatisticsItem item) => new(item.Id, item.Name, item.Description, item.Distance);
    private static StatisticsConfigurationDTO ToDto(StatisticsItemConfig config) => config switch
    {
        SimpleStatisticsItemConfig simple => new(simple.Id, simple.StatisticsItemId, StatisticsConfigurationType.Simple, simple.CoursePointStart, simple.CoursePointEnd, null, null),
        MultiStatisticsItemConfig multi => new(multi.Id, multi.StatisticsItemId, StatisticsConfigurationType.Multi, null, null, multi.ParentConfigId, multi.Repetitions),
        _ => throw new InvalidOperationException("Unknown configuration type.")
    };
    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict);
}