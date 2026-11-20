using System.Globalization;
using System.Text.Json;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class PublicStatisticsQueryTests
{
    private static readonly Guid FacilityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ItemId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid ConfigurationId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid EntryId = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public void Query_DeclaresFixedPageSizeAndAnonymousRoutes()
    {
        Assert.Equal(25, PublicStatisticsQuery.PageSize);
        Assert.Equal(26, PublicStatisticsQuery.FetchSize);
        Assert.Equal("/api/statistics/public/discovery", PublicStatisticsQuery.DiscoveryPath);
        Assert.Equal("/api/statistics/public/results", PublicStatisticsQuery.ResultsPath);
    }

    [Fact]
    public void Validate_AcceptsInclusiveEqualBoundsAndNormalizesNameWithoutChangingLiteralCharacters()
    {
        var request = ValidRequest() with
        {
            Name = "  O'Brien 100%_ + [A&B]  ",
            MinDuration = TimeSpan.FromMilliseconds(1234),
            MaxDuration = TimeSpan.FromMilliseconds(1234),
            MinSpeedKmh = 0,
            MaxSpeedKmh = 0
        };

        var normalized = PublicStatisticsQuery.Normalize(request);

        Assert.Empty(PublicStatisticsQuery.Validate(normalized));
        Assert.Equal("O'Brien 100%_ + [A&B]", normalized.Name);
        Assert.Equal(request.MinDuration, normalized.MaxDuration);
        Assert.Equal(0, normalized.MinSpeedKmh);
        Assert.Equal(0, normalized.MaxSpeedKmh);
    }

    [Fact]
    public void Validate_RejectsMissingSelectionsAndInvalidOrReversedBounds()
    {
        var request = ValidRequest() with
        {
            FacilityId = Guid.Empty,
            StatisticsItemId = Guid.Empty,
            StatisticsItemConfigId = Guid.Empty,
            MinDuration = TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond),
            MaxDuration = TimeSpan.FromTicks(1),
            MinSpeedKmh = double.NaN,
            MaxSpeedKmh = double.PositiveInfinity
        };

        var errors = PublicStatisticsQuery.Validate(request);

        Assert.Contains("facilityId", errors.Keys);
        Assert.Contains("itemId", errors.Keys);
        Assert.Contains("configurationId", errors.Keys);
        Assert.Contains("minTime", errors.Keys);
        Assert.Contains("maxTime", errors.Keys);
        Assert.Contains("minSpeed", errors.Keys);
        Assert.Contains("maxSpeed", errors.Keys);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.1)]
    public void Validate_RejectsNegativeOrSubMillisecondDurationBounds(double milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);

        var errors = PublicStatisticsQuery.Validate(ValidRequest() with { MinDuration = duration });

        Assert.Contains("minTime", errors.Keys);
    }

    [Fact]
    public void Validate_RejectsReversedDurationAndSpeedBounds()
    {
        var errors = PublicStatisticsQuery.Validate(ValidRequest() with
        {
            MinDuration = TimeSpan.FromSeconds(2),
            MaxDuration = TimeSpan.FromSeconds(1),
            MinSpeedKmh = 20,
            MaxSpeedKmh = 10
        });

        Assert.Contains("maxTime", errors.Keys);
        Assert.Contains("maxSpeed", errors.Keys);
    }

    [Fact]
    public void Validate_RequiresCursorAndDirectionTogetherAndRejectsUndefinedDirection()
    {
        var cursor = new PublicStatisticsCursorDTO(UtcTimestamp(), EntryId);

        Assert.Contains("cursor", PublicStatisticsQuery.Validate(ValidRequest() with
        {
            Cursor = cursor
        }).Keys);
        Assert.Contains("cursor", PublicStatisticsQuery.Validate(ValidRequest() with
        {
            Direction = PublicStatisticsPageDirection.Next
        }).Keys);
        Assert.Contains("direction", PublicStatisticsQuery.Validate(ValidRequest() with
        {
            Cursor = cursor,
            Direction = (PublicStatisticsPageDirection)99
        }).Keys);
    }

    [Fact]
    public void TryParse_UsesInvariantValuesAndPreservesTrimmedLiteralName()
    {
        var query = Query();
        query["name"] = "  50%_A+B & C  ";
        query["minTime"] = "1.02:03:04.005";
        query["maxTime"] = "2.00:00:00.000";
        query["minSpeed"] = "1.25e1";
        query["maxSpeed"] = "37.5";

        Assert.True(PublicStatisticsQuery.TryParse(query, out var request, out var errors), FormatErrors(errors));
        Assert.Empty(errors);
        Assert.NotNull(request);
        Assert.Equal("50%_A+B & C", request.Name);
        Assert.Equal(TimeSpan.FromDays(1) + new TimeSpan(2, 3, 4) + TimeSpan.FromMilliseconds(5), request.MinDuration);
        Assert.Equal(TimeSpan.FromDays(2), request.MaxDuration);
        Assert.Equal(12.5, request.MinSpeedKmh);
        Assert.Equal(37.5, request.MaxSpeedKmh);
    }

    [Theory]
    [InlineData("minTime", "")]
    [InlineData("minTime", "00:00:01.0001")]
    [InlineData("maxTime", "-00:00:01.000")]
    [InlineData("minSpeed", "NaN")]
    [InlineData("maxSpeed", "Infinity")]
    [InlineData("minSpeed", "-0.1")]
    public void TryParse_RejectsPresentButEmptyOrMalformedBounds(string key, string value)
    {
        var query = Query();
        query[key] = value;

        Assert.False(PublicStatisticsQuery.TryParse(query, out var request, out var errors));
        Assert.Null(request);
        Assert.Contains(key, errors.Keys);
    }

    [Fact]
    public void TryParse_RejectsUnknownKeysAndMalformedSelectionIds()
    {
        var query = Query();
        query["pageSize"] = "25";
        query["itemId"] = "not-a-uuid";

        Assert.False(PublicStatisticsQuery.TryParse(query, out var request, out var errors));
        Assert.Null(request);
        Assert.Contains("pageSize", errors.Keys);
        Assert.Contains("itemId", errors.Keys);
    }

    [Fact]
    public void DurationFormatting_UsesInvariantMillisecondsAndSupportsDayPrefix()
    {
        var duration = TimeSpan.FromDays(3) + new TimeSpan(4, 5, 6) + TimeSpan.FromMilliseconds(7);

        Assert.Equal("3.04:05:06.007", PublicStatisticsQuery.FormatDuration(duration));
        Assert.True(PublicStatisticsQuery.TryParseDuration("3.04:05:06.007", out var parsed));
        Assert.Equal(duration, parsed);
        Assert.Equal("00:00:00.000", PublicStatisticsQuery.FormatDuration(TimeSpan.Zero));
        Assert.False(PublicStatisticsQuery.TryParseDuration("00:00:00.0001", out _));
        Assert.False(PublicStatisticsQuery.TryParseDuration("-00:00:01.000", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PublicStatisticsQuery.FormatDuration(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void CursorFormattingAndParsing_RetainUtcMicrosecondTimestampAndUuid()
    {
        var cursor = new PublicStatisticsCursorDTO(UtcTimestamp(), EntryId);

        var text = PublicStatisticsQuery.FormatCursor(cursor);

        Assert.Equal("2026-10-31T08:47:54.1234560+00:00|" + EntryId.ToString("D"), text);
        Assert.True(PublicStatisticsQuery.TryParseCursor(text, out var parsed));
        Assert.Equal(cursor, parsed);
    }

    [Theory]
    [InlineData("2026-10-31T08:47:54.1234567+00:00|40000000-0000-0000-0000-000000000004")]
    [InlineData("2026-10-31T08:47:54.1234560+01:00|40000000-0000-0000-0000-000000000004")]
    [InlineData("2026-10-31T08:47:54.1234560+00:00|00000000-0000-0000-0000-000000000000")]
    [InlineData("2026-10-31T08:47:54.1234560+00:00|not-a-uuid")]
    [InlineData("2026-10-31T08:47:54.1234560+00:00")]
    public void TryParseCursor_RejectsInvalidPrecisionOffsetOrIdentifier(string value)
    {
        Assert.False(PublicStatisticsQuery.TryParseCursor(value, out var cursor));
        Assert.Null(cursor);
    }

    [Fact]
    public async Task ToQuery_AndFormEncoding_RoundTripLiteralSearchAndCompositeCursor()
    {
        var request = ValidRequest() with
        {
            Name = "  50%_A+B & C  ",
            MinDuration = TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(5),
            MinSpeedKmh = 12.5,
            Cursor = new PublicStatisticsCursorDTO(UtcTimestamp(), EntryId),
            Direction = PublicStatisticsPageDirection.Previous
        };

        var values = PublicStatisticsQuery.ToQuery(request);
        using var content = new FormUrlEncodedContent(values);
        var encoded = await content.ReadAsStringAsync();
        var decoded = encoded.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => DecodeFormComponent(parts[0]), parts => (string?)DecodeFormComponent(parts[1]));

        Assert.Contains("%26+C", encoded);
        Assert.Contains("%25", encoded);
        Assert.Contains("%2B", encoded);
        Assert.Contains("%26", encoded);
        Assert.Contains("%7C", encoded);
        Assert.Contains("%2B00%3A00", encoded);
        Assert.True(PublicStatisticsQuery.TryParse(decoded, out var roundTripped, out var errors), FormatErrors(errors));
        Assert.Equal(PublicStatisticsQuery.Normalize(request), roundTripped);
        Assert.Equal("previous", values["direction"]);
        Assert.Equal(12.5.ToString("R", CultureInfo.InvariantCulture), values["minSpeed"]);
    }

    [Fact]
    public void PublicResultJson_RoundTripsFullDurationAndUtcTimestampPrecisionWithoutPrivateIdentifiers()
    {
        var result = new PublicStatisticsResultDTO(
            EntryId,
            "Public rider",
            ItemId,
            ConfigurationId,
            FacilityId,
            TimeSpan.FromTicks(12_345_678),
            37.123456789,
            UtcTimestamp());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(result, options);
        var roundTripped = JsonSerializer.Deserialize<PublicStatisticsResultDTO>(json, options);
        var cursor = new PublicStatisticsCursorDTO(UtcTimestamp(), EntryId);
        var cursorJson = JsonSerializer.Serialize(cursor, options);
        var roundTrippedCursor = JsonSerializer.Deserialize<PublicStatisticsCursorDTO>(cursorJson, options);

        Assert.Equal(result, roundTripped);
        Assert.Equal(cursor, roundTrippedCursor);
        Assert.Contains("\"duration\":\"00:00:01.2345678\"", json);
        Assert.Contains("\"timeEnd\":\"2026-10-31T08:47:54.123456+00:00\"", json);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transponderId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profileId", json, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> Query() => new()
    {
        ["facilityId"] = FacilityId.ToString("D"),
        ["itemId"] = ItemId.ToString("D"),
        ["configurationId"] = ConfigurationId.ToString("D")
    };

    private static GetPublicStatisticsResultsRequest ValidRequest() =>
        new(FacilityId, ItemId, ConfigurationId);

    private static DateTimeOffset UtcTimestamp() =>
        new DateTimeOffset(2026, 10, 31, 8, 47, 54, TimeSpan.Zero).AddTicks(1_234_560);

    private static string DecodeFormComponent(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));

    private static string FormatErrors(Dictionary<string, string[]> errors) =>
        string.Join(Environment.NewLine, errors.Select(error => $"{error.Key}: {string.Join(", ", error.Value)}"));
}
