using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Statistics.Api;
using VeloTime.Module.Statistics.Interface.Messages;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsIdentityContractTests
{
    private static readonly DateTime Start = new(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Rider = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Point1 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Point2 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void OriginalConstructorsAndDeconstructorsRemainAvailable()
    {
        var sample = new SampleComplete(Rider, Start, Start.AddSeconds(10), Point1, Point2, 250);
        var entry = new EntryCreated(Rider, Start, Start.AddSeconds(10), Point1, Point2);
        Assert.Null(sample.SampleId);
        Assert.Null(entry.EntryId);
        var (rider, start, end, from, to, distance) = sample;
        Assert.Equal((Rider, Start, Start.AddSeconds(10), Point1, Point2, 250d), (rider, start, end, from, to, distance));
        var (entryRider, entryStart, entryEnd, item, config) = entry;
        Assert.Equal((Rider, Start, Start.AddSeconds(10), Point1, Point2), (entryRider, entryStart, entryEnd, item, config));
        Assert.NotNull(typeof(SampleComplete).GetConstructor([typeof(Guid), typeof(DateTime), typeof(DateTime), typeof(Guid), typeof(Guid), typeof(double)]));
        Assert.NotNull(typeof(EntryCreated).GetConstructor([typeof(Guid), typeof(DateTime), typeof(DateTime), typeof(Guid), typeof(Guid)]));
        Assert.True(typeof(SampleComplete).GetConstructor([typeof(Guid), typeof(DateTime), typeof(DateTime), typeof(Guid), typeof(Guid), typeof(double), typeof(Guid?)])!.GetParameters()[^1].HasDefaultValue);
        Assert.True(typeof(EntryCreated).GetConstructor([typeof(Guid), typeof(DateTime), typeof(DateTime), typeof(Guid), typeof(Guid), typeof(Guid?)])!.GetParameters()[^1].HasDefaultValue);
        Assert.Equal("SampleComplete", typeof(SampleComplete).Name);
        Assert.Equal("EntryCreated", typeof(EntryCreated).Name);
    }

    [Fact]
    public void DefaultSystemTextJsonReadsLegacyAndNewMessages()
    {
        var legacySample = new LegacySampleComplete(Rider, Start, Start.AddSeconds(10), Point1, Point2, 250);
        var legacyEntry = new LegacyEntryCreated(Rider, Start, Start.AddSeconds(10), Point1, Point2);
        var sample = new SampleComplete(Rider, Start, Start.AddSeconds(10), Point1, Point2, 250, Id);
        var entry = new EntryCreated(Rider, Start, Start.AddSeconds(10), Point1, Point2, Id);
        Assert.Equal(sample with { SampleId = null }, JsonSerializer.Deserialize<SampleComplete>(JsonSerializer.Serialize(legacySample)));
        Assert.Equal(entry with { EntryId = null }, JsonSerializer.Deserialize<EntryCreated>(JsonSerializer.Serialize(legacyEntry)));
        Assert.Null(JsonSerializer.Deserialize<SampleComplete>(JsonSerializer.Serialize(sample with { SampleId = null }))!.SampleId);
        Assert.Null(JsonSerializer.Deserialize<EntryCreated>(JsonSerializer.Serialize(entry with { EntryId = null }))!.EntryId);
        Assert.Equal(sample, JsonSerializer.Deserialize<SampleComplete>(JsonSerializer.Serialize(sample)));
        Assert.Equal(entry, JsonSerializer.Deserialize<EntryCreated>(JsonSerializer.Serialize(entry)));
        Assert.Equal(legacySample, JsonSerializer.Deserialize<LegacySampleComplete>(JsonSerializer.Serialize(sample)));
        Assert.Equal(legacyEntry, JsonSerializer.Deserialize<LegacyEntryCreated>(JsonSerializer.Serialize(entry)));
    }

    [Fact]
    public void RegisteredSlimMessageBusSerializerReadsLegacyAndRoundTripsNewMessages()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/";
        builder.AddStatisticsValidityThresholdsMessaging(); // Same unconfigured AddJsonSerializer as the processor.
        using var host = builder.Build(); // No broker or database connection.
        var serializer = host.Services.GetRequiredService<JsonMessageSerializer>();
        var legacySample = new LegacySampleComplete(Rider, Start, Start.AddSeconds(10), Point1, Point2, 250);
        var legacyEntry = new LegacyEntryCreated(Rider, Start, Start.AddSeconds(10), Point1, Point2);
        var sample = new SampleComplete(Rider, Start, Start.AddSeconds(10), Point1, Point2, 250, Id);
        var entry = new EntryCreated(Rider, Start, Start.AddSeconds(10), Point1, Point2, Id);
        var headers = new Dictionary<string, object>();

        byte[] Write<T>(T message) => serializer.Serialize(typeof(T), headers, message!, null!);
        T Read<T>(byte[] bytes) => (T)serializer.Deserialize(typeof(T), headers, bytes, null!);

        var oldSampleJson = Write(legacySample);
        var oldEntryJson = Write(legacyEntry);
        using var oldSampleDocument = JsonDocument.Parse(oldSampleJson);
        using var oldEntryDocument = JsonDocument.Parse(oldEntryJson);
        Assert.DoesNotContain(oldSampleDocument.RootElement.EnumerateObject(), p => p.Name.Equals("SampleId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(oldEntryDocument.RootElement.EnumerateObject(), p => p.Name.Equals("EntryId", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(sample with { SampleId = null }, Read<SampleComplete>(oldSampleJson));
        Assert.Equal(entry with { EntryId = null }, Read<EntryCreated>(oldEntryJson));
        Assert.Null(Read<SampleComplete>(Write(sample with { SampleId = null })).SampleId);
        Assert.Null(Read<EntryCreated>(Write(entry with { EntryId = null })).EntryId);
        var sampleJson = Write(sample);
        var entryJson = Write(entry);
        Assert.Equal(sample, Read<SampleComplete>(sampleJson));
        Assert.Equal(entry, Read<EntryCreated>(entryJson));
        Assert.Equal(legacySample, Read<LegacySampleComplete>(sampleJson));
        Assert.Equal(legacyEntry, Read<LegacyEntryCreated>(entryJson));
        Assert.Equal(DateTimeKind.Utc, Read<SampleComplete>(sampleJson).TimeStart.Kind);
        Assert.Equal(250, Read<SampleComplete>(sampleJson).Distance);

        // Inspect the actual wire bytes, not just record equality.
        using var sampleDocument = JsonDocument.Parse(sampleJson);
        using var entryDocument = JsonDocument.Parse(entryJson);
        Assert.Contains(sampleDocument.RootElement.EnumerateObject(), p => p.Name.Equals("SampleId", StringComparison.OrdinalIgnoreCase) && p.Value.GetGuid() == Id);
        Assert.Contains(entryDocument.RootElement.EnumerateObject(), p => p.Name.Equals("EntryId", StringComparison.OrdinalIgnoreCase) && p.Value.GetGuid() == Id);
    }

    public record LegacySampleComplete(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid CoursePointStart, Guid CoursePointEnd, double Distance);
    public record LegacyEntryCreated(Guid TransponderId, DateTime TimeStart, DateTime TimeEnd, Guid StatisticsItemId, Guid ConfigItemId);
}
