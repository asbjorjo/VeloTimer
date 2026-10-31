using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Platform.Test;

public sealed class BootstrapWorkerTests
{
    [Fact]
    public async Task BootstrapWorker_SeedsModuleDataAndIsIdempotent()
    {
        await using var database = await BootstrapDatabase.CreateAsync();

        await RunWorkerAsync(database.Services);
        var firstCounts = await database.ReadCountsAsync();

        Assert.Equal(1, firstCounts.Installations);
        Assert.Equal(5, firstCounts.TimingPoints);
        Assert.Equal(1, firstCounts.Facilities);
        Assert.Equal(5, firstCounts.CourseSegments);
        Assert.Equal(7, firstCounts.StatisticsItems);
        Assert.Equal(12, firstCounts.StatisticsConfigs);

        await RunWorkerAsync(database.Services);
        var secondCounts = await database.ReadCountsAsync();

        Assert.Equal(firstCounts, secondCounts);
    }

    private static async Task RunWorkerAsync(IServiceProvider services)
    {
        var lifetime = new TestHostApplicationLifetime();
        var worker = new global::Worker(services, lifetime);
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        Assert.True(lifetime.StopRequested);
    }

    private sealed class BootstrapDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection timingConnection;
        private readonly SqliteConnection facilityConnection;
        private readonly SqliteConnection statisticsConnection;
        private readonly ServiceProvider services;

        private BootstrapDatabase(SqliteConnection timingConnection, SqliteConnection facilityConnection, SqliteConnection statisticsConnection, ServiceProvider services)
        {
            this.timingConnection = timingConnection;
            this.facilityConnection = facilityConnection;
            this.statisticsConnection = statisticsConnection;
            this.services = services;
        }

        public IServiceProvider Services => services;

        public static async Task<BootstrapDatabase> CreateAsync()
        {
            var timingConnection = await OpenConnectionAsync();
            var facilityConnection = await OpenConnectionAsync();
            var statisticsConnection = await OpenConnectionAsync();

            var timingOptions = new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(timingConnection).Options;
            var facilityOptions = new DbContextOptionsBuilder<FacilityDbContext>().UseSqlite(facilityConnection).Options;
            // Match the production column naming used by the public-profile partial index.
            // This bootstrap fixture still tests seeding only, not PostgreSQL query behavior.
            var statisticsOptions = new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseSqlite(statisticsConnection).UseSnakeCaseNamingConvention().Options;

            await using (var context = new TimingDbContext(timingOptions)) await context.Database.EnsureCreatedAsync();
            await using (var context = new FacilityDbContext(facilityOptions)) await context.Database.EnsureCreatedAsync();
            await using (var context = new StatisticsDbContext(statisticsOptions)) await context.Database.EnsureCreatedAsync();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDbContext<TimingDbContext>(options => options.UseSqlite(timingConnection));
            serviceCollection.AddDbContext<FacilityDbContext>(options => options.UseSqlite(facilityConnection));
            serviceCollection.AddDbContext<StatisticsDbContext>(options =>
                options.UseSqlite(statisticsConnection).UseSnakeCaseNamingConvention());

            return new BootstrapDatabase(timingConnection, facilityConnection, statisticsConnection, serviceCollection.BuildServiceProvider());
        }

        public async Task<SeedCounts> ReadCountsAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var timing = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var facilities = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
            var statistics = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            return new SeedCounts(
                await timing.Set<Installation>().CountAsync(),
                await timing.Set<TimingPoint>().CountAsync(),
                await facilities.Set<Facility>().CountAsync(),
                await facilities.Set<CourseSegment>().CountAsync(),
                await statistics.Set<StatisticsItem>().CountAsync(),
                await statistics.Set<SimpleStatisticsItemConfig>().CountAsync() + await statistics.Set<MultiStatisticsItemConfig>().CountAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await timingConnection.DisposeAsync();
            await facilityConnection.DisposeAsync();
            await statisticsConnection.DisposeAsync();
        }

        private static async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            return connection;
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => stopped.Token;
        public bool StopRequested { get; private set; }

        public void StopApplication()
        {
            StopRequested = true;
            stopping.Cancel();
            stopped.Cancel();
        }
    }

    private sealed record SeedCounts(int Installations, int TimingPoints, int Facilities, int CourseSegments, int StatisticsItems, int StatisticsConfigs);
}
