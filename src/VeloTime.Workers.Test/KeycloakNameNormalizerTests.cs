using System.Text.RegularExpressions;
using VeloTime.Users.Migration.Services;

namespace VeloTime.Workers.Test;

public class KeycloakNameNormalizerTests
{
    // Keycloak's default person-name-prohibited-characters pattern.
    private static readonly Regex KeycloakPersonName =
        new("^[^<>&\"\\v$%!#?§;*~/\\\\|^=\\[\\]{}()\\u0000-\\u001F\\u007F]+$");

    [Theory]
    [InlineData("Ola", "Ola")]
    [InlineData("  Ola   Nordmann ", "Ola Nordmann")]
    [InlineData("Anne-Marie", "Anne-Marie")]
    [InlineData("O'Brien", "O'Brien")]
    [InlineData("Ola (Junior)", "Ola Junior")]
    [InlineData("Ola/Kari", "Ola Kari")]
    [InlineData("Tom & Jerry", "Tom Jerry")]
    [InlineData("Ola\tNord\u0000mann", "Ola Nord mann")]
    [InlineData("<script>", "script")]
    public void Normalize_RemovesProhibitedCharactersAndCollapsesWhitespace(string input, string expected)
    {
        var normalized = KeycloakNameNormalizer.Normalize(input);

        Assert.Equal(expected, normalized);
        Assert.Matches(KeycloakPersonName, normalized!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()")]
    [InlineData("?!#")]
    public void Normalize_ReturnsNullWhenNothingValidRemains(string? input) =>
        Assert.Null(KeycloakNameNormalizer.Normalize(input));

    [Fact]
    public void Normalize_ComposesUnicode()
    {
        var decomposed = "Bjo\u0308rn";

        Assert.Equal("Björn", KeycloakNameNormalizer.Normalize(decomposed));
    }

    [Fact]
    public void Normalize_TruncatesToKeycloakMaxLength()
    {
        var normalized = KeycloakNameNormalizer.Normalize(new string('a', 300));

        Assert.Equal(KeycloakNameNormalizer.MaxLength, normalized!.Length);
    }

    [Fact]
    public void Normalize_DoesNotSplitSurrogatePairWhenTruncating()
    {
        var input = new string('a', KeycloakNameNormalizer.MaxLength - 1) + "😀";

        var normalized = KeycloakNameNormalizer.Normalize(input);

        Assert.Equal(KeycloakNameNormalizer.MaxLength - 1, normalized!.Length);
        Assert.False(char.IsHighSurrogate(normalized[^1]));
    }
}
