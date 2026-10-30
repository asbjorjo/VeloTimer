namespace VeloTime.Module.Profiles.Interface;

public static class ProfilePreferenceRoutes
{
    public const string CurrentLanguage = "/api/profiles/me/preferences";
    public const string UpdateLanguage = "/api/profiles/me/preferences/language";
}

public static class ProfileLanguageCodes
{
    public const string English = "en";
    public const string NorwegianBokmal = "nb-NO";

    public static bool IsSupportedPreference(string? language) =>
        language is English or NorwegianBokmal;
}

public static class ProfilePreferenceErrorCodes
{
    public const string InvalidLanguage = "profiles.invalid_language";
    public const string InvalidIdentity = "profiles.invalid_identity";
}
