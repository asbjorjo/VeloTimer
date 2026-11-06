using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsCacheInvalidationTests
{
    [Fact]
    public async Task InvalidationAttemptsAllTagsThenKeysAndAggregatesFailures()
    {
        var cache = new RecordingHybridCache { FailingTag = "tag-fails", FailingKey = "key-fails" };

        var failure = await Assert.ThrowsAsync<AggregateException>(() => StatisticsCacheInvalidation.InvalidateAsync(
            cache, NullLogger.Instance, ["key-fails", "key-ok", "key-ok"], ["tag-fails", "tag-ok", "tag-ok"]));

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(["tag:tag-fails", "tag:tag-ok", "key:key-fails", "key:key-ok"], cache.Operations);
    }

    [Fact]
    public async Task InvalidationUsesFreshBoundedTokenIndependentOfAnyRequest()
    {
        var cache = new RecordingHybridCache();

        await StatisticsCacheInvalidation.InvalidateAsync(cache, NullLogger.Instance, ["key"], ["tag"]);

        Assert.Equal(["tag:tag", "key:key"], cache.Operations);
        Assert.All(cache.Tokens, observed =>
        {
            Assert.True(observed.Token.CanBeCanceled);
            Assert.False(observed.WasCanceledWhenCalled);
        });
    }
}

internal sealed record ObservedToken(CancellationToken Token, bool WasCanceledWhenCalled);

internal sealed class RecordingHybridCache : HybridCache
{
    public List<string> Operations { get; } = [];
    public List<ObservedToken> Tokens { get; } = [];
    public string? FailingTag { get; init; }
    public string? FailingKey { get; init; }

    public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
        factory(state, cancellationToken);

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Operations.Add($"key:{key}");
        Tokens.Add(new ObservedToken(cancellationToken, cancellationToken.IsCancellationRequested));
        return key == FailingKey ? ValueTask.FromException(new InvalidOperationException("injected key failure")) : ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        Operations.Add($"tag:{tag}");
        Tokens.Add(new ObservedToken(cancellationToken, cancellationToken.IsCancellationRequested));
        return tag == FailingTag ? ValueTask.FromException(new InvalidOperationException("injected tag failure")) : ValueTask.CompletedTask;
    }
}
