using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Model;
using VeloTime.Module.Notifications.Processor.Workers;
using VeloTime.Module.Notifications.Storage;
using Xunit;
using Xunit.Sdk;

namespace VeloTime.Module.Notifications.Processor.Test;

public sealed class NotificationWorkDispatcherTests
{
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
    public async Task CurrentDeadlineCompletesAndAtomicallyBumpsRevisionAndEnqueuesRefresh()
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
            Assert.Null(hint.PublishedAtUtc);
            Assert.Equal(now, hint.CreatedAtUtc);
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
