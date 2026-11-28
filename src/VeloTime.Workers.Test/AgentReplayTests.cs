using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Model;
using VeloTime.Agent.Replay;
using VeloTime.Agent.Service;
using VeloTime.Agent.Storage;

namespace VeloTime.Workers.Test;

public sealed class AgentReplayTests : IAsyncDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly ServiceProvider provider;
    private readonly WorkerHandlerTests.RecordingMessageBus recorder;
    private readonly FakeBacklog backlog = new();
    private readonly ReplayOptions options = new() { ChunkSize = 2, ChunkPause = TimeSpan.Zero, ProgressInterval = TimeSpan.Zero };

    public AgentReplayTests()
    {
        connection.Open();
        var (bus, recording) = WorkerHandlerTests.RecordingMessageBus.Create();
        recorder = recording;
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddLogging();
        services.AddDbContextFactory<AgentDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton(bus);
        services.AddSingleton<VeloTime.Agent.Metrics>();
        services.AddScoped<MessagingService>();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<AgentStoreReplayCapabilityProvider>();
        services.AddSingleton<IOutboxBacklog>(backlog);
        services.AddSingleton<PassingReplayService>();
        provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await connection.DisposeAsync();
    }

    private PassingReplayService Replays => provider.GetRequiredService<PassingReplayService>();

    private async Task StorePassingsAsync(params (int Minutes, string Loop)[] passings)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MessagingService>().SendEventsAsync(passings.Select(p => new Passing
        {
            Id = VeloTime.Agent.Identity.PassingId.Create("Test", p.Loop, p.Minutes.ToString()),
            Time = T0.AddMinutes(p.Minutes),
            TransponderType = TransponderTypes.MylapsProChip,
            TransponderId = "106187718",
            LoopId = p.Loop
        }).ToList(), CancellationToken.None);
        recorder.Published.Clear();
    }

    private static ReplayPassingsCommandV1 Command(DateTime from, DateTime to, string mode = PassingReplayModes.AgentStore,
        IReadOnlyList<string>? loops = null, DateTime? expires = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "agent", mode, from, to, loops, expires ?? DateTime.UtcNow.AddMinutes(15));

    private List<PassingObservedV1> ReplayedPassings(Guid requestId) => recorder.Published
        .Where(m => m.Payload is PassingObservedV1 && Equals(m.Headers?[AgentControl.ReplayRequestIdHeader], requestId.ToString()))
        .Select(m => (PassingObservedV1)m.Payload!).ToList();

    private List<PassingReplayStatusV1> Statuses(Guid requestId) =>
        recorder.Published.Select(m => m.Payload).OfType<PassingReplayStatusV1>().Where(s => s.RequestId == requestId).ToList();

    [Fact]
    public async Task AgentStoreReplay_RepublishesEveryStoredPassingInRangeInChunksAndReportsProgress()
    {
        await StorePassingsAsync((0, "loop-1"), (1, "loop-2"), (2, "loop-1"), (3, "loop-1"), (4, "loop-1"), (10, "loop-1"));
        var command = Command(T0, T0.AddMinutes(5));

        var accepted = Assert.IsType<ReplayRequest>(await Replays.AcceptAsync(command, CancellationToken.None));
        Assert.True(Replays.IsBusy);
        await Replays.RunAsync(accepted, CancellationToken.None);

        // Every passing in [From, To), including ones Timing already has, in time order; the one at +10 min is outside.
        Assert.Equal(Enumerable.Range(0, 5).Select(m => T0.AddMinutes(m)), ReplayedPassings(command.RequestId).Select(p => p.Time));
        var states = Statuses(command.RequestId);
        Assert.Equal(PassingReplayStates.Accepted, states[0].State);
        Assert.Equal(PassingReplayStates.Running, states[1].State);
        Assert.Equal(PassingReplayStates.Completed, states[^1].State);
        Assert.Equal((5, 5, 0), (states[^1].Read, states[^1].Published, states[^1].NewInAgentStore));
        Assert.False(Replays.IsBusy);
        // Chunks of two: the outbox backlog is checked before each chunk.
        Assert.True(backlog.Checks >= 3);

        await using var scope = provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AgentDbContext>().Set<ReplayRequest>().SingleAsync();
        Assert.Equal((PassingReplayStates.Completed, 5), (stored.State, stored.Published));
        // Replays do not touch the store: still six passings.
        Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<AgentDbContext>().Set<Passing>().CountAsync());
    }

    [Fact]
    public async Task AgentStoreReplay_LoopFilterLimitsTheReplay()
    {
        await StorePassingsAsync((0, "loop-1"), (1, "loop-2"), (2, "loop-3"));
        var command = Command(T0, T0.AddMinutes(5), loops: ["loop-2", "loop-3"]);

        await Replays.RunAsync((await Replays.AcceptAsync(command, CancellationToken.None))!, CancellationToken.None);

        Assert.Equal(["loop-2", "loop-3"], ReplayedPassings(command.RequestId).Select(p => p.LoopId));
    }

    [Fact]
    public async Task RedeliveredCommand_ReportsItsEarlierResultInsteadOfRunningAgain()
    {
        await StorePassingsAsync((0, "loop-1"));
        var command = Command(T0, T0.AddMinutes(5));
        await Replays.RunAsync((await Replays.AcceptAsync(command, CancellationToken.None))!, CancellationToken.None);
        recorder.Published.Clear();

        Assert.Null(await Replays.AcceptAsync(command, CancellationToken.None));

        Assert.Empty(ReplayedPassings(command.RequestId));
        var status = Assert.Single(Statuses(command.RequestId));
        Assert.Equal((PassingReplayStates.Completed, 1), (status.State, status.Published));
    }

    public static TheoryData<string, Func<ReplayPassingsCommandV1>> Rejections => new()
    {
        { PassingReplayRejectionReasons.Expired, () => Command(T0, T0.AddMinutes(5), expires: DateTime.UtcNow.AddSeconds(-1)) },
        { PassingReplayRejectionReasons.UnsupportedMode, () => Command(T0, T0.AddMinutes(5), mode: PassingReplayModes.Source) },
        { PassingReplayRejectionReasons.InvalidRange, () => Command(T0, T0) },
        { PassingReplayRejectionReasons.RangeTooLong, () => Command(T0, T0.AddDays(31)) },
        // The oldest stored passing is at T0, so a range that ends at or before it has nothing to replay.
        { PassingReplayRejectionReasons.OutOfRetention, () => Command(T0.AddDays(-1), T0) },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task Agent_RejectsRequestsItsCurrentCapabilitiesDoNotAllow(string reason, Func<ReplayPassingsCommandV1> create)
    {
        await StorePassingsAsync((0, "loop-1"));
        var command = create();

        Assert.Null(await Replays.AcceptAsync(command, CancellationToken.None));

        var status = Assert.Single(Statuses(command.RequestId));
        Assert.Equal((PassingReplayStates.Rejected, reason), (status.State, status.Reason));
        Assert.Empty(ReplayedPassings(command.RequestId));
        Assert.False(Replays.IsBusy);
    }

    [Fact]
    public async Task Agent_RunsOneReplayAtATime()
    {
        await StorePassingsAsync((0, "loop-1"));
        var first = await Replays.AcceptAsync(Command(T0, T0.AddMinutes(5)), CancellationToken.None);
        var second = Command(T0, T0.AddMinutes(5));

        Assert.Null(await Replays.AcceptAsync(second, CancellationToken.None));
        Assert.Equal(PassingReplayRejectionReasons.Busy, Assert.Single(Statuses(second.RequestId)).Reason);

        await Replays.RunAsync(first!, CancellationToken.None);
        Assert.NotNull(await Replays.AcceptAsync(Command(T0, T0.AddMinutes(5)), CancellationToken.None));
    }

    [Fact]
    public async Task InterruptedReplay_IsReservedAndRunsAgainFromTheStart()
    {
        await StorePassingsAsync((0, "loop-1"), (1, "loop-1"));
        var command = Command(T0, T0.AddMinutes(5));
        var accepted = (await Replays.AcceptAsync(command, CancellationToken.None))!;
        using var cancelled = new CancellationTokenSource();
        backlog.OnCheck = () => cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Replays.RunAsync(accepted, cancelled.Token));
        backlog.OnCheck = null;

        // A new process: the stored request is still running, so it is resumed and blocks new requests until done.
        var restarted = ActivatorUtilities.CreateInstance<PassingReplayService>(provider);
        var interrupted = Assert.Single(await restarted.ReserveInterruptedAsync(CancellationToken.None));
        Assert.Equal(command.RequestId, interrupted.RequestId);
        Assert.True(restarted.IsBusy);
        recorder.Published.Clear();
        await restarted.RunAsync(interrupted, CancellationToken.None);

        Assert.Equal(2, ReplayedPassings(command.RequestId).Count);
        Assert.Equal(PassingReplayStates.Completed, Statuses(command.RequestId)[^1].State);
        Assert.False(restarted.IsBusy);
    }

    [Fact]
    public async Task Replay_WaitsForTheOutboxToDrainBeforeTheNextChunk()
    {
        await StorePassingsAsync((0, "loop-1"));
        backlog.Pending.Enqueue(500);
        backlog.Pending.Enqueue(300);

        await Replays.RunAsync((await Replays.AcceptAsync(Command(T0, T0.AddMinutes(5)), CancellationToken.None))!, CancellationToken.None);

        Assert.Empty(backlog.Pending);
        Assert.True(backlog.Checks >= 3);
    }

    [Fact]
    public async Task AgentStoreCapability_StartsAtTheOldestStoredPassing()
    {
        var capability = provider.GetRequiredService<AgentStoreReplayCapabilityProvider>();
        var empty = await capability.DescribeAsync(CancellationToken.None);
        Assert.Null(empty.OldestAvailableUtc);
        Assert.Equal((PassingReplayModes.AgentStore, TimeSpan.FromDays(30), true), (empty.Mode, empty.MaxRange!.Value, empty.SupportsLoopFilter));

        await StorePassingsAsync((5, "loop-1"), (2, "loop-1"));
        var listed = Assert.Single(await capability.GetCapabilitiesAsync(CancellationToken.None));
        Assert.Equal(AgentCapabilities.PassingReplay, listed.Name);
        Assert.Equal(T0.AddMinutes(2), listed.PassingReplay!.OldestAvailableUtc);

        options.AgentStoreEnabled = false;
        Assert.Empty(await capability.GetCapabilitiesAsync(CancellationToken.None));
    }

    private sealed class FakeBacklog : IOutboxBacklog
    {
        public Queue<int> Pending { get; } = new();
        public int Checks { get; private set; }
        public Action? OnCheck { get; set; }

        public Task<int> PendingAsync(CancellationToken cancellationToken)
        {
            Checks++;
            OnCheck?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Pending.TryDequeue(out var value) ? value : 0);
        }
    }
}
