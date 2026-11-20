using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Service;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class DateCultureMigrationTests
{
    [PostgresFact]
    public async Task UpgradeAndRollback_PreserveExistingPreferences_AndNewNullableColumnIsReapplicable()
    {
        await using var database = new PostgresFixture { ApplyMigrations = false };
        await database.InitializeAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        const string previous = "20261031115140_display_preference";
        await migrator.MigrateAsync(previous);
        await context.Database.ExecuteSqlRawAsync(
            """INSERT INTO profiles.display_preference (issuer, subject, distance_unit, speed_unit, time_mode, time_zone_id) VALUES ('upgrade', 'rider', 'ft', 'mph', 'timeZone', 'Europe/Oslo')""");
        await context.Database.MigrateAsync();
        var service = new DisplayPreferenceService(context);
        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "timeZone", "Europe/Oslo"), await service.GetAsync("upgrade", "rider"));
        Assert.False(context.Database.HasPendingModelChanges());
        await service.SetAsync("upgrade", "rider", new("ft", "mph", "timeZone", "Europe/Oslo", "nb-NO"));
        await migrator.MigrateAsync(previous);
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "timeZone", "Europe/Oslo"), await service.GetAsync("upgrade", "rider"));
        await service.SetAsync("upgrade", "rider", new("ft", "mph", "timeZone", "Europe/Oslo", "browser"));
        await using var fresh = database.CreateContext();
        Assert.Equal("browser", (await new DisplayPreferenceService(fresh).GetAsync("upgrade", "rider")).DateTimeCulture);
    }
}
