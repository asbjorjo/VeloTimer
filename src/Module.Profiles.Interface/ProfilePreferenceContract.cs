namespace VeloTime.Module.Profiles.Interface;

public static class ProfilePreferenceRoutes
{
    public const string CurrentLanguage = "/api/profiles/me/preferences";
    public const string UpdateLanguage = "/api/profiles/me/preferences/language";
    public const string CurrentDisplayPreference = "/api/profiles/me/preferences/display";
    public const string UpdateDisplayPreference = "/api/profiles/me/preferences/display";
    public const string SupportedTimeZoneCatalogue = "/api/profiles/me/preferences/time-zones";
}

public static class ProfileLanguageCodes
{
    public const string English = "en";
    public const string NorwegianBokmal = "nb-NO";

    public static bool IsSupportedPreference(string? language) =>
        language is English or NorwegianBokmal;
}

public static class ProfileDisplayPreferenceCodes
{
    public const string Metres = "m";
    public const string DefaultDistanceUnit = Metres;
    public const string Feet = "ft";

    public const string KilometresPerHour = "km/h";
    public const string DefaultSpeedUnit = KilometresPerHour;
    public const string MilesPerHour = "mph";
    public const string MetresPerSecond = "m/s";

    public const string BrowserTime = "browser";
    public const string DefaultTimeMode = BrowserTime;
    public const string UtcTime = "utc";
    public const string NamedTimeZone = "timeZone";
}

public static class ProfilePreferenceErrorCodes
{
    public const string InvalidLanguage = "profiles.invalid_language";
    public const string InvalidIdentity = "profiles.invalid_identity";
    public const string InvalidDistanceUnit = "profiles.invalid_distance_unit";
    public const string InvalidSpeedUnit = "profiles.invalid_speed_unit";
    public const string InvalidTimeMode = "profiles.invalid_time_mode";
    public const string InvalidTimeZone = "profiles.invalid_time_zone";
    public const string TimeZoneRequired = "profiles.time_zone_required";
    public const string TimeZoneNotAllowed = "profiles.time_zone_not_allowed";
}
