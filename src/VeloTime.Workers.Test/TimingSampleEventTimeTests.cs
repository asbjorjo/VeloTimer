using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Workers.Test;

public sealed class TimingSampleEventTimeTests
{
    private static readonly DateTime ReassignedAt = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-10)] // Delayed historical sample, both endpoints before reassignment.
    [InlineData(0)] // The end instant is exactly at the half-open reassignment boundary.
    [InlineData(10)] // Sample spans the half-open reassignment boundary.
    public async Task DelayedMessageAfterReassignment_ResolvesEachEndpointAtEventTime(int endSeconds)
    {
        await using var fixture = await Fixture.CreateAsync();
        var oldStart = Guid.NewGuid();
        var oldEnd = Guid.NewGuid();
        var currentStart = Guid.NewGuid();
        var currentEnd = Guid.NewGuid();
        var message = new TimingSampleComplete(ReassignedAt.AddSeconds(-20), ReassignedAt.AddSeconds(endSeconds),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        fixture.Client.Resolve = (start, end, startAt, endAt, token) =>
        {
            Assert.Equal(message.TimingPointStart, start);
            Assert.Equal(message.TimingPointEnd, end);
            AssertInstant(message.TimeStart, startAt);
            AssertInstant(message.TimeEnd, endAt);
            Assert.Equal(cancellation.Token, token);
            // A current/no-time lookup would choose the new assignments for both endpoints.
            return Task.FromResult(new CoursePointDistance
            {
                CoursePointStartId = startAt < new DateTimeOffset(ReassignedAt) ? oldStart : currentStart,
                CoursePointEndId = endAt < new DateTimeOffset(ReassignedAt) ? oldEnd : currentEnd,
                Distance = 150,
                FacilityId = fixture.Distance.FacilityId
            });
        };

        await fixture.Handler.OnHandle(message, cancellation.Token);

        Assert.Equal(1, fixture.Client.Calls);
        var sample = await fixture.Storage.Set<Sample>().SingleAsync();
        Assert.Equal(oldStart, sample.CoursePointStartId);
        Assert.Equal(endSeconds < 0 ? oldEnd : currentEnd, sample.CoursePointEndId);
        Assert.Equal(fixture.Distance.FacilityId, sample.FacilityId);
        Assert.Equal(150, sample.Distance);
        Assert.Equal(message.TimeEnd - message.TimeStart, sample.Duration);
        Assert.Equal(150 / sample.Duration.TotalSeconds, sample.Speed);
        AssertPreservedTimes(message, sample.TimeStart, sample.TimeEnd);
        var published = Assert.IsType<SampleComplete>(Assert.Single(fixture.Bus.Published).Payload);
        Assert.Equal(message.TransponderId, sample.TransponderId);
        Assert.Equal(message.TransponderId, published.TransponderId);
        Assert.Equal(sample.CoursePointStartId, published.CoursePointStart);
        Assert.Equal(sample.CoursePointEndId, published.CoursePointEnd);
        Assert.Equal(sample.Distance, published.Distance);
        AssertPreservedTimes(message, published.TimeStart, published.TimeEnd);
        fixture.Storage.ChangeTracker.Clear();
        var persisted = await fixture.Storage.Set<Sample>().SingleAsync();
        Assert.Equal(message.TimeStart.Ticks, persisted.TimeStart.Ticks);
        Assert.Equal(message.TimeEnd.Ticks, persisted.TimeEnd.Ticks);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc, DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified, DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local, DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified, DateTimeKind.Local)]
    [InlineData(DateTimeKind.Local, DateTimeKind.Utc)]
    public async Task DateTimeKinds_SendOffsetZeroInstantsWithoutChangingSampleTimes(DateTimeKind startKind, DateTimeKind endKind)
    {
        await using var fixture = await Fixture.CreateAsync();
        var startUtc = ReassignedAt.AddSeconds(-20);
        var endUtc = ReassignedAt.AddSeconds(10);
        var message = new TimingSampleComplete(WithKind(startUtc, startKind), WithKind(endUtc, endKind),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        fixture.Client.Resolve = (_, _, startAt, endAt, _) =>
        {
            AssertInstant(startUtc, startAt);
            AssertInstant(endUtc, endAt);
            return Task.FromResult(fixture.Distance);
        };

        await fixture.Handler.OnHandle(message, CancellationToken.None);

        Assert.Equal(1, fixture.Client.Calls);
        var sample = await fixture.Storage.Set<Sample>().SingleAsync();
        AssertPreservedTimes(message, sample.TimeStart, sample.TimeEnd);
        var published = Assert.IsType<SampleComplete>(Assert.Single(fixture.Bus.Published).Payload);
        AssertPreservedTimes(message, published.TimeStart, published.TimeEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledLookup_PropagatesWithoutSavingOrPublishing(bool cancelled)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        var failure = new InvalidOperationException("Association lookup failed");
        fixture.Client.Resolve = (_, _, _, _, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            return cancelled
                ? Task.FromCanceled<CoursePointDistance>(token)
                : Task.FromException<CoursePointDistance>(failure);
        };
        var message = new TimingSampleComplete(ReassignedAt.AddSeconds(-20), ReassignedAt.AddSeconds(10),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        if (cancelled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Handler.OnHandle(message, cancellation.Token));
        else
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Handler.OnHandle(message, cancellation.Token)));

        Assert.Equal(1, fixture.Client.Calls);
        Assert.Empty(await fixture.Storage.Set<Sample>().ToListAsync());
        Assert.Empty(fixture.Bus.Published);
    }

    private static DateTime WithKind(DateTime utc, DateTimeKind kind) =>
        kind == DateTimeKind.Local ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, kind);

    private static void AssertInstant(DateTime expectedUtc, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(TimeSpan.Zero, actual.Value.Offset);
        Assert.Equal(expectedUtc.Ticks, actual.Value.UtcTicks);
    }

    private static void AssertPreservedTimes(TimingSampleComplete message, DateTime start, DateTime end)
    {
        Assert.Equal(message.TimeStart.Ticks, start.Ticks);
        Assert.Equal(message.TimeStart.Kind, start.Kind);
        Assert.Equal(message.TimeEnd.Ticks, end.Ticks);
        Assert.Equal(message.TimeEnd.Kind, end.Kind);
    }

    public class FacilitiesProxy : DispatchProxy
    {
        public Func<Guid, Guid, DateTimeOffset?, DateTimeOffset?, CancellationToken, Task<CoursePointDistance>> Resolve { get; set; } = null!;
        public int Calls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Assert.Equal(nameof(IFacilitiesClient.Distance2Async), targetMethod.Name);
            Assert.Equal(new[] { "start", "end", "startAt", "endAt", "cancellationToken" },
                targetMethod.GetParameters().Select(parameter => parameter.Name));
            Assert.NotNull(args);
            Assert.Equal(5, args.Length);
            Calls++;
            return Resolve(Assert.IsType<Guid>(args[0]), Assert.IsType<Guid>(args[1]),
                Assert.IsType<DateTimeOffset>(args[2]), Assert.IsType<DateTimeOffset>(args[3]),
                Assert.IsType<CancellationToken>(args[4]));
        }
    }

    private sealed class Fixture(SqliteConnection connection, StatisticsDbContext storage, ServiceProvider services) : IAsyncDisposable
    {
        private readonly IFacilitiesClient facilities = DispatchProxy.Create<IFacilitiesClient, FacilitiesProxy>();
        private readonly (SlimMessageBus.IMessageBus Bus, WorkerHandlerTests.RecordingMessageBus Recorder) bus =
            WorkerHandlerTests.RecordingMessageBus.Create();
        public StatisticsDbContext Storage => storage;
        public FacilitiesProxy Client => (FacilitiesProxy)(object)facilities;
        public WorkerHandlerTests.RecordingMessageBus Bus => bus.Recorder;
        public CoursePointDistance Distance { get; } = new()
        {
            CoursePointStartId = Guid.NewGuid(), CoursePointEndId = Guid.NewGuid(),
            Distance = 150, FacilityId = Guid.NewGuid()
        };
        public TimingSampleHandler Handler => new(storage, facilities, bus.Bus,
            new Metrics(services.GetRequiredService<IMeterFactory>()), NullLogger<TimingSampleHandler>.Instance);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var storage = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>().UseSqlite(connection).Options);
            await storage.Database.EnsureCreatedAsync();
            return new Fixture(connection, storage, new ServiceCollection().AddMetrics().BuildServiceProvider());
        }

        public async ValueTask DisposeAsync()
        {
            await storage.DisposeAsync();
            await connection.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
