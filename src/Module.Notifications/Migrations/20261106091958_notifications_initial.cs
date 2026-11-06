using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VeloTime.Module.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class notifications_initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    english_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    norwegian_bokmal_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    norwegian_bokmal_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    audience_kind = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    audience_role = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    recipient_issuer = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    recipient_subject = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    allows_duration_dismissal = table.Column<bool>(type: "boolean", nullable: false),
                    allows_session_dismissal = table.Column<bool>(type: "boolean", nullable: false),
                    allows_permanent_dismissal = table.Column<bool>(type: "boolean", nullable: false),
                    default_dismissal_scope = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    dismissal_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                    table.CheckConstraint("ck_notification_audience", "(audience_kind = 'everyone' AND audience_role IS NULL AND recipient_issuer IS NULL AND recipient_subject IS NULL) OR (audience_kind = 'authenticated' AND audience_role IS NULL AND recipient_issuer IS NULL AND recipient_subject IS NULL) OR (audience_kind = 'role' AND audience_role IS NOT NULL AND length(btrim(audience_role)) > 0 AND recipient_issuer IS NULL AND recipient_subject IS NULL) OR (audience_kind = 'user' AND audience_role IS NULL AND recipient_issuer IS NOT NULL AND length(btrim(recipient_issuer)) > 0 AND recipient_subject IS NOT NULL AND length(btrim(recipient_subject)) > 0)");
                    table.CheckConstraint("ck_notification_content_nonblank", "length(btrim(english_title)) > 0 AND length(btrim(english_body)) > 0 AND length(btrim(norwegian_bokmal_title)) > 0 AND length(btrim(norwegian_bokmal_body)) > 0");
                    table.CheckConstraint("ck_notification_dismissal_policy", "((allows_duration_dismissal OR allows_session_dismissal OR allows_permanent_dismissal) AND (NOT allows_duration_dismissal OR (dismissal_duration_seconds BETWEEN 1 AND 31536000)) AND (allows_duration_dismissal OR dismissal_duration_seconds IS NULL) AND ((default_dismissal_scope = 'duration' AND allows_duration_dismissal) OR (default_dismissal_scope = 'session' AND allows_session_dismissal) OR (default_dismissal_scope = 'permanent' AND allows_permanent_dismissal)))");
                    table.CheckConstraint("ck_notification_finite_timestamps", "isfinite(starts_at_utc) AND (ends_at_utc IS NULL OR isfinite(ends_at_utc)) AND isfinite(created_at_utc) AND isfinite(updated_at_utc)");
                    table.CheckConstraint("ck_notification_schedule", "ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
                    table.CheckConstraint("ck_notification_severity", "severity IN ('info', 'warning', 'error')");
                    table.CheckConstraint("ck_notification_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "refresh_outbox",
                schema: "notifications",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_outbox", x => x.event_id);
                    table.CheckConstraint("ck_notification_refresh_outbox_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_notification_refresh_outbox_claim", "(claim_id IS NULL AND claimed_until_utc IS NULL) OR (claim_id IS NOT NULL AND claimed_until_utc IS NOT NULL)");
                    table.CheckConstraint("ck_notification_refresh_outbox_finite_timestamps", "isfinite(created_at_utc) AND isfinite(available_at_utc) AND (claimed_until_utc IS NULL OR isfinite(claimed_until_utc)) AND (published_at_utc IS NULL OR isfinite(published_at_utc))");
                });

            migrationBuilder.CreateTable(
                name: "snapshot_revision",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshot_revision", x => x.id);
                    table.CheckConstraint("ck_notification_snapshot_revision_singleton", "id = 1 AND revision >= 0");
                });

            migrationBuilder.CreateTable(
                name: "account_dismissal",
                schema: "notifications",
                columns: table => new
                {
                    issuer = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    subject = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    dismissed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dismissed_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_dismissal", x => new { x.issuer, x.subject, x.notification_id });
                    table.UniqueConstraint("ak_account_dismissal_id", x => x.id);
                    table.CheckConstraint("ck_notification_account_dismissal_finite_timestamps", "isfinite(dismissed_at_utc) AND (dismissed_until_utc IS NULL OR isfinite(dismissed_until_utc))");
                    table.CheckConstraint("ck_notification_account_dismissal_state", "(scope = 'duration' AND dismissed_until_utc IS NOT NULL AND dismissed_until_utc > dismissed_at_utc) OR (scope = 'permanent' AND dismissed_until_utc IS NULL)");
                    table.ForeignKey(
                        name: "fk_account_dismissal_notification_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notification",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_work",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_work", x => x.id);
                    table.CheckConstraint("ck_notification_scheduled_work_claim", "(claim_id IS NULL AND claimed_until_utc IS NULL) OR (claim_id IS NOT NULL AND claimed_until_utc IS NOT NULL)");
                    table.CheckConstraint("ck_notification_scheduled_work_finite_timestamps", "isfinite(due_at_utc) AND (claimed_until_utc IS NULL OR isfinite(claimed_until_utc)) AND (completed_at_utc IS NULL OR isfinite(completed_at_utc))");
                    table.CheckConstraint("ck_notification_scheduled_work_generation", "generation >= 1 AND attempt_count >= 0");
                    table.CheckConstraint("ck_notification_scheduled_work_kind", "kind IN ('NotificationStart', 'NotificationEnd', 'AccountDismissalExpiry', 'SessionDismissalExpiry')");
                    table.ForeignKey(
                        name: "fk_scheduled_work_notification_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notification",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_dismissal",
                schema: "notifications",
                columns: table => new
                {
                    issuer = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    subject = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_session_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dismissed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_dismissal", x => new { x.issuer, x.subject, x.notification_id, x.login_session_id });
                    table.UniqueConstraint("ak_session_dismissal_id", x => x.id);
                    table.CheckConstraint("ck_notification_session_dismissal_finite_timestamp", "isfinite(dismissed_at_utc)");
                    table.ForeignKey(
                        name: "fk_session_dismissal_notification_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notification",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "notifications",
                table: "snapshot_revision",
                columns: new[] { "id", "revision" },
                values: new object[] { 1, 0L });

            migrationBuilder.CreateIndex(
                name: "ix_account_dismissal_notification_id_dismissed_until_utc",
                schema: "notifications",
                table: "account_dismissal",
                columns: new[] { "notification_id", "dismissed_until_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_audience_kind_audience_role_starts_at_utc_ends",
                schema: "notifications",
                table: "notification",
                columns: new[] { "audience_kind", "audience_role", "starts_at_utc", "ends_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_audience_kind_recipient_issuer_recipient_subje",
                schema: "notifications",
                table: "notification",
                columns: new[] { "audience_kind", "recipient_issuer", "recipient_subject", "starts_at_utc", "ends_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_audience_kind_starts_at_utc_ends_at_utc",
                schema: "notifications",
                table: "notification",
                columns: new[] { "audience_kind", "starts_at_utc", "ends_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_outbox_published_at_utc_available_at_utc_claimed_un",
                schema: "notifications",
                table: "refresh_outbox",
                columns: new[] { "published_at_utc", "available_at_utc", "claimed_until_utc" },
                filter: "published_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_work_completed_at_utc_due_at_utc_claimed_until_utc",
                schema: "notifications",
                table: "scheduled_work",
                columns: new[] { "completed_at_utc", "due_at_utc", "claimed_until_utc" },
                filter: "completed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_work_notification_id_kind_source_id",
                schema: "notifications",
                table: "scheduled_work",
                columns: new[] { "notification_id", "kind", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_session_dismissal_issuer_subject_login_session_id_notificat",
                schema: "notifications",
                table: "session_dismissal",
                columns: new[] { "issuer", "subject", "login_session_id", "notification_id" });

            migrationBuilder.CreateIndex(
                name: "ix_session_dismissal_notification_id",
                schema: "notifications",
                table: "session_dismissal",
                column: "notification_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_dismissal",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "refresh_outbox",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "scheduled_work",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "session_dismissal",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "snapshot_revision",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "notifications");
        }
    }
}
