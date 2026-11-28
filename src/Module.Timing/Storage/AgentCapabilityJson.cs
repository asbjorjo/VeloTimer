using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Storage;

/// <summary>Maps <see cref="Installation.AgentCapabilities"/> to one JSON column (<c>jsonb</c> on PostgreSQL).</summary>
internal static class AgentCapabilityJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static string Serialize(List<AgentCapabilityEntry> value) => JsonSerializer.Serialize(value, Options);

    private static List<AgentCapabilityEntry> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<AgentCapabilityEntry>>(json, Options) ?? [];

    public static readonly ValueConverter<List<AgentCapabilityEntry>, string> Converter =
        new(value => Serialize(value), json => Deserialize(json));

    public static readonly ValueComparer<List<AgentCapabilityEntry>> Comparer =
        new((left, right) => Serialize(left!) == Serialize(right!),
            value => Serialize(value).GetHashCode(),
            value => Deserialize(Serialize(value)));
}

/// <summary>Maps a list of strings to one JSON column (<c>jsonb</c> on PostgreSQL).</summary>
internal static class StringListJson
{
    private static string Serialize(List<string> value) => JsonSerializer.Serialize(value);

    private static List<string> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    public static readonly ValueConverter<List<string>, string> Converter = new(value => Serialize(value), json => Deserialize(json));

    public static readonly ValueComparer<List<string>> Comparer =
        new((left, right) => left!.SequenceEqual(right!), value => Serialize(value).GetHashCode(), value => value.ToList());
}
