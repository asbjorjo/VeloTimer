using System.Globalization;
using System.Text.RegularExpressions;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Client.Pages.Admin;

/// <summary>Presentation-only draft. Never round-trip untouched canonical durations through speed.</summary>
public sealed class StatisticsThresholdDraft
{
    public StatisticsThresholdDraft(TimeSpan? minimum, TimeSpan? maximum, double distance, EffectiveDisplaySettings settings)
    {
        Minimum = minimum;
        Maximum = maximum;
        Distance = distance;
        Settings = settings;
        RefreshText();
    }

    public EffectiveDisplaySettings Settings { get; }
    public TimeSpan? Minimum { get; private set; }
    public TimeSpan? Maximum { get; private set; }
    public double Distance { get; private set; }
    public bool SpeedMode { get; private set; }
    public string LowerText { get; private set; } = "";
    public string UpperText { get; private set; } = "";
    public bool LowerValid { get; private set; } = true;
    public bool UpperValid { get; private set; } = true;
    public bool IsValid => LowerValid && UpperValid && ValidRange(Minimum, Maximum);

    public void SetLower(string text) => SetText(text, true);
    public void SetUpper(string text) => SetText(text, false);

    private void SetText(string text, bool lower)
    {
        if (text == (lower ? LowerText : UpperText)) return;
        var valid = TryDuration(text, out var duration);
        if (lower) { LowerText = text; LowerValid = valid; }
        else { UpperText = text; UpperValid = valid; }
        if (!valid) return; // Keep malformed drafts visible, never turn them into null.
        if (lower != SpeedMode) Minimum = duration;
        else Maximum = duration;
    }

    public bool ChangeMode(bool speed)
    {
        if (!LowerValid || !UpperValid) return false;
        SpeedMode = speed;
        RefreshText();
        return true;
    }

    public void ChangeDistance(double distance)
    {
        if (Distance == distance) return;
        Distance = distance;
        // Valid canonical values remain durations when the selected item/distance changes.
        if (SpeedMode)
        {
            if (LowerValid) LowerText = SpeedText(Maximum);
            if (UpperValid) UpperText = SpeedText(Minimum);
        }
    }

    private void RefreshText()
    {
        LowerText = SpeedMode ? SpeedText(Maximum) : DurationText(Minimum);
        UpperText = SpeedMode ? SpeedText(Minimum) : DurationText(Maximum);
    }

    public string DurationText(TimeSpan? value)
    {
        if (!value.HasValue) return "";
        return value.Value.ToString(value.Value.Days == 0 ? @"hh\:mm\:ss\.fffffff" : @"d\.hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture)
            .ReplaceLastDecimal(Settings.NumberCulture.NumberFormat.NumberDecimalSeparator);
    }

    public string SpeedText(TimeSpan? value) => value.HasValue && value.Value > TimeSpan.Zero && double.IsFinite(Distance) && Distance > 0
        ? Settings.SpeedDraft(Distance / value.Value.TotalSeconds * 3.6) : "";

    private bool TryDuration(string text, out TimeSpan? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            decimal seconds;
            if (SpeedMode)
            {
                if (!Settings.TrySpeedDraft(text, out var kmh) || !kmh.HasValue || kmh.Value <= 0 || !double.IsFinite(Distance) || Distance <= 0) return false;
                var result = Distance / (kmh.Value / 3.6);
                if (!double.IsFinite(result) || result <= 0) return false;
                seconds = (decimal)result;
            }
            else
            {
                // Same day.hh:mm:ss shape as the results duration editor; decimal follows browser numbers.
                var separator = Regex.Escape(Settings.NumberCulture.NumberFormat.NumberDecimalSeparator);
                var match = Regex.Match(text, $@"^(?:(?<d>[0-9]+)\.)?(?<h>[0-9]{{2}}):(?<m>[0-9]{{2}}):(?<s>[0-9]{{2}})(?:{separator}(?<f>[0-9]{{1,7}}))?$");
                if (!match.Success) return false;
                var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
                var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
                var secs = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
                if (hours > 23 || minutes > 59 || secs > 59) return false;
                var days = match.Groups["d"].Success ? decimal.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture) : 0;
                seconds = days * 86400 + hours * 3600 + minutes * 60 + secs;
                if (match.Groups["f"].Success) seconds += decimal.Parse("0." + match.Groups["f"].Value, CultureInfo.InvariantCulture);
            }
            // PostgreSQL intervals store microseconds. Only edited inputs are rounded.
            var ticks = checked((long)(decimal.Round(seconds * 1_000_000, 0, MidpointRounding.AwayFromZero) * 10));
            if (ticks <= 0) return false;
            value = TimeSpan.FromTicks(ticks);
            return true;
        }
        catch (OverflowException) { return false; }
        catch (FormatException) { return false; }
    }

    public static (TimeSpan? Minimum, TimeSpan? Maximum) Effective(TimeSpan? globalMin, TimeSpan? globalMax, TimeSpan? localMin, TimeSpan? localMax) =>
        (globalMin is null ? localMin : localMin is null ? globalMin : globalMin > localMin ? globalMin : localMin,
         globalMax is null ? localMax : localMax is null ? globalMax : globalMax < localMax ? globalMax : localMax);

    public static bool ValidRange(TimeSpan? minimum, TimeSpan? maximum) =>
        (!minimum.HasValue || minimum > TimeSpan.Zero) && (!maximum.HasValue || maximum > TimeSpan.Zero) &&
        (!minimum.HasValue || !maximum.HasValue || minimum <= maximum);
}

internal static class ThresholdDurationFormatting
{
    internal static string ReplaceLastDecimal(this string text, string separator)
    {
        var index = text.LastIndexOf('.');
        return index < 0 ? text : text[..index] + separator + text[(index + 1)..];
    }
}
