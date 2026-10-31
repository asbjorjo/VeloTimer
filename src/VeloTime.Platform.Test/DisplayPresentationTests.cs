using System.Globalization;
using VeloTime.WebUI.Mud.Client.Services;
namespace VeloTime.Platform.Test;
public sealed class DisplayPresentationTests
{
    private static EffectiveDisplaySettings Settings(string distance = "ft", string speed = "mph", string zone = "Europe/Oslo", string locale = "en-US") =>
        EffectiveDisplaySettings.Create(new(distance, speed, "timeZone", zone), "Pacific/Auckland", locale);
    [Fact]
    public void IntendedSourceUnits_AndUntouchedDistances_ArePreserved()
    {
        var settings = Settings();
        Assert.Equal(3.280839895013123, settings.DistanceFromMetres(1), 12);
        Assert.Equal(22.369362920544, settings.SpeedFromKilometresPerHour(36), 10);
        foreach (var metres in new[] { 0.001, 1.234567890123, 250.0, 10000.0 })
        {
            Assert.Equal(metres, settings.DistanceToMetres(settings.DistanceFromMetres(metres)), 10);
            var draft = new DistanceEditorValue(metres, settings);
            Assert.Equal(metres, draft.Metres);
            Assert.True(draft.IsValid);
        }
        var edited = new DistanceEditorValue(250, settings) { Value = settings.DistanceFromMetres(0.001) };
        Assert.True(edited.IsValid);
        Assert.Equal(0.001, edited.Metres);
        Assert.False(settings.ValidDistance(settings.DistanceFromMetres(0.000999)));
        Assert.False(settings.ValidDistance(double.NaN));
        Assert.False(settings.ValidDistance(double.PositiveInfinity));
    }
    [Theory]
    [InlineData("km/h", 36)] [InlineData("mph", 22.369362920544)] [InlineData("m/s", 10)]
    public void StatisticsSpeedRoundTrips_FromKilometresPerHour(string unit, double display)
    {
        var settings = Settings(speed: unit);
        Assert.Equal(display, settings.SpeedFromKilometresPerHour(36), 10);
        Assert.Equal(36, settings.SpeedToKilometresPerHour(settings.SpeedFromKilometresPerHour(36)), 10);
        Assert.True(settings.TrySpeedDraft(settings.SpeedDraft(36), out var canonical));
        Assert.Equal(36, canonical!.Value, 10);
        Assert.True(settings.TrySpeedDraft("", out canonical)); Assert.Null(canonical);
        Assert.True(settings.TrySpeedDraft("0", out canonical)); Assert.Equal(0, canonical);
    }
    [Theory]
    [InlineData("NaN")] [InlineData("Infinity")] [InlineData("1e309")] [InlineData("-1")] [InlineData("1,000")] [InlineData("12 mph")]
    public void InvalidDrafts_DoNotBecomeCanonicalValues(string text)
    {
        Assert.False(Settings().TrySpeedDraft(text, out var canonical)); Assert.Null(canonical);
    }
    [Fact]
    public void NumberLocale_IsIndependentOfServerCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var settings = Settings(speed: "m/s", locale: "nb-NO");
            Assert.True(settings.TrySpeedDraft("10,5", out var canonical));
            Assert.Equal(37.8, canonical!.Value, 10);
            Assert.Contains("10,500 m/s", settings.StatisticsSpeed(37.8));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
    [Theory]
    [InlineData(2026, 3, 29, 23)] [InlineData(2026, 10, 25, 25)] [InlineData(2026, 2, 1, 24)]
    public void CalendarRanges_AreUtcHalfOpenAndFollowDst(int year, int month, int day, int hours)
    {
        var settings = Settings(); var date = new DateOnly(year, month, day); var range = settings.DayRange(date);
        Assert.Equal(TimeSpan.Zero, range.StartInclusive.Offset); Assert.Equal(TimeSpan.Zero, range.EndExclusive.Offset);
        Assert.Equal(hours, (range.EndExclusive - range.StartInclusive).TotalHours);
        Assert.Equal(date, settings.CalendarDate(range.StartInclusive));
        Assert.Equal(date, settings.CalendarDate(range.EndExclusive.AddTicks(-1)));
        Assert.Equal(date.AddDays(1), settings.CalendarDate(range.EndExclusive));
    }
    [Fact]
    public void GapRejected_RepeatedHourRequiresAnExplicitValidOffset()
    {
        var settings = Settings();
        Assert.Equal("LocalTimeGap", Assert.Throws<PresentationValidationException>(() => settings.ToUtc(new(2026, 3, 29, 2, 30, 0))).ResourceKey);
        var wall = new DateTime(2026, 10, 25, 2, 30, 0);
        Assert.Equal(new[] { TimeSpan.FromHours(2), TimeSpan.FromHours(1) }, settings.Offsets(wall));
        Assert.Equal("LocalTimeRepeated", Assert.Throws<PresentationValidationException>(() => settings.ToUtc(wall)).ResourceKey);
        Assert.Throws<PresentationValidationException>(() => settings.ToUtc(wall, TimeSpan.Zero));
        var early = settings.ToUtc(wall, TimeSpan.FromHours(2)); var late = settings.ToUtc(wall, TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromHours(1), late - early);
        Assert.Equal(wall, settings.Local(early).DateTime); Assert.Equal(wall, settings.Local(late).DateTime);
    }
    [Fact]
    public void UntouchedRepeatedInstant_PreservesTicks_WallEditsClearOffsetChoice()
    {
        var settings = Settings();
        var instant = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero).AddTicks(1234567);
        var draft = ZonedInstantDraft.FromInstant(instant, settings);
        Assert.Equal(instant, draft.Resolve(settings)); Assert.Equal(instant.Ticks, draft.Resolve(settings).Ticks);
        draft.Time += TimeSpan.FromMinutes(1); draft.Changed(); Assert.Null(draft.Offset);
        Assert.Throws<PresentationValidationException>(() => draft.Resolve(settings));
        draft.Offset = TimeSpan.FromHours(1); Assert.Equal(instant.AddMinutes(1), draft.Resolve(settings));
    }
    [Fact]
    public void NonexistentMidnightIsRejected_AndRepeatedMidnightNeedsExplicitBoundaryOffset()
    {
        var gap = Settings(zone: "America/Sao_Paulo");
        Assert.Equal("LocalTimeGap", Assert.Throws<PresentationValidationException>(() => gap.DayRange(new(2018, 11, 4))).ResourceKey);
        var repeated = Settings(zone: "America/Havana");
        var day = new DateOnly(2026, 11, 1);
        Assert.Equal("LocalTimeRepeated", Assert.Throws<PresentationValidationException>(() => repeated.DayRange(day)).ResourceKey);
        var offsets = repeated.Offsets(day.ToDateTime(TimeOnly.MinValue)); Assert.Equal(2, offsets.Length);
        var first = repeated.DayRange(day, offsets[0]); var second = repeated.DayRange(day, offsets[1]);
        Assert.Equal(TimeSpan.FromHours(1), second.StartInclusive - first.StartInclusive);
        Assert.Equal(first.EndExclusive, second.EndExclusive);
        Assert.Equal(TimeSpan.FromHours(25), first.EndExclusive - first.StartInclusive);
    }

    [Fact]
    public void BrowserAndUtcModes_UseBrowserIdentity_NotServerZone()
    {
        var instant = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture);
        var browser = EffectiveDisplaySettings.Create(new("m", "km/h", "browser", null), "Pacific/Auckland", "en-NZ");
        var utc = EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "Pacific/Auckland", "en-NZ");
        Assert.Equal(TimeSpan.FromHours(13), browser.Local(instant).Offset); Assert.Equal(TimeSpan.Zero, utc.Local(instant).Offset);
        Assert.False(EffectiveDisplaySettings.Unresolved.IsReady);
        Assert.Throws<PresentationValidationException>(() => EffectiveDisplaySettings.Unresolved.Local(instant));
        Assert.Equal("-", browser.Timestamp((DateTimeOffset?)null));
    }
}
