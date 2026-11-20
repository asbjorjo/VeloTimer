using System.Collections.Concurrent;
using Microsoft.AspNetCore.OutputCaching;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class RecordingOutputCacheStore : IOutputCacheStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<StoredResponse> stored = new();
    private int evictionCalls;
    private int getCalls;
    private int setCalls;
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<StoredResponse> Stored => stored.ToArray();
    public int EvictionCalls => Volatile.Read(ref evictionCalls);
    public int GetCalls => Volatile.Read(ref getCalls);
    public int SetCalls => Volatile.Read(ref setCalls);
    public bool FailOnEviction { get; set; }

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref getCalls);
        if (!entries.TryGetValue(key, out var entry))
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        if (entry.ExpiresAt <= now)
        {
            entries.TryRemove(key, out _);
            return ValueTask.FromResult<byte[]?>(null);
        }

        return ValueTask.FromResult<byte[]?>(entry.Value);
    }

    public ValueTask SetAsync(
        string key,
        byte[] value,
        string[]? tags,
        TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref setCalls);
        var expiresAt = now + validFor;
        var immutableTags = tags?.ToArray() ?? [];
        entries[key] = new CacheEntry(value, immutableTags, expiresAt);
        stored.Enqueue(new StoredResponse(key, immutableTags, validFor, expiresAt));
        return ValueTask.CompletedTask;
    }

    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref evictionCalls);
        if (FailOnEviction)
        {
            throw new InvalidOperationException("injected output-cache eviction failure");
        }
        foreach (var pair in entries)
        {
            if (pair.Value.Tags.Contains(tag, StringComparer.Ordinal))
            {
                entries.TryRemove(pair.Key, out _);
            }
        }

        return ValueTask.CompletedTask;
    }

    public void Reset()
    {
        entries.Clear();
        while (stored.TryDequeue(out _))
        {
        }
        Interlocked.Exchange(ref evictionCalls, 0);
        Interlocked.Exchange(ref getCalls, 0);
        Interlocked.Exchange(ref setCalls, 0);
        now = DateTimeOffset.UtcNow;
    }

    public void AdvanceBy(TimeSpan duration) => now += duration;

    private sealed record CacheEntry(byte[] Value, string[] Tags, DateTimeOffset ExpiresAt);
    public sealed record StoredResponse(string Key, string[] Tags, TimeSpan ValidFor, DateTimeOffset ExpiresAt);
}
