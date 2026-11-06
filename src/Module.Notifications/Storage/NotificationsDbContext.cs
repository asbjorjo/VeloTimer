using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VeloTime.Module.Common;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Model;

namespace VeloTime.Module.Notifications.Storage;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : BaseDbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationAccountDismissal> AccountDismissals => Set<NotificationAccountDismissal>();
    public DbSet<NotificationSessionDismissal> SessionDismissals => Set<NotificationSessionDismissal>();
    public DbSet<NotificationRefreshOutbox> RefreshOutbox => Set<NotificationRefreshOutbox>();
    public DbSet<NotificationScheduledWork> ScheduledWork => Set<NotificationScheduledWork>();
    public DbSet<NotificationSnapshotRevision> SnapshotRevisions => Set<NotificationSnapshotRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notification", table =>
            {
                table.HasCheckConstraint(
                    "ck_notification_severity",
                    "severity IN ('info', 'warning', 'error')");
                table.HasCheckConstraint(
                    "ck_notification_version",
                    "version >= 1");
                table.HasCheckConstraint(
                    "ck_notification_content_nonblank",
                    "length(btrim(english_title)) > 0 AND length(btrim(english_body)) > 0 " +
                    "AND length(btrim(norwegian_bokmal_title)) > 0 AND length(btrim(norwegian_bokmal_body)) > 0");
                table.HasCheckConstraint(
                    "ck_notification_audience",
                    "(audience_kind = 'everyone' AND audience_role IS NULL AND recipient_issuer IS NULL AND recipient_subject IS NULL) OR " +
                    "(audience_kind = 'authenticated' AND audience_role IS NULL AND recipient_issuer IS NULL AND recipient_subject IS NULL) OR " +
                    "(audience_kind = 'role' AND audience_role IS NOT NULL AND length(btrim(audience_role)) > 0 AND recipient_issuer IS NULL AND recipient_subject IS NULL) OR " +
                    "(audience_kind = 'user' AND audience_role IS NULL AND recipient_issuer IS NOT NULL AND length(btrim(recipient_issuer)) > 0 " +
                    "AND recipient_subject IS NOT NULL AND length(btrim(recipient_subject)) > 0)");
                table.HasCheckConstraint(
                    "ck_notification_dismissal_policy",
                    "((allows_duration_dismissal OR allows_session_dismissal OR allows_permanent_dismissal) " +
                    "AND (NOT allows_duration_dismissal OR (dismissal_duration_seconds BETWEEN 1 AND 31536000)) " +
                    "AND (allows_duration_dismissal OR dismissal_duration_seconds IS NULL) " +
                    "AND ((default_dismissal_scope = 'duration' AND allows_duration_dismissal) OR " +
                    "(default_dismissal_scope = 'session' AND allows_session_dismissal) OR " +
                    "(default_dismissal_scope = 'permanent' AND allows_permanent_dismissal)))");
                table.HasCheckConstraint(
                    "ck_notification_schedule",
                    "ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
                table.HasCheckConstraint(
                    "ck_notification_finite_timestamps",
                    "isfinite(starts_at_utc) AND (ends_at_utc IS NULL OR isfinite(ends_at_utc)) " +
                    "AND isfinite(created_at_utc) AND isfinite(updated_at_utc)");
            });
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Id).ValueGeneratedNever();
            entity.Property(notification => notification.Version).IsConcurrencyToken();
            entity.Property(notification => notification.EnglishTitle).HasMaxLength(200).IsRequired();
            entity.Property(notification => notification.EnglishBody).HasMaxLength(4000).IsRequired();
            entity.Property(notification => notification.NorwegianBokmalTitle).HasMaxLength(200).IsRequired();
            entity.Property(notification => notification.NorwegianBokmalBody).HasMaxLength(4000).IsRequired();
            entity.Property(notification => notification.Severity)
                .HasConversion(new ValueConverter<NotificationSeverity, string>(
                    value => SeverityToCode(value),
                    value => SeverityFromCode(value)))
                .UseCollation("C");
            entity.Property(notification => notification.AudienceKind)
                .HasConversion(new ValueConverter<NotificationAudienceKind, string>(
                    value => AudienceKindToCode(value),
                    value => AudienceKindFromCode(value)))
                .UseCollation("C");
            entity.Property(notification => notification.AudienceRole).UseCollation("C");
            entity.Property(notification => notification.RecipientIssuer).UseCollation("C");
            entity.Property(notification => notification.RecipientSubject).UseCollation("C");
            entity.Property(notification => notification.DefaultDismissalScope)
                .HasConversion(new ValueConverter<NotificationDismissalScope, string>(
                    value => DismissalScopeToCode(value),
                    value => DismissalScopeFromCode(value)))
                .UseCollation("C");
            entity.HasIndex(notification => new
            {
                notification.AudienceKind,
                notification.StartsAtUtc,
                notification.EndsAtUtc
            });
            entity.HasIndex(notification => new
            {
                notification.AudienceKind,
                notification.AudienceRole,
                notification.StartsAtUtc,
                notification.EndsAtUtc
            });
            entity.HasIndex(notification => new
            {
                notification.AudienceKind,
                notification.RecipientIssuer,
                notification.RecipientSubject,
                notification.StartsAtUtc,
                notification.EndsAtUtc
            });
        });

        modelBuilder.Entity<NotificationAccountDismissal>(entity =>
        {
            entity.ToTable("account_dismissal", table =>
            {
                table.HasCheckConstraint(
                    "ck_notification_account_dismissal_state",
                    "(scope = 'duration' AND dismissed_until_utc IS NOT NULL AND dismissed_until_utc > dismissed_at_utc) OR " +
                    "(scope = 'permanent' AND dismissed_until_utc IS NULL)");
                table.HasCheckConstraint(
                    "ck_notification_account_dismissal_finite_timestamps",
                    "isfinite(dismissed_at_utc) AND (dismissed_until_utc IS NULL OR isfinite(dismissed_until_utc))");
            });
            entity.HasKey(dismissal => new { dismissal.Issuer, dismissal.Subject, dismissal.NotificationId });
            entity.HasAlternateKey(dismissal => dismissal.Id);
            entity.Property(dismissal => dismissal.Id).ValueGeneratedNever();
            entity.Property(dismissal => dismissal.Issuer).UseCollation("C");
            entity.Property(dismissal => dismissal.Subject).UseCollation("C");
            entity.Property(dismissal => dismissal.Scope)
                .HasConversion(new ValueConverter<NotificationDismissalScope, string>(
                    value => AccountDismissalScopeToCode(value),
                    value => AccountDismissalScopeFromCode(value)))
                .UseCollation("C");
            entity.HasOne<Notification>()
                .WithMany()
                .HasForeignKey(dismissal => dismissal.NotificationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(dismissal => new { dismissal.NotificationId, dismissal.DismissedUntilUtc });
        });

        modelBuilder.Entity<NotificationSessionDismissal>(entity =>
        {
            entity.ToTable("session_dismissal", table =>
                table.HasCheckConstraint(
                    "ck_notification_session_dismissal_finite_timestamp",
                    "isfinite(dismissed_at_utc)"));
            entity.HasKey(dismissal => new
            {
                dismissal.Issuer,
                dismissal.Subject,
                dismissal.NotificationId,
                dismissal.LoginSessionId
            });
            entity.HasAlternateKey(dismissal => dismissal.Id);
            entity.Property(dismissal => dismissal.Id).ValueGeneratedNever();
            entity.Property(dismissal => dismissal.Issuer).UseCollation("C");
            entity.Property(dismissal => dismissal.Subject).UseCollation("C");
            entity.Property(dismissal => dismissal.LoginSessionId).UseCollation("C");
            entity.HasOne<Notification>()
                .WithMany()
                .HasForeignKey(dismissal => dismissal.NotificationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(dismissal => new
            {
                dismissal.Issuer,
                dismissal.Subject,
                dismissal.LoginSessionId,
                dismissal.NotificationId
            });
        });

        modelBuilder.Entity<NotificationRefreshOutbox>(entity =>
        {
            entity.ToTable("refresh_outbox", table =>
            {
                table.HasCheckConstraint(
                    "ck_notification_refresh_outbox_attempt_count",
                    "attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_notification_refresh_outbox_claim",
                    "(claim_id IS NULL AND claimed_until_utc IS NULL) OR " +
                    "(claim_id IS NOT NULL AND claimed_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_notification_refresh_outbox_finite_timestamps",
                    "isfinite(created_at_utc) AND isfinite(available_at_utc) " +
                    "AND (claimed_until_utc IS NULL OR isfinite(claimed_until_utc)) " +
                    "AND (published_at_utc IS NULL OR isfinite(published_at_utc))");
            });
            entity.HasKey(message => message.EventId);
            entity.Property(message => message.EventId).ValueGeneratedNever();
            entity.HasIndex(message => new { message.PublishedAtUtc, message.AvailableAtUtc, message.ClaimedUntilUtc })
                .HasFilter("published_at_utc IS NULL");
        });

        modelBuilder.Entity<NotificationScheduledWork>(entity =>
        {
            entity.ToTable("scheduled_work", table =>
            {
                table.HasCheckConstraint(
                    "ck_notification_scheduled_work_generation",
                    "generation >= 1 AND attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_notification_scheduled_work_claim",
                    "(claim_id IS NULL AND claimed_until_utc IS NULL) OR " +
                    "(claim_id IS NOT NULL AND claimed_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_notification_scheduled_work_finite_timestamps",
                    "isfinite(due_at_utc) AND (claimed_until_utc IS NULL OR isfinite(claimed_until_utc)) " +
                    "AND (completed_at_utc IS NULL OR isfinite(completed_at_utc))");
                table.HasCheckConstraint(
                    "ck_notification_scheduled_work_kind",
                    "kind IN ('NotificationStart', 'NotificationEnd', 'AccountDismissalExpiry', 'SessionDismissalExpiry')");
            });
            entity.HasKey(work => work.Id);
            entity.Property(work => work.Id).ValueGeneratedNever();
            entity.Property(work => work.Kind).HasConversion<string>().UseCollation("C");
            entity.HasOne<Notification>()
                .WithMany()
                .HasForeignKey(work => work.NotificationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(work => new { work.NotificationId, work.Kind, work.SourceId }).IsUnique();
            entity.HasIndex(work => new { work.CompletedAtUtc, work.DueAtUtc, work.ClaimedUntilUtc })
                .HasFilter("completed_at_utc IS NULL");
        });

        modelBuilder.Entity<NotificationSnapshotRevision>(entity =>
        {
            entity.ToTable("snapshot_revision", table =>
                table.HasCheckConstraint("ck_notification_snapshot_revision_singleton", "id = 1 AND revision >= 0"));
            entity.HasKey(revision => revision.Id);
            entity.HasData(new NotificationSnapshotRevision { Id = 1, Revision = 0 });
        });

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (!HasSnapshotRelevantChanges())
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        var ownsTransaction = Database.CurrentTransaction is null && Database.IsRelational();
        using var transaction = ownsTransaction ? Database.BeginTransaction() : null;
        try
        {
            BumpSnapshotRevision();
            var affected = base.SaveChanges(acceptAllChangesOnSuccess);
            transaction?.Commit();
            return affected;
        }
        catch
        {
            transaction?.Rollback();
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        if (!HasSnapshotRelevantChanges())
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var ownsTransaction = Database.CurrentTransaction is null && Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            await BumpSnapshotRevisionAsync(cancellationToken);
            var affected = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return affected;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
    }

    private bool HasSnapshotRelevantChanges() =>
        ChangeTracker.Entries().Any(entry =>
            entry.State is (EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && entry.Entity is (Notification or NotificationAccountDismissal or NotificationSessionDismissal));

    private static string SeverityToCode(NotificationSeverity value) => value switch
    {
        NotificationSeverity.Info => "info",
        NotificationSeverity.Warning => "warning",
        NotificationSeverity.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static NotificationSeverity SeverityFromCode(string value) => value switch
    {
        "info" => NotificationSeverity.Info,
        "warning" => NotificationSeverity.Warning,
        "error" => NotificationSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static string AudienceKindToCode(NotificationAudienceKind value) => value switch
    {
        NotificationAudienceKind.Everyone => "everyone",
        NotificationAudienceKind.Authenticated => "authenticated",
        NotificationAudienceKind.Role => "role",
        NotificationAudienceKind.User => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static NotificationAudienceKind AudienceKindFromCode(string value) => value switch
    {
        "everyone" => NotificationAudienceKind.Everyone,
        "authenticated" => NotificationAudienceKind.Authenticated,
        "role" => NotificationAudienceKind.Role,
        "user" => NotificationAudienceKind.User,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static string DismissalScopeToCode(NotificationDismissalScope value) => value switch
    {
        NotificationDismissalScope.Duration => "duration",
        NotificationDismissalScope.Session => "session",
        NotificationDismissalScope.Permanent => "permanent",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static NotificationDismissalScope DismissalScopeFromCode(string value) => value switch
    {
        "duration" => NotificationDismissalScope.Duration,
        "session" => NotificationDismissalScope.Session,
        "permanent" => NotificationDismissalScope.Permanent,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static string AccountDismissalScopeToCode(NotificationDismissalScope value) => value switch
    {
        NotificationDismissalScope.Duration => "duration",
        NotificationDismissalScope.Permanent => "permanent",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static NotificationDismissalScope AccountDismissalScopeFromCode(string value) => value switch
    {
        "duration" => NotificationDismissalScope.Duration,
        "permanent" => NotificationDismissalScope.Permanent,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private void BumpSnapshotRevision()
    {
        if (!Database.IsRelational())
        {
            throw new InvalidOperationException("Notification snapshot revision fencing requires a relational database.");
        }

        var affected = Database.ExecuteSqlRaw(
            "UPDATE notifications.snapshot_revision SET revision = revision + 1 WHERE id = 1");
        if (affected != 1)
        {
            throw new InvalidOperationException("The Notifications snapshot revision singleton is missing.");
        }
    }

    private async Task BumpSnapshotRevisionAsync(CancellationToken cancellationToken)
    {
        if (!Database.IsRelational())
        {
            throw new InvalidOperationException("Notification snapshot revision fencing requires a relational database.");
        }

        var affected = await Database.ExecuteSqlRawAsync(
            "UPDATE notifications.snapshot_revision SET revision = revision + 1 WHERE id = 1",
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("The Notifications snapshot revision singleton is missing.");
        }
    }
}

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>();
        options.UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__velotimedb")
                ?? "Host=localhost;Port=25432;Database=velotimedb;Username=velotime",
            postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "ef"));
        options.UseSnakeCaseNamingConvention();
        return new NotificationsDbContext(options.Options);
    }
}
