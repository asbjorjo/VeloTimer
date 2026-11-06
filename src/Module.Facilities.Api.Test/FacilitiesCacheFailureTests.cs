using System.Reflection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesCacheFailureTests
{
    [Fact]
    public async Task OutputStoreEvictionIsAttemptedWhenHybridTagEvictionFails()
    {
        var hybrid = new FailingHybridCache { FailOnTagEviction = true };
        var output = new RecordingOutputCacheStore();

        await Assert.ThrowsAsync<AggregateException>(() => InvalidateRelationshipsAsync(hybrid, output));

        Assert.Equal(1, hybrid.TagEvictionCalls);
        Assert.Equal(1, output.EvictionCalls);
    }

    [Fact]
    public async Task OutputStoreFailureIsPropagatedAfterSuccessfulHybridTagEviction()
    {
        var hybrid = new FailingHybridCache();
        var output = new RecordingOutputCacheStore { FailOnEviction = true };

        var failure = await Assert.ThrowsAsync<AggregateException>(() => InvalidateRelationshipsAsync(hybrid, output));

        Assert.Single(failure.InnerExceptions);
        Assert.Equal(1, hybrid.TagEvictionCalls);
        Assert.Equal(1, output.EvictionCalls);
    }

    private static Task InvalidateRelationshipsAsync(HybridCache cache, RecordingOutputCacheStore output)
    {
        var helperType = typeof(Program).Assembly.GetType(
            "VeloTime.Module.Facilities.Api.Endpoints.FacilitiesOutputCache",
            throwOnError: true)!;
        var method = helperType.GetMethod("InvalidateRelationshipsAsync", BindingFlags.Public | BindingFlags.Static)!;
        return (Task)method.Invoke(null, [cache, output, NullLogger.Instance])!;
    }

    private sealed class FailingHybridCache : HybridCache
    {
        public bool FailOnTagEviction { get; init; }
        public int TagEvictionCalls { get; private set; }

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) =>
            factory(state, cancellationToken);

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            TagEvictionCalls++;
            return FailOnTagEviction
                ? ValueTask.FromException(new InvalidOperationException("injected HybridCache tag failure"))
                : ValueTask.CompletedTask;
        }
    }
}
