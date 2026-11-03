using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Storage;

/// <summary>Reads and atomically replaces the singleton Hall of Fame settings document.</summary>
public sealed class HallOfFameSettingsStore(StatisticsDbContext storage)
{
    private static readonly JsonSerializerOptions SectionsJsonOptions = CreateJsonOptions();

    public async Task<HallOfFameSettingsDTO> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await storage.HallOfFameSettingsDocuments
            .AsNoTracking()
            .SingleAsync(s => s.Id == HallOfFameSettings.SingletonId, cancellationToken);

        return new HallOfFameSettingsDTO(
            settings.Version,
            DeserializeSections(settings.SectionsJson));
    }

    /// <summary>
    /// Locks the singleton settings row until the current transaction completes. The item
    /// deletion and settings replacement endpoints share this lock to avoid dangling JSON
    /// references during concurrent administrator writes.
    /// </summary>
    public async Task<HallOfFameSettingsDTO> GetForUpdateAsync(CancellationToken cancellationToken = default)
    {
        IQueryable<HallOfFameSettings> query;
        if (storage.Database.IsNpgsql())
        {
            query = storage.HallOfFameSettingsDocuments
                .FromSqlRaw("SELECT * FROM statistics.hall_of_fame_settings WHERE id = 1 FOR UPDATE")
                .AsNoTracking();
        }
        else
            query = storage.HallOfFameSettingsDocuments.AsNoTracking();

        var settings = await query.SingleAsync(cancellationToken);
        return new HallOfFameSettingsDTO(settings.Version, DeserializeSections(settings.SectionsJson));
    }

    /// <summary>
    /// Replaces all settings in one conditional UPDATE. A null result means that the expected
    /// version was stale; only one concurrent writer can update a given version.
    /// </summary>
    public async Task<HallOfFameSettingsDTO?> TryReplaceAsync(
        SaveHallOfFameSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Sections);

        var newVersion = checked(request.ExpectedVersion + 1);
        var serializedSections = JsonSerializer.Serialize(request.Sections, SectionsJsonOptions);

        var updated = await storage.HallOfFameSettingsDocuments
            .Where(s => s.Id == HallOfFameSettings.SingletonId && s.Version == request.ExpectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Version, newVersion)
                    .SetProperty(s => s.SectionsJson, serializedSections),
                cancellationToken);

        return updated == 0
            ? null
            : new HallOfFameSettingsDTO(newVersion, DeserializeSections(serializedSections));
    }

    private static IReadOnlyList<HallOfFameSectionSettingsDTO> DeserializeSections(string sectionsJson) =>
        JsonSerializer.Deserialize<List<HallOfFameSectionSettingsDTO>>(sectionsJson, SectionsJsonOptions)
        ?? throw new JsonException("Hall of Fame settings must be a JSON array.");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
