using System.Globalization;

namespace VeloTime.WebUI.Mud.Services;

// NSwag exposes nullable TimeSpan wire values as strings. Never parse using the server's locale,
// round untouched values, or replace malformed upstream values with an unset bound.
internal static class StatisticsDurationTransport
{
    internal static string? Format(TimeSpan? value) => value?.ToString("c", CultureInfo.InvariantCulture);

    internal static TimeSpan? Parse(string? value)
    {
        if (value is null) return null;
        if (!TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var duration) ||
            duration <= TimeSpan.Zero || duration.Ticks % 10 != 0)
            throw new InvalidDataException("The statistics duration bound is invalid.");
        return duration;
    }
}
