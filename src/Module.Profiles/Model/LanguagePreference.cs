namespace VeloTime.Module.Profiles.Model;

public sealed class LanguagePreference
{
    public required string Issuer { get; init; }
    public required string Subject { get; init; }
    public required string Language { get; set; }
}
