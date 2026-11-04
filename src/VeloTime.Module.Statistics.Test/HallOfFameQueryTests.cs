using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Test;

public sealed class HallOfFameQueryTests
{
    private static HallOfFameSectionSettingsDTO Section() =>
        new(Guid.NewGuid(), HallOfFameRankingMode.BestTime, true, 10, []);

    [Fact]
    public void DefaultsAndBoundsAreExplicitAndBothModesMayCoexist()
    {
        Assert.Equal(10, HallOfFameQuery.DefaultRowCount);
        Assert.Equal(1, HallOfFameQuery.MinimumRowCount);
        Assert.Equal(100, HallOfFameQuery.MaximumRowCount);
        var section = Section();
        Assert.Empty(HallOfFameQuery.Validate(new(0, [])));
        Assert.Empty(HallOfFameQuery.Validate(new(long.MaxValue - 1,
            [section with { RowCount = 1 }, section with { RankingMode = HallOfFameRankingMode.MostPassings, RowCount = 100, IsEnabled = false }])));
        Assert.Empty(HallOfFameQuery.Validate(new(0,
            Enumerable.Range(0, 250).Select(_ => Section()).ToArray())));
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        var section = Section();
        yield return [new SaveHallOfFameSettingsRequest(-1, []), "expectedVersion"];
        yield return [new SaveHallOfFameSettingsRequest(0, null!), "sections"];
        yield return [new SaveHallOfFameSettingsRequest(0, [null!]), "sections[0]"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { StatisticsItemId = Guid.Empty }]), "sections[0].statisticsItemId"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { RankingMode = (HallOfFameRankingMode)99 }]), "sections[0].rankingMode"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { RowCount = 0 }]), "sections[0].rowCount"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { RowCount = 101 }]), "sections[0].rowCount"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { Columns = null! }]), "sections[0].columns"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { Columns = [(HallOfFameColumn)99] }]), "sections[0].columns[0]"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section with { Columns = [HallOfFameColumn.BestTime, HallOfFameColumn.BestTime] }]), "sections[0].columns[1]"];
        yield return [new SaveHallOfFameSettingsRequest(0, [section, section with { IsEnabled = false }]), "sections[1]"];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void ValidationRejectsMalformedDraftsWithJsonPropertyKeys(SaveHallOfFameSettingsRequest request, string key)
    {
        var errors = HallOfFameQuery.Validate(request);
        Assert.True(errors.ContainsKey(key), string.Join(", ", errors.Keys));
        Assert.NotEmpty(errors[key]);
    }
}
