using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_recompute_operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recompute_job",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_transponders = table.Column<int>(type: "integer", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recompute_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unresolved_passing",
                schema: "statistics",
                columns: table => new
                {
                    passing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unresolved_passing", x => x.passing_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recompute_request_causation",
                schema: "statistics",
                table: "recompute_request",
                column: "causation_id");

            migrationBuilder.CreateIndex(
                name: "ix_recompute_job_created_id",
                schema: "statistics",
                table: "recompute_job",
                columns: new[] { "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_unresolved_passing_transponder_time",
                schema: "statistics",
                table: "unresolved_passing",
                columns: new[] { "transponder_id", "time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recompute_job",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "unresolved_passing",
                schema: "statistics");

            migrationBuilder.DropIndex(
                name: "ix_recompute_request_causation",
                schema: "statistics",
                table: "recompute_request");
        }
    }
}
