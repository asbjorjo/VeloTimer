using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
using Generated = VeloTime.Module.Profiles.Client;
using VeloTime.Module.Profiles.Interface;

namespace VeloTime.WebUI.Mud.Services;

/// <summary>Uses the delegated Profiles client for server-interactive preference requests.</summary>
public sealed class DisplayPreferenceService(
    VeloTime.Module.Profiles.Client.IProfilesClient profiles,
    WebUiCache cache) : IDisplayPreferenceService
{
    public async Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await cache.GetPreferenceAsync(
            "display",
            async (Generated.IProfilesClient factoryClient, CancellationToken token) =>
                Map(await factoryClient.GetCurrentDisplayPreferenceAsync(token)),
            cancellationToken);
        return snapshot with { };
    }

    public async Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var preferenceTag = await cache.PreferenceTagAsync("display", cancellationToken);
        var response = await cache.WriteAndInvalidateAsync(() => profiles.UpdateDisplayPreferenceAsync(new Generated.UpdateDisplayPreferenceRequest
        {
            DistanceUnit = request.DistanceUnit,
            SpeedUnit = request.SpeedUnit,
            TimeMode = request.TimeMode,
            TimeZoneId = request.TimeZoneId
        }, cancellationToken), preferenceTag, "saving display preferences");
        return Map(response);
    }

    public async Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default)
    {
        var representation = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var snapshot = await cache.GetAuthenticatedReferenceAsync(
            "time-zone-catalogue",
            $"culture:{representation}",
            async (Generated.IProfilesClient factoryClient, CancellationToken token) =>
            {
                var zones = await factoryClient.GetSupportedTimeZonesAsync(token);
                if (zones is null || zones.Count == 0)
                    throw new InvalidDataException("The time-zone catalogue is missing.");
                return zones.Select(zone =>
                {
                    if (zone is null || string.IsNullOrWhiteSpace(zone.Id) || string.IsNullOrWhiteSpace(zone.DisplayName))
                        throw new InvalidDataException("The time-zone catalogue contains an invalid option.");
                    ValidateZone(zone.Id);
                    return new TimeZoneOptionDTO(zone.Id, zone.DisplayName);
                }).ToArray();
            },
            cancellationToken);
        return snapshot.ToArray();
    }

    private static DisplayPreferenceDTO Map(Generated.DisplayPreferenceDTO preference)
    {
        if (preference is null ||
            preference.DistanceUnit is not ("m" or "ft") ||
            preference.SpeedUnit is not ("km/h" or "mph" or "m/s") ||
            preference.TimeMode is not ("browser" or "utc" or "timeZone") ||
            (preference.TimeMode != "timeZone" && preference.TimeZoneId is not null))
            throw new InvalidDataException("The persisted display preference is invalid.");
        if (preference.TimeMode == "timeZone")
            ValidateZone(preference.TimeZoneId);
        return new(preference.DistanceUnit, preference.SpeedUnit, preference.TimeMode, preference.TimeZoneId);
    }

    private static void ValidateZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _))
            throw new InvalidDataException("The persisted time zone must be a supported IANA identifier.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidDataException("The persisted time zone is invalid.", exception);
        }
    }
}

public interface ILanguagePreferenceService
{
    Task<LanguagePreferenceDTO> GetAsync(CancellationToken cancellationToken = default);
    Task<LanguagePreferenceDTO> SaveAsync(string? language, CancellationToken cancellationToken = default);
}

/// <summary>Shared server-side owner for the persisted language preference used by BFF and localization.</summary>
public sealed class LanguagePreferenceService(
    VeloTime.Module.Profiles.Client.IProfilesClient profiles,
    WebUiCache cache) : ILanguagePreferenceService
{
    public async Task<LanguagePreferenceDTO> GetAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await cache.GetPreferenceAsync(
            "language",
            async (Generated.IProfilesClient factoryClient, CancellationToken token) =>
            {
                return Map(await factoryClient.GetCurrentLanguagePreferenceAsync(token));
            },
            cancellationToken);
        return snapshot with { };
    }

    public async Task<LanguagePreferenceDTO> SaveAsync(string? language, CancellationToken cancellationToken = default)
    {
        var preferenceTag = await cache.PreferenceTagAsync("language", cancellationToken);
        var response = await cache.WriteAndInvalidateAsync(() => profiles.UpdateLanguagePreferenceAsync(
            new Generated.UpdateLanguagePreferenceRequest { Language = language }, cancellationToken),
            preferenceTag, "saving language preferences");
        return Map(response);
    }

    private static LanguagePreferenceDTO Map(Generated.LanguagePreferenceDTO response)
    {
        if (response is null ||
            (response.Language is not null && !ProfileLanguageCodes.IsSupportedPreference(response.Language)))
            throw new InvalidDataException("The persisted language preference is invalid.");
        return new LanguagePreferenceDTO(response.Language);
    }
}
