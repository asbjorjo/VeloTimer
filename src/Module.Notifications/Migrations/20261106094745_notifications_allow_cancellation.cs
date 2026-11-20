using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class notifications_allow_cancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_schedule",
                schema: "notifications",
                table: "notification");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_schedule",
                schema: "notifications",
                table: "notification",
                sql: "ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
        }
    }
}
