using VeloTime.Module.Profiles.Interface.Data;

namespace VeloTime.Module.Profiles.Service;

/// <summary>Persists preferences for an identity obtained from a validated authentication token.</summary>
/// <remarks>
/// The caller must supply the validated token issuer and subject, never request-body identity values.
/// Values are matched exactly, without trimming or case folding.
/// </remarks>
public interface ILanguagePreferenceService
{
    /// <summary>Returns a null language when no preference exists, without creating any data.</summary>
    Task<LanguagePreferenceDTO> GetAsync(
        string issuer, string subject, CancellationToken cancellationToken = default);

    /// <summary>Atomically sets a canonical language; null and unsupported values are rejected.</summary>
    /// <exception cref="ArgumentException">Identity is blank or language is not exactly en or nb-NO.</exception>
    Task<LanguagePreferenceDTO> UpdateAsync(
        string issuer, string subject, string? language, CancellationToken cancellationToken = default);
}
