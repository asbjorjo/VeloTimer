using System.Globalization;
using VeloTime.Agent.Interface;
using VeloTime.Module.Timing.Interface;

namespace VeloTime.Module.Timing.Model;

/// <summary>
/// The one place that maps agent transponder types to <see cref="TransponderType"/>, validates IDs and printed codes
/// per type, and builds the matching <see cref="Transponder"/> subclass.
/// </summary>
public static class TransponderFactory
{
    /// <summary>Maps a contract value from <see cref="TransponderTypes"/> (case-insensitive) to a transponder type.</summary>
    public static bool TryParseType(string? value, out TransponderType type)
    {
        type = string.Equals(value?.Trim(), TransponderTypes.MylapsProChip, StringComparison.OrdinalIgnoreCase)
            ? TransponderType.MylapsProChip
            : TransponderType.Unknown;
        return type != TransponderType.Unknown;
    }

    /// <summary>Validates <paramref name="systemId"/> for <paramref name="type"/> and builds the transponder.</summary>
    public static bool TryCreate(TransponderType type, string? systemId, out Transponder? transponder, out string? error)
    {
        transponder = null;
        error = null;
        switch (type)
        {
            case TransponderType.MylapsProChip:
                if (!IsProChipId(systemId, out var id))
                {
                    error = $"'{systemId}' is not a valid ProChip ID.";
                    return false;
                }
                transponder = new MylapsProChipTransponder { SystemId = id.ToString(CultureInfo.InvariantCulture) };
                return true;
            default:
                error = $"Unsupported transponder type {type}.";
                return false;
        }
    }

    /// <summary>Converts a printed code (e.g. HT-24422) to the system ID for <paramref name="type"/>.</summary>
    public static bool TryParseCode(TransponderType type, string? code, out string systemId)
    {
        systemId = string.Empty;
        if (type != TransponderType.MylapsProChip || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var id = MylapsProChipCode.CodeToId(code.Trim().ToUpperInvariant());
            if (!IsProChipId(id.ToString(CultureInfo.InvariantCulture), out _)) return false;
            systemId = id.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsProChipId(string? value, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(value) || !value.All(char.IsAsciiDigit)
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
        try
        {
            // Round-trip through the printed code: rejects IDs at or below the ProChip offset.
            return MylapsProChipCode.CodeToId(MylapsProChipCode.IdToCode(id)) == id;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
