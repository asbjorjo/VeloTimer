using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Test;

public sealed class HallOfFameSettingsStoreTests
{
    [Fact]
    public async Task SettingsRoundTripRetainsSectionAndColumnOrderAndStartsAtVersionZero()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StatisticsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new StatisticsDbContext(options))
            await setup.Database.EnsureCreatedAsync();

        await using var context = new StatisticsDbContext(options);
        var store = new HallOfFameSettingsStore(context);
        var initial = await store.GetAsync();

        Assert.Equal(0, initial.Version);
        Assert.Empty(initial.Sections);

        var expectedSections = new[]
        {
            new HallOfFameSectionSettingsDTO(
                Guid.NewGuid(),
                HallOfFameRankingMode.MostPassings,
                IsEnabled: false,
                RowCount: 7,
                [HallOfFameColumn.DateOfBestResult, HallOfFameColumn.BestTime]),
            new HallOfFameSectionSettingsDTO(
                Guid.NewGuid(),
                HallOfFameRankingMode.BestTime,
                IsEnabled: true,
                RowCount: 10,
                [HallOfFameColumn.PassingCount, HallOfFameColumn.FacilityOfBestResult])
        };
        var saved = await store.TryReplaceAsync(new SaveHallOfFameSettingsRequest(0, expectedSections));

        Assert.NotNull(saved);
        Assert.Equal(1, saved.Version);
        Assert.Equal(expectedSections.Length, saved.Sections.Count);

        var reloaded = await store.GetAsync();
        Assert.Equal(saved.Version, reloaded.Version);
        Assert.Equal(expectedSections.Length, reloaded.Sections.Count);
        for (var index = 0; index < expectedSections.Length; index++)
        {
            Assert.Equal(expectedSections[index].StatisticsItemId, reloaded.Sections[index].StatisticsItemId);
            Assert.Equal(expectedSections[index].RankingMode, reloaded.Sections[index].RankingMode);
            Assert.Equal(expectedSections[index].IsEnabled, reloaded.Sections[index].IsEnabled);
            Assert.Equal(expectedSections[index].RowCount, reloaded.Sections[index].RowCount);
            Assert.Equal(expectedSections[index].Columns, reloaded.Sections[index].Columns);
        }
    }

    [Fact]
    public async Task StaleWholeDocumentReplacementDoesNotOverwriteTheWinningSettings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StatisticsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new StatisticsDbContext(options))
            await setup.Database.EnsureCreatedAsync();

        var winningSection = new HallOfFameSectionSettingsDTO(
            Guid.NewGuid(),
            HallOfFameRankingMode.BestTime,
            IsEnabled: true,
            RowCount: 4,
            []);
        var staleSection = new HallOfFameSectionSettingsDTO(
            Guid.NewGuid(),
            HallOfFameRankingMode.MostPassings,
            IsEnabled: true,
            RowCount: 8,
            []);

        await using (var firstContext = new StatisticsDbContext(options))
        {
            var winner = await new HallOfFameSettingsStore(firstContext)
                .TryReplaceAsync(new SaveHallOfFameSettingsRequest(0, [winningSection]));
            Assert.NotNull(winner);
        }

        await using (var secondContext = new StatisticsDbContext(options))
        {
            var stale = await new HallOfFameSettingsStore(secondContext)
                .TryReplaceAsync(new SaveHallOfFameSettingsRequest(0, [staleSection]));
            Assert.Null(stale);
        }

        await using var verificationContext = new StatisticsDbContext(options);
        var current = await new HallOfFameSettingsStore(verificationContext).GetAsync();
        Assert.Equal(1, current.Version);
        Assert.Single(current.Sections);
        Assert.Equal(winningSection.StatisticsItemId, current.Sections[0].StatisticsItemId);
        Assert.Equal(winningSection.RankingMode, current.Sections[0].RankingMode);
        Assert.Equal(winningSection.RowCount, current.Sections[0].RowCount);
    }
}
