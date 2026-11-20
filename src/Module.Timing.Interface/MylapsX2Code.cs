namespace VeloTime.Module.Timing.Interface;

/// <summary>Converts between MyLaps X2 system IDs and printed transponder codes.</summary>
public static class MylapsX2Code
{
    private const int IdOffset = 0x6000000;
    private const int NumericOffset = 100000;
    private static readonly char[] Characters = ['C', 'F', 'G', 'H', 'K', 'L', 'N', 'P', 'R', 'S', 'T', 'V', 'W', 'X', 'Z'];

    /// <summary>Converts a numeric system ID to its printed code.</summary>
    /// <param name="id">The MyLaps X2 system ID.</param>
    /// <returns>The printed transponder code.</returns>
    public static string IdToCode(long id)
    {
        if (id <= IdOffset)
            throw new ArgumentOutOfRangeException(nameof(id), $"Must be a positive number greater than {IdOffset} - {id}");

        var n = id - IdOffset;
        var m = n / NumericOffset;
        var prefix = "";
        for (var i = 2; i >= 0; i--)
        {
            var c = Characters[m % 15];
            if (c != 'C' || i > 0)
            {
                prefix = c + prefix;
                m /= 15;
            }
        }
        return $"{prefix}-{n % NumericOffset:00000}";
    }

    /// <summary>Converts a printed transponder code to its numeric system ID.</summary>
    /// <param name="code">The MyLaps X2 code.</param>
    /// <returns>The numeric system ID.</returns>
    public static long CodeToId(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentOutOfRangeException(nameof(code), "Cannot be empty");
        if (code.IndexOf("-") > 0) code = code.ToUpper().Remove(code.IndexOf("-"), 1);
        if (code.Length == 7) code = "C" + code;
        if (code.Length != 8) throw new ArgumentException(nameof(code), "Must be 8 characters");
        if (!code.Substring(3, 5).All(char.IsDigit))
            throw new ArgumentOutOfRangeException(nameof(code), code, "Last 5 characters must be numbers.");
        if (code.Substring(0, 3).Except(Characters).Any())
            throw new ArgumentOutOfRangeException(nameof(code), code, "Contains illegal characters.");

        return NumericOffset * (Array.IndexOf(Characters, code[0]) * 225
            + Array.IndexOf(Characters, code[1]) * 15 + Array.IndexOf(Characters, code[2]))
            + int.Parse(code.Substring(3, 5)) + IdOffset;
    }
}
