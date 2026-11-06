using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Service;

namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class FacilitiesOutputCache
{
    public const string PolicyName = "Relationships";
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);
    public const string Tag = FacilitiesCache.RelationshipsTag;
    private static readonly TimeSpan EvictionTimeout = TimeSpan.FromSeconds(5);

    public static Task InvalidateRelationshipsAsync(HybridCache cache, IOutputCacheStore output, ILogger logger) =>
        InvalidateRelationshipsCoreAsync(cache, output, logger, null);

    public static Task InvalidateRelationshipsWithOperationsAsync(HybridCache cache, IOutputCacheStore output, ILogger logger,
        ICacheInvalidationOperations operations) =>
        InvalidateRelationshipsCoreAsync(cache, output, logger, operations);

    private static async Task InvalidateRelationshipsCoreAsync(HybridCache cache, IOutputCacheStore output, ILogger logger,
        ICacheInvalidationOperations? operations)
    {
        List<Exception>? failures = null;
        try
        {
            await FacilitiesCache.InvalidateRelationshipsAsync(cache, logger, operations);
        }
        catch (Exception exception)
        {
            failures = [exception];
        }

        try
        {
            using var timeout = new CancellationTokenSource(EvictionTimeout);
            await output.EvictByTagAsync(Tag, timeout.Token);
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }

        if (failures is not null)
        {
            var failure = new AggregateException("One or more Facilities relationship cache stores could not be invalidated after a durable write.", failures);
            logger.LogError(failure, "Facilities relationship cache invalidation failed for {FailureCount} store(s).", failures.Count);
            throw failure;
        }
    }
}
