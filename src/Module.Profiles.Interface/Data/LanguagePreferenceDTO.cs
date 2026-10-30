namespace VeloTime.Module.Profiles.Interface.Data;

/// <summary>A user's saved language preference; null means that no preference is saved.</summary>
public sealed record LanguagePreferenceDTO(string? Language);

/// <summary>Sets the authenticated user's language preference to a canonical supported value.</summary>
public sealed record UpdateLanguagePreferenceRequest(string? Language);
