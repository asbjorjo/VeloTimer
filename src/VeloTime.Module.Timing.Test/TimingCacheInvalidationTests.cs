using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Test;

public sealed class TimingCacheInvalidationTests
{
    [Fact]
    public async Task InvalidationAttemptsEveryTagBeforeKeysAndPropagatesFailures()
    {
        var cache = new RecordingHybridCache { FailingTag = "tag-fails" };

        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            TimingCacheInvalidation.InvalidateAsync(
                cache,
                NullLogger.Instance,
                ["key-a", "key-a", "key-b"],
                ["tag-fails", "tag-good", "tag-good"]));

        Assert.Single(failure.InnerExceptions);
        Assert.Equal(
            ["tag:tag-fails", "tag:tag-good", "key:key-a", "key:key-b"],
            cache.Operations);
        Assert.All(cache.CancellationTokens, token => Assert.True(token.CanBeCanceled));
    }

    [Fact]
    public async Task InvalidationUsesFreshBoundedPostCommitTimeoutToken()
    {
        var cache = new RecordingHybridCache();

        await TimingCacheInvalidation.InvalidateAsync(
            cache, NullLogger.Instance, ["one-key"], ["one-tag"]);

        Assert.Equal(["tag:one-tag", "key:one-key"], cache.Operations);
        Assert.All(cache.CancellationTokens, token => Assert.True(token.CanBeCanceled));
    }

    private sealed class RecordingHybridCache : HybridCache
    {
        public List<string> Operations { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];
        public string? FailingTag { get; init; }

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

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Operations.Add($"key:{key}");
            CancellationTokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            Operations.Add($"tag:{tag}");
            CancellationTokens.Add(cancellationToken);
            return FailingTag == tag
                ? ValueTask.FromException(new InvalidOperationException("injected tag failure"))
                : ValueTask.CompletedTask;
        }
    }
}
