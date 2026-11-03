using System.Globalization;
using System.Text.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Pages.Admin;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class StatisticsThresholdDraftTests
{
    [Theory]
    [InlineData("en-US", "km/h")]
    [InlineData("en-US", "mph")]
    [InlineData("en-US", "m/s")]
    [InlineData("nb-NO", "km/h")]
    [InlineData("nb-NO", "mph")]
    [InlineData("nb-NO", "m/s")]
    public void LocaleSpeedInversionAndUntouchedModePrecision(string locale, string unit)
    {
        var settings = EffectiveDisplaySettings.Create(new("m", unit, "utc", null), "UTC", locale);
        var min = TimeSpan.FromTicks(123456789);
        var max = TimeSpan.FromTicks(234567891);
        var draft = new StatisticsThresholdDraft(min, max, 250, settings);
        for (var i = 0; i < 20; i++)
        {
            Assert.True(draft.ChangeMode(true));
            Assert.Equal(settings.SpeedDraft(250 / max.TotalSeconds * 3.6), draft.LowerText);
            Assert.Equal(settings.SpeedDraft(250 / min.TotalSeconds * 3.6), draft.UpperText);
            draft.SetLower(draft.LowerText);
            draft.SetUpper(draft.UpperText);
            Assert.True(draft.ChangeMode(false));
            Assert.Equal(min, draft.Minimum);
            Assert.Equal(max, draft.Maximum);
        }
        Assert.True(draft.ChangeMode(true));
        draft.SetLower(settings.SpeedFromKilometresPerHour(36).ToString("R", settings.NumberCulture));
        draft.SetUpper(settings.SpeedFromKilometresPerHour(72).ToString("R", settings.NumberCulture));
        Assert.Equal(TimeSpan.FromSeconds(25), draft.Maximum);
        Assert.Equal(TimeSpan.FromSeconds(12.5), draft.Minimum);
        draft.ChangeDistance(500); // Item/distance changes keep durations, not displayed speeds.
        Assert.Equal(TimeSpan.FromSeconds(25), draft.Maximum);
        Assert.Equal(settings.SpeedDraft(72), draft.LowerText);
        draft.SetLower("");
        draft.SetUpper("");
        Assert.Null(draft.Minimum);
        Assert.Null(draft.Maximum);
        Assert.True(draft.IsValid);
    }

    [Theory]
    [InlineData("00:00:00.0000004", false, 0)]
    [InlineData("00:00:00.0000005", true, 10)]
    [InlineData("00:00:00.0000014", true, 10)]
    [InlineData("00:00:00.0000015", true, 20)]
    [InlineData("00:00:00", false, 0)]
    [InlineData("-00:00:01", false, 0)]
    [InlineData("24:00:00", false, 0)]
    [InlineData("999999999999.00:00:00", false, 0)]
    [InlineData("garbage", false, 0)]
    public void EditedDurationRoundsToPositiveMicrosecondsOrRetainsMalformedDraft(string input, bool valid, long ticks)
    {
        var settings = EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "UTC", "en-US");
        var draft = new StatisticsThresholdDraft(null, null, 250, settings);
        draft.SetLower(input);
        Assert.Equal(valid, draft.LowerValid);
        if (valid) Assert.Equal(TimeSpan.FromTicks(ticks), draft.Minimum);
        else
        {
            Assert.False(draft.ChangeMode(true));
            Assert.Equal(input, draft.LowerText);
            Assert.Null(draft.Minimum);
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e309")]
    [InlineData("0")]
    [InlineData("-1")]
    public void InvalidSpeedCannotDisappearOnModeSwitch(string text)
    {
        var draft = new StatisticsThresholdDraft(null, null, 250,
            EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "UTC", "en-US"));
        Assert.True(draft.ChangeMode(true));
        draft.SetLower(text);
        Assert.False(draft.IsValid);
        Assert.False(draft.ChangeMode(false));
        Assert.Equal(text, draft.LowerText);
    }

    [Fact]
    public void DecimalCommaInheritanceClampingEqualAndDisjointRanges()
    {
        var draft = new StatisticsThresholdDraft(null, null, 250,
            EffectiveDisplaySettings.Create(new("m", "km/h", "utc", null), "UTC", "nb-NO"));
        draft.SetLower("00:00:01,0000015");
        Assert.Equal(TimeSpan.FromTicks(10000020), draft.Minimum);
        Assert.Equal("00:00:01,0000020", draft.DurationText(draft.Minimum));
        var min = TimeSpan.FromSeconds(10);
        var max = TimeSpan.FromSeconds(20);
        Assert.Equal((min, max), StatisticsThresholdDraft.Effective(min, max, null, null));
        Assert.Equal((min, max), StatisticsThresholdDraft.Effective(min, max, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        Assert.True(StatisticsThresholdDraft.ValidRange(max, max));
        Assert.False(StatisticsThresholdDraft.ValidRange(max, min));
    }

    [Fact]
    public void SharedSaveContractsRetainCanonicalDurationFieldsAndExplicitClearing()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var min = TimeSpan.FromTicks(123456780);
        var max = TimeSpan.FromTicks(234567890);
        var item = new SaveStatisticsItemRequest("Lap", "", 250, min, max);
        var config = new SaveStatisticsConfigurationRequest(Guid.NewGuid(), StatisticsConfigurationType.Multi,
            null, null, Guid.NewGuid(), 2, min, max);
        Assert.Equal(item, JsonSerializer.Deserialize<SaveStatisticsItemRequest>(JsonSerializer.Serialize(item, json), json));
        Assert.Equal(config, JsonSerializer.Deserialize<SaveStatisticsConfigurationRequest>(JsonSerializer.Serialize(config, json), json));
        using var cleared = JsonDocument.Parse(JsonSerializer.Serialize(config with { MinDuration = null, MaxDuration = null }, json));
        Assert.Equal(JsonValueKind.Null, cleared.RootElement.GetProperty("minDuration").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.RootElement.GetProperty("maxDuration").ValueKind);
        Assert.Null(JsonSerializer.Deserialize<SaveStatisticsItemRequest>("""{"name":"legacy","description":"","distance":250}""", json)!.MinDuration);
    }
}
