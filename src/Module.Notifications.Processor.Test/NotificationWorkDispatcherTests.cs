using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Model;
using VeloTime.Module.Notifications.Processor.Workers;
using VeloTime.Module.Notifications.Storage;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace VeloTime.Module.Notifications.Processor.Test;

public sealed class NotificationWorkDispatcherTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("0.25", true)]
    [InlineData("2", true)]
    [InlineData("60", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.249", false)]
    [InlineData("60.01", false)]
    [InlineData("NaN", false)]
    [InlineData("Infinity", false)]
    public void PollIntervalConfigurationValidatesFractionalAndLegacySeconds(string value, bool valid)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{NotificationWorkerOptions.Section}:PollIntervalSeconds"] = value
            }).Build();
        var services = new ServiceCollection();
        services.AddOptions<NotificationWorkerOptions>()
            .Bind(configuration.GetSection(NotificationWorkerOptions.Section))
            .ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NotificationWorkerOptions>>();
        if (valid)
        {
            Assert.Equal(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                options.Value.PollIntervalSeconds);
        }
        else
        {
            Assert.Throws<OptionsValidationException>(() => options.Value);
        }
    }

    [Fact]
    public async Task FailedPublicationIsDurablyRetriedAndAcknowledgedAfterRestart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var eventId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.RefreshOutbox.Add(new()
            {
                EventId = eventId,
                CreatedAtUtc = now,
                AvailableAtUtc = now.AddSeconds(-1)
            });
            await db.SaveChangesAsync();
        }

        var clock = new TestClock(now);
        var calls = new List<Guid>();
        var failingPublisher = new TestPublisher((id, _) =>
        {
            calls.Add(id);
            return Task.FromException(new InvalidOperationException("test transport failure"));
        });
        await DispatchOnceAsync(database, failingPublisher, clock);

        await using (var db = database.CreateContext())
        {
            var stored = await db.RefreshOutbox.SingleAsync(x => x.EventId == eventId);
            Assert.Equal(1, stored.AttemptCount);
            Assert.Null(stored.ClaimId);
            Assert.Null(stored.ClaimedUntilUtc);
            Assert.Null(stored.PublishedAtUtc);
            Assert.True(stored.AvailableAtUtc > now);
        }

        await using (var db = database.CreateContext())
        {
            await db.RefreshOutbox.Where(x => x.EventId == eventId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.AvailableAtUtc, now));
        }

        var succeedingPublisher = new TestPublisher((id, _) =>
        {
            calls.Add(id);
            return Task.CompletedTask;
        });
        await DispatchOnceAsync(database, succeedingPublisher, clock);

        Assert.Equal(new[] { eventId, eventId }, calls);
        await using (var db = database.CreateContext())
        {
            var stored = await db.RefreshOutbox.SingleAsync(x => x.EventId == eventId);
            Assert.Equal(2, stored.AttemptCount);
            Assert.NotNull(stored.PublishedAtUtc);
            Assert.Null(stored.ClaimId);
        }
    }

    [Fact]
    public async Task LostAcknowledgementCanRedeliverSameEventIdAfterLeaseExpiry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var eventId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.RefreshOutbox.Add(new()
            {
                EventId = eventId,
                CreatedAtUtc = now,
                AvailableAtUtc = now
            });
            await db.SaveChangesAsync();
        }

        var calls = new List<Guid>();
        var publisher = new TestPublisher(async (id, _) =>
        {
            calls.Add(id);
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE notifications.refresh_outbox
                SET claim_id = @new_claim
                WHERE event_id = @event_id
                """;
            command.Parameters.AddWithValue("new_claim", Guid.NewGuid());
            command.Parameters.AddWithValue("event_id", eventId);
            await command.ExecuteNonQueryAsync();
        });

        var clock = new TestClock(now);
        await DispatchOnceAsync(database, publisher, clock);

        await using (var db = database.CreateContext())
        {
            var stored = await db.RefreshOutbox.SingleAsync(x => x.EventId == eventId);
            Assert.Null(stored.PublishedAtUtc);
            Assert.NotNull(stored.ClaimId);
            await db.RefreshOutbox.Where(x => x.EventId == eventId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.AvailableAtUtc, now)
                    .SetProperty(x => x.ClaimedUntilUtc, (DateTimeOffset?)now.AddSeconds(-1)));
        }

        var retryPublisher = new TestPublisher((id, _) =>
        {
            calls.Add(id);
            return Task.CompletedTask;
        });
        await DispatchOnceAsync(database, retryPublisher, clock);

        Assert.Equal(new[] { eventId, eventId }, calls);
        await using (var db = database.CreateContext())
        {
            Assert.NotNull((await db.RefreshOutbox.SingleAsync(x => x.EventId == eventId)).PublishedAtUtc);
        }
    }

    [Fact]
    public async Task CurrentDeadlineCompletesAtomicallyAndPublishesRefreshInSameCycle()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var notificationId = Guid.NewGuid();
        var start = now.AddSeconds(-1);
        var workId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Notifications.Add(CreateNotification(notificationId, start, null, now));
            db.ScheduledWork.Add(new()
            {
                Id = workId,
                NotificationId = notificationId,
                Kind = NotificationScheduledWorkKind.NotificationStart,
                SourceId = notificationId,
                DueAtUtc = start
            });
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
        }

        var publisher = new TestPublisher((_, _) => Task.CompletedTask);
        var clock = new TestClock(now);
        await DispatchOnceAsync(database, publisher, clock);

        await using (var db = database.CreateContext())
        {
            var slot = await db.ScheduledWork.SingleAsync(x => x.Id == workId);
            Assert.NotNull(slot.CompletedAtUtc);
            Assert.Null(slot.ClaimId);
            Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            var hint = await db.RefreshOutbox.SingleAsync();
            Assert.NotNull(hint.PublishedAtUtc);
            Assert.Equal(now, hint.CreatedAtUtc);
            Assert.Equal(new[] { hint.EventId }, publisher.EventIds);
        }

        await DispatchOnceAsync(database, publisher, clock);
        Assert.Single(publisher.EventIds);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            Assert.NotNull((await db.RefreshOutbox.SingleAsync()).PublishedAtUtc);
        }
    }

    [Fact]
    public async Task DeadlineHintPublicationFailureRetriesStableEventWithoutCompletingDeadlineAgain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var notificationId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Notifications.Add(CreateNotification(notificationId, now, null, now));
            db.ScheduledWork.Add(new()
            {
                Id = Guid.NewGuid(),
                NotificationId = notificationId,
                Kind = NotificationScheduledWorkKind.NotificationStart,
                SourceId = notificationId,
                DueAtUtc = now
            });
            await db.SaveChangesAsync();
        }

        var failingPublisher = new TestPublisher((_, _) =>
            Task.FromException(new InvalidOperationException("test transport failure")));
        await DispatchOnceAsync(database, failingPublisher, new TestClock(now));
        var eventId = Assert.Single(failingPublisher.EventIds);
        await using (var db = database.CreateContext())
        {
            Assert.NotNull((await db.ScheduledWork.SingleAsync()).CompletedAtUtc);
            Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            var hint = await db.RefreshOutbox.SingleAsync();
            Assert.Equal(eventId, hint.EventId);
            Assert.Null(hint.PublishedAtUtc);
            Assert.Null(hint.ClaimId);
            Assert.Equal(now.AddSeconds(1), hint.AvailableAtUtc);
            Assert.Equal(1, hint.AttemptCount);
        }

        var publisher = new TestPublisher((_, _) => Task.CompletedTask);
        await DispatchOnceAsync(database, publisher, new TestClock(now));
        Assert.Empty(publisher.EventIds); // A shorter poll must not bypass durable backoff.
        await DispatchOnceAsync(database, publisher, new TestClock(now.AddSeconds(1)));
        await DispatchOnceAsync(database, publisher, new TestClock(now.AddSeconds(2)));
        Assert.Equal(new[] { eventId }, publisher.EventIds);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            var hint = await db.RefreshOutbox.SingleAsync();
            Assert.NotNull(hint.PublishedAtUtc);
            Assert.Equal(2, hint.AttemptCount);
        }
    }

    [Fact]
    public async Task DispatchCycleKeepsDeadlineAndOutboxBatchesBounded()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        await using (var db = database.CreateContext())
        {
            for (var index = 0; index < 11; index++)
            {
                var notificationId = Guid.NewGuid();
                db.Notifications.Add(CreateNotification(notificationId, now, null, now));
                db.ScheduledWork.Add(new()
                {
                    Id = Guid.NewGuid(),
                    NotificationId = notificationId,
                    Kind = NotificationScheduledWorkKind.NotificationStart,
                    SourceId = notificationId,
                    DueAtUtc = now
                });
            }
            await db.SaveChangesAsync();
        }

        var publisher = new TestPublisher((_, _) => Task.CompletedTask);
        var clock = new TestClock(now);
        await DispatchOnceAsync(database, publisher, clock); // Test helper batch size is ten.
        Assert.Equal(10, publisher.EventIds.Count);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(10, await db.ScheduledWork.CountAsync(x => x.CompletedAtUtc != null));
            Assert.Equal(10, await db.RefreshOutbox.CountAsync(x => x.PublishedAtUtc != null));
        }

        await DispatchOnceAsync(database, publisher, clock);
        Assert.Equal(11, publisher.EventIds.Distinct().Count());
        await using (var db = database.CreateContext())
        {
            Assert.Equal(11, await db.ScheduledWork.CountAsync(x => x.CompletedAtUtc != null));
            Assert.Equal(11, await db.RefreshOutbox.CountAsync(x => x.PublishedAtUtc != null));
        }
    }

    [Fact]
    public async Task EmptyCancelledIntervalDoesNotEmitDeadlineRefresh()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var notificationId = Guid.NewGuid();
        var start = now.AddSeconds(-1);
        var end = start.AddTicks(-1);
        var workId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Notifications.Add(CreateNotification(notificationId, start, end, now));
            db.ScheduledWork.Add(new()
            {
                Id = workId,
                NotificationId = notificationId,
                Kind = NotificationScheduledWorkKind.NotificationStart,
                SourceId = notificationId,
                DueAtUtc = start
            });
            await db.SaveChangesAsync();
        }

        var publisher = new TestPublisher((_, _) => Task.CompletedTask);
        await DispatchOnceAsync(database, publisher, new TestClock(now));

        Assert.Empty(publisher.EventIds);
        await using (var db = database.CreateContext())
        {
            Assert.NotNull((await db.ScheduledWork.SingleAsync(x => x.Id == workId)).CompletedAtUtc);
            Assert.Equal(1, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            Assert.Empty(await db.RefreshOutbox.ToListAsync());
        }
    }

    [Fact]
    public async Task RescheduleWhileDeadlineClaimWaitsOnSourceLockFencesOldGeneration()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var notificationId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Notifications.Add(CreateNotification(notificationId, now.AddSeconds(-1), null, now));
            db.ScheduledWork.Add(new()
            {
                Id = workId,
                NotificationId = notificationId,
                Kind = NotificationScheduledWorkKind.NotificationStart,
                SourceId = notificationId,
                DueAtUtc = now.AddSeconds(-1)
            });
            await db.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockSource = connection.CreateCommand())
        {
            lockSource.Transaction = transaction;
            lockSource.CommandText = "SELECT id FROM notifications.notification WHERE id = @id FOR UPDATE";
            lockSource.Parameters.AddWithValue("id", notificationId);
            Assert.Equal(notificationId, await lockSource.ExecuteScalarAsync());
        }

        var publisher = new TestPublisher((_, _) => Task.CompletedTask);
        var clock = new TestClock(now);
        var processing = DispatchOnceAsync(database, publisher, clock);
        Guid? oldClaim = null;
        for (var attempt = 0; attempt < 100 && oldClaim is null; attempt++)
        {
            await using var observe = new NpgsqlConnection(database.ConnectionString);
            await observe.OpenAsync();
            await using var command = observe.CreateCommand();
            command.CommandText = "SELECT claim_id FROM notifications.scheduled_work WHERE id = @id";
            command.Parameters.AddWithValue("id", workId);
            var claimValue = await command.ExecuteScalarAsync();
            oldClaim = claimValue is Guid claimId ? claimId : null;
            if (oldClaim is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }
        }

        Assert.NotNull(oldClaim);
        await using (var reschedule = connection.CreateCommand())
        {
            reschedule.Transaction = transaction;
            reschedule.CommandText = """
                UPDATE notifications.scheduled_work
                SET generation = generation + 1, claim_id = NULL, claimed_until_utc = NULL,
                    completed_at_utc = NULL, attempt_count = 0
                WHERE id = @id AND claim_id = @old_claim
                """;
            reschedule.Parameters.AddWithValue("id", workId);
            reschedule.Parameters.AddWithValue("old_claim", oldClaim.Value);
            Assert.Equal(1, await reschedule.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        await processing;

        await using (var db = database.CreateContext())
        {
            var stale = await db.ScheduledWork.SingleAsync(x => x.Id == workId);
            Assert.Equal(2, stale.Generation);
            Assert.Null(stale.ClaimId);
            Assert.Null(stale.CompletedAtUtc);
            Assert.Empty(await db.RefreshOutbox.ToListAsync());
            Assert.Equal(1, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
        }

        await DispatchOnceAsync(database, publisher, clock);
        await using (var db = database.CreateContext())
        {
            Assert.NotNull((await db.ScheduledWork.SingleAsync(x => x.Id == workId)).CompletedAtUtc);
            Assert.Single(await db.RefreshOutbox.ToListAsync());
            Assert.NotNull((await db.RefreshOutbox.SingleAsync()).PublishedAtUtc);
            Assert.Single(publisher.EventIds);
            Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
        }
    }

    [Fact]
    public async Task MatchingAccountDurationExpiryEnqueuesRefreshAndInvalidatesSnapshotRevision()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = PersistedNow();
        var notificationId = Guid.NewGuid();
        var dismissalId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Notifications.Add(CreateNotification(notificationId, now.AddDays(-1), null, now));
            db.AccountDismissals.Add(new()
            {
                Id = dismissalId,
                Issuer = "https://issuer.example",
                Subject = "subject",
                NotificationId = notificationId,
                Scope = NotificationDismissalScope.Duration,
                DismissedAtUtc = now.AddSeconds(-10),
                DismissedUntilUtc = now.AddSeconds(-1)
            });
            db.ScheduledWork.Add(new()
            {
                Id = workId,
                NotificationId = notificationId,
                Kind = NotificationScheduledWorkKind.AccountDismissalExpiry,
                SourceId = dismissalId,
                DueAtUtc = now.AddSeconds(-1)
            });
            await db.SaveChangesAsync();
        }

        var publisher = new TestPublisher((_, _) => Task.CompletedTask);
        await DispatchOnceAsync(database, publisher, new TestClock(now));

        await using (var db = database.CreateContext())
        {
            Assert.NotNull((await db.ScheduledWork.SingleAsync(x => x.Id == workId)).CompletedAtUtc);
            Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            Assert.Single(await db.RefreshOutbox.ToListAsync());
            Assert.NotNull((await db.RefreshOutbox.SingleAsync()).PublishedAtUtc);
            Assert.Single(publisher.EventIds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedLoopPublishesWorkEligibleJustAfterCycleWithinLocalSchedulingBudget(bool deadline)
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new PollObservingClock();
        var published = new TaskCompletionSource<(Guid EventId, long Timestamp)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new TestPublisher((id, _) =>
        {
            published.TrySetResult((id, Stopwatch.GetTimestamp()));
            return Task.CompletedTask;
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped(_ => database.CreateContext());
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<INotificationRefreshPublisher>(publisher);
        builder.Services.AddOptions<NotificationWorkerOptions>().ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddScoped<NotificationWorkDispatcher>();
        builder.Services.AddHostedService<NotificationWorkDispatcherWorker>();
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            // Observe the real worker entering its first post-cycle delay. Only then make work
            // due, exercising the worst idle-boundary placement rather than startup dispatch.
            var firstDelay = await clock.FirstDelay.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TimeSpan.FromMilliseconds(250), firstDelay);
            var eligibleTimestamp = Stopwatch.GetTimestamp();
            var now = PersistedNow();
            var sourceId = Guid.NewGuid();
            await using (var db = database.CreateContext())
            {
                if (deadline)
                {
                    db.Notifications.Add(CreateNotification(sourceId, now, null, now));
                    db.ScheduledWork.Add(new()
                    {
                        Id = Guid.NewGuid(),
                        NotificationId = sourceId,
                        Kind = NotificationScheduledWorkKind.NotificationStart,
                        SourceId = sourceId,
                        DueAtUtc = now
                    });
                }
                else
                {
                    db.RefreshOutbox.Add(new()
                    {
                        EventId = sourceId,
                        CreatedAtUtc = now,
                        AvailableAtUtc = now
                    });
                }

                await db.SaveChangesAsync();
            }

            var result = await published.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var elapsed = Stopwatch.GetElapsedTime(eligibleTimestamp, result.Timestamp);
            output.WriteLine($"{(deadline ? "Deadline" : "Outbox")} just-after-cycle eligibility-to-fake-publisher: {elapsed.TotalMilliseconds:F1} ms");
            Assert.True(elapsed <= TimeSpan.FromSeconds(2), $"Scheduling latency was {elapsed}.");
            if (!deadline)
            {
                Assert.Equal(sourceId, result.EventId);
            }

            // Publication precedes acknowledgement. Wait for the post-cycle timer so stop
            // exercises idle cancellation without racing the durable acknowledgement.
            Assert.Equal(firstDelay,
                await clock.SecondDelay.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            var stop = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await host.StopAsync(timeout.Token);
            output.WriteLine($"Hosted-loop cancellation/stop: {stop.Elapsed.TotalMilliseconds:F1} ms");
        }

        await using (var db = database.CreateContext())
        {
            var hint = await db.RefreshOutbox.SingleAsync();
            Assert.NotNull(hint.PublishedAtUtc);
            Assert.Equal(new[] { hint.EventId }, publisher.EventIds);
            if (deadline)
            {
                Assert.NotNull((await db.ScheduledWork.SingleAsync()).CompletedAtUtc);
                Assert.Equal(2, await db.SnapshotRevisions.Select(x => x.Revision).SingleAsync());
            }
        }
    }

    private static async Task DispatchOnceAsync(
        TestDatabase database,
        INotificationRefreshPublisher publisher,
        TimeProvider clock)
    {
        await using var context = database.CreateContext();
        var dispatcher = new NotificationWorkDispatcher(context, publisher, clock,
            Options.Create(new NotificationWorkerOptions
            {
                BatchSize = 10,
                ClaimLeaseSeconds = 10,
                RetryInitialDelaySeconds = 1,
                RetryMaximumDelaySeconds = 4
            }),
            NullLogger<NotificationWorkDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(default);
    }

    private static DateTimeOffset PersistedNow()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static Notification CreateNotification(
        Guid id,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        DateTimeOffset now) => new()
    {
        Id = id,
        Version = 1,
        EnglishTitle = "Title",
        EnglishBody = "Body",
        NorwegianBokmalTitle = "Tittel",
        NorwegianBokmalBody = "Tekst",
        Severity = NotificationSeverity.Info,
        AudienceKind = NotificationAudienceKind.Everyone,
        StartsAtUtc = startsAt,
        EndsAtUtc = endsAt,
        AllowsSessionDismissal = true,
        AllowsPermanentDismissal = true,
        DefaultDismissalScope = NotificationDismissalScope.Permanent,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private sealed class TestPublisher(Func<Guid, CancellationToken, Task> publish)
        : INotificationRefreshPublisher
    {
        public List<Guid> EventIds { get; } = [];

        public Task PublishAsync(Guid eventId, CancellationToken cancellationToken)
        {
            EventIds.Add(eventId);
            return publish(eventId, cancellationToken);
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class PollObservingClock : TimeProvider
    {
        private int timerCount;

        public TaskCompletionSource<TimeSpan> FirstDelay { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TimeSpan> SecondDelay { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            var count = Interlocked.Increment(ref timerCount);
            if (count == 1)
            {
                FirstDelay.TrySetResult(dueTime);
            }
            else if (count == 2)
            {
                SecondDelay.TrySetResult(dueTime);
            }
            return timer;
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;

        private TestDatabase(string adminConnectionString, string connectionString)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var adminConnectionString = Environment.GetEnvironmentVariable("NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION");
            if (string.IsNullOrWhiteSpace(adminConnectionString))
            {
                throw SkipException.ForSkip(
                    "Set NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION to an isolated PostgreSQL admin connection.");
            }

            var databaseName = $"notifications_worker_test_{Guid.NewGuid():N}";
            var connectionBuilder = new DbConnectionStringBuilder { ConnectionString = adminConnectionString! };
            connectionBuilder["Database"] = databaseName;
            var connectionString = connectionBuilder.ConnectionString;

            await using (var connection = new NpgsqlConnection(adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                await command.ExecuteNonQueryAsync();
            }

            var database = new TestDatabase(adminConnectionString!, connectionString);
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }

        public NotificationsDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseNpgsql(ConnectionString, postgres =>
                    postgres.MigrationsHistoryTable("__ef_migrations_history", "ef"))
                .UseSnakeCaseNamingConvention()
                .Options;
            return new NotificationsDbContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE \"{new DbConnectionStringBuilder { ConnectionString = ConnectionString }["Database"]}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
    }
}
