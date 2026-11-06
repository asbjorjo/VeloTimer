using System.Text;

namespace VeloTime.Users.Migration.Services;

/// <summary>
/// Normalizes person names to satisfy Keycloak's default user-profile validators
/// (<c>person-name-prohibited-characters</c> and <c>length</c> max 255) for <c>firstName</c>/<c>lastName</c>.
/// </summary>
internal static class KeycloakNameNormalizer
{
    internal const int MaxLength = 255;

    private const string ProhibitedCharacters = "<>&\"$%!#?§;*~/\\|^=[]{}()";

    internal static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var character in name.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || ProhibitedCharacters.Contains(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(character);
        }

        if (builder.Length > MaxLength)
        {
            var length = char.IsHighSurrogate(builder[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            builder.Length = length;
        }

        var normalized = builder.ToString().TrimEnd();
        return normalized.Length == 0 ? null : normalized;
    }
}
