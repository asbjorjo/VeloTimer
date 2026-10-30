using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Profiles.Interface;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Service;

public sealed class LanguagePreferenceService(ProfilesDbContext storage) : ILanguagePreferenceService
{
    public async Task<LanguagePreferenceDTO> GetAsync(
        string issuer, string subject, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(issuer, subject);

        var language = await storage.LanguagePreferences.AsNoTracking()
            .Where(preference => preference.Issuer == issuer && preference.Subject == subject)
            .Select(preference => preference.Language)
            .SingleOrDefaultAsync(cancellationToken);

        return new LanguagePreferenceDTO(language);
    }

    public async Task<LanguagePreferenceDTO> UpdateAsync(
        string issuer, string subject, string? language, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(issuer, subject);
        if (!ProfileLanguageCodes.IsSupportedPreference(language))
        {
            throw new ArgumentException("Language must be exactly 'en' or 'nb-NO'.", nameof(language));
        }

        // PostgreSQL arbitrates concurrent first writes using the composite primary key.
        // Do not replace this with read-then-insert, or retry a unique violation in an aborted transaction.
        // All interpolated values become parameters; no identity or language is embedded in SQL.
        await storage.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO profiles.language_preference (issuer, subject, language)
            VALUES ({issuer}, {subject}, {language})
            ON CONFLICT (issuer, subject) DO UPDATE SET language = EXCLUDED.language
            """, cancellationToken);

        // Return this write's value, not a second read that could observe a concurrent later write.
        return new LanguagePreferenceDTO(language);
    }

    private static void ValidateIdentity(string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
    }
}
