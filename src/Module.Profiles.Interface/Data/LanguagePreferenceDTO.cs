namespace VeloTime.Module.Profiles.Interface.Data;

/// <summary>A user's saved language preference; null means that no preference is saved.</summary>
public sealed record LanguagePreferenceDTO(string? Language);

/// <summary>Sets the authenticated user's language preference to a canonical supported value.</summary>
public sealed record UpdateLanguagePreferenceRequest(string? Language);

/// <summary>A complete display-preference snapshot. Unit values are explicit stable wire codes.</summary>
/// <remarks>Browser and UTC modes use a null time-zone ID; timeZone mode requires a valid IANA ID.</remarks>
/// <param name="TimeZoneId">An IANA identifier only when time mode is <c>timeZone</c>; otherwise null.</param>
public sealed record DisplayPreferenceDTO(string DistanceUnit, string SpeedUnit, string TimeMode, string? TimeZoneId);

/// <summary>Replaces all display preferences as one complete snapshot.</summary>
/// <remarks>Browser and UTC modes require a null time-zone ID; timeZone mode requires a valid IANA ID.</remarks>
/// <param name="TimeZoneId">An IANA identifier only when time mode is <c>timeZone</c>; otherwise null.</param>
public sealed record UpdateDisplayPreferenceRequest(string DistanceUnit, string SpeedUnit, string TimeMode, string? TimeZoneId);

/// <summary>A supported named time zone and its human-readable search/display label.</summary>
public sealed record TimeZoneOptionDTO(string Id, string DisplayName);
