using System.Globalization;
using UnitsNet;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

/// <summary>Immutable boundary. Statistics API speeds are km/h; API distances are metres.</summary>
public sealed record EffectiveDisplaySettings(DisplayPreferenceDTO Preference, TimeZoneInfo? Zone, CultureInfo NumberCulture)
{
    public static EffectiveDisplaySettings Unresolved { get; } = new(new("m", "km/h", "browser", null), null, CultureInfo.InvariantCulture);
    public bool IsReady => Zone is not null;
    public string DistanceUnit => Preference.DistanceUnit;
    public string SpeedUnit => Preference.SpeedUnit;
    public string ZoneLabel => Zone?.Id ?? "";
    public static EffectiveDisplaySettings Create(DisplayPreferenceDTO value, string browserZone, string browserLocale)
    {
        if (value.TimeMode != "timeZone" && value.TimeZoneId is not null) throw new PresentationValidationException("DisplaySettingsInvalid");
        if (value.DistanceUnit is not ("m" or "ft") || value.SpeedUnit is not ("km/h" or "mph" or "m/s")) throw new PresentationValidationException("DisplaySettingsInvalid");
        var id = value.TimeMode switch { "utc" => "UTC", "browser" => browserZone, "timeZone" => value.TimeZoneId, _ => throw new PresentationValidationException("DisplaySettingsInvalid") };
        if (string.IsNullOrWhiteSpace(id)) throw new PresentationValidationException("DisplaySettingsInvalid");
        // Never use TimeZoneInfo.Local: it can be the server zone.
        return new(value, TimeZoneInfo.FindSystemTimeZoneById(id), CultureInfo.GetCultureInfo(browserLocale));
    }
    public double DistanceFromMetres(double metres) => DistanceUnit == "ft" ? Length.FromMeters(metres).Feet : Length.FromMeters(metres).Meters;
    public double DistanceToMetres(double value) => DistanceUnit == "ft" ? Length.FromFeet(value).Meters : Length.FromMeters(value).Meters;
    public double SpeedFromKilometresPerHour(double value)
    {
        var speed = Speed.FromKilometersPerHour(value);
        return SpeedUnit switch { "mph" => speed.MilesPerHour, "m/s" => speed.MetersPerSecond, _ => speed.KilometersPerHour };
    }
    public double SpeedToKilometresPerHour(double value) => (SpeedUnit switch { "mph" => Speed.FromMilesPerHour(value), "m/s" => Speed.FromMetersPerSecond(value), _ => Speed.FromKilometersPerHour(value) }).KilometersPerHour;
    public string Distance(double metres, string format = "G") => $"{DistanceFromMetres(metres).ToString(format, NumberCulture)} {DistanceUnit}";
    public string StatisticsSpeed(double kmh) => $"{SpeedFromKilometresPerHour(kmh).ToString("N3", NumberCulture)} {SpeedUnit}";
    public string SpeedDraft(double? kmh) => kmh.HasValue ? SpeedFromKilometresPerHour(kmh.Value).ToString("R", NumberCulture) : "";
    public bool TrySpeedDraft(string text, out double? kmh)
    {
        kmh = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text, NumberStyles.Float, NumberCulture, out var number) || !double.IsFinite(number) || number < 0) return false;
        var canonical = SpeedToKilometresPerHour(number);
        if (!double.IsFinite(canonical)) return false;
        kmh = canonical;
        return true;
    }
    public bool ValidDistance(double value, double minimumMetres = 0.001) => double.IsFinite(value) && double.IsFinite(DistanceToMetres(value)) && value >= DistanceFromMetres(minimumMetres);
    public DateTimeOffset Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone ?? throw new PresentationValidationException("DisplaySettingsLoading"));
    public DateTimeOffset Local(DateTime utc) => Local(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
    public string Timestamp(DateTimeOffset? instant) => instant.HasValue ? $"{Local(instant.Value).ToString("G", NumberCulture)} {Local(instant.Value):zzz} [{ZoneLabel}]" : "-";
    public string Timestamp(DateTime? utc) => Timestamp(utc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)) : null);
    public DateOnly CalendarDate(DateTimeOffset instant) => DateOnly.FromDateTime(Local(instant).DateTime);
    public TimeSpan[] Offsets(DateTime wall)
    {
        var zone = Zone ?? throw new PresentationValidationException("DisplaySettingsLoading");
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) throw new PresentationValidationException("LocalTimeGap");
        return zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).OrderDescending().ToArray() : [zone.GetUtcOffset(wall)];
    }
    public DateTimeOffset ToUtc(DateTime wall, TimeSpan? selectedOffset = null)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        var offsets = Offsets(wall);
        if (offsets.Length > 1 && (!selectedOffset.HasValue || !offsets.Contains(selectedOffset.Value))) throw new PresentationValidationException("LocalTimeRepeated");
        return new DateTimeOffset(wall, offsets.Length == 1 ? offsets[0] : selectedOffset!.Value).ToUniversalTime();
    }
    public UtcInstantRange DayRange(DateOnly day, TimeSpan? startOffset = null, TimeSpan? endOffset = null) => new(ToUtc(day.ToDateTime(TimeOnly.MinValue), startOffset), ToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), endOffset));
    public string RegistrationDescription(DateTimeOffset start, DateTimeOffset? end) => $"{Timestamp(start)} — {(end.HasValue ? Timestamp(end.Value.AddTicks(-1)) : "∞")}";
}

public sealed class PresentationValidationException(string resourceKey) : Exception(resourceKey)
{
    public string ResourceKey { get; } = resourceKey;
}

/// <summary>Untouched instants retain precision and repeated-hour offset. Wall edits clear the choice.</summary>
public sealed class ZonedInstantDraft
{
    public DateTime? Date { get; set; }
    public TimeSpan? Time { get; set; } = TimeSpan.Zero;
    public TimeSpan? Offset { get; set; }
    public bool NextDay { get; init; }
    private DateTimeOffset? _original;
    private DateTime? _originalWall;
    private TimeSpan? _originalOffset;
    public DateTime? Wall => Date.HasValue && Time.HasValue ? DateTime.SpecifyKind(Date.Value.Date.AddDays(NextDay ? 1 : 0).Add(Time.Value), DateTimeKind.Unspecified) : null;
    public void Changed() => Offset = Wall == _originalWall ? _originalOffset : null;
    public static ZonedInstantDraft FromInstant(DateTimeOffset instant, EffectiveDisplaySettings settings)
    {
        var local = settings.Local(instant);
        return new() { Date = local.Date, Time = local.TimeOfDay, Offset = local.Offset, _original = instant, _originalWall = local.DateTime, _originalOffset = local.Offset };
    }
    public DateTimeOffset Resolve(EffectiveDisplaySettings settings)
    {
        if (!Wall.HasValue) throw new PresentationValidationException("RequiredError");
        if (Wall == _originalWall && _original.HasValue && Offset == _originalOffset) return _original.Value.ToUniversalTime();
        return settings.ToUtc(Wall.Value, Offset);
    }
}

/// <summary>Captures editor units and exact stored SI value; untouched saves never round-trip conversions.</summary>
public sealed class DistanceEditorValue(double metres, EffectiveDisplaySettings settings)
{
    public EffectiveDisplaySettings Settings { get; } = settings;
    private readonly double _originalMetres = metres;
    private readonly double _originalDisplay = settings.DistanceFromMetres(metres);
    public double Value { get; set; } = settings.DistanceFromMetres(metres);
    public double Metres => Value == _originalDisplay ? _originalMetres :
        Value == Settings.DistanceFromMetres(0.001) ? 0.001 : Settings.DistanceToMetres(Value);
    public bool IsValid => Settings.ValidDistance(Value);
}
