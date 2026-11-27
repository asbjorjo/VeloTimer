using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class ReplaySafePassingFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Alpha reset (docs/replay-safe-statistics-plan.md, Phase 1): stored samples are no longer derived,
            // and existing passings carry no agent SourcePassingId, so both are deleted rather than migrated.
            // Passing history returns only through an agent replay.
            migrationBuilder.DropTable(
                name: "sample",
                schema: "timing");

            migrationBuilder.Sql("DELETE FROM timing.passing;");

            migrationBuilder.AddColumn<Guid>(
                name: "installation_id",
                schema: "timing",
                table: "passing",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "source_passing_id",
                schema: "timing",
                table: "passing",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "passing_conflict",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_passing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    existing_passing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    low_battery = table.Column<bool>(type: "boolean", nullable: false),
                    low_strength = table.Column<bool>(type: "boolean", nullable: false),
                    low_hits = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passing_conflict", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passing_installation_id_source_passing_id",
                schema: "timing",
                table: "passing",
                columns: new[] { "installation_id", "source_passing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_passing_installation_id_transponder_id_time_id",
                schema: "timing",
                table: "passing",
                columns: new[] { "installation_id", "transponder_id", "time", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_passing_time",
                schema: "timing",
                table: "passing",
                column: "time");

            migrationBuilder.CreateIndex(
                name: "ix_passing_conflict_installation_id_source_passing_id_reason_t",
                schema: "timing",
                table: "passing_conflict",
                columns: new[] { "installation_id", "source_passing_id", "reason", "transponder_id", "timing_point_id", "time", "low_battery", "low_strength", "low_hits" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_passing_conflict_last_seen_at",
                schema: "timing",
                table: "passing_conflict",
                column: "last_seen_at");

            migrationBuilder.AddForeignKey(
                name: "fk_passing_installation_installation_id",
                schema: "timing",
                table: "passing",
                column: "installation_id",
                principalSchema: "timing",
                principalTable: "installation",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_passing_installation_installation_id",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropTable(
                name: "passing_conflict",
                schema: "timing");

            migrationBuilder.DropIndex(
                name: "ix_passing_installation_id_source_passing_id",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropIndex(
                name: "ix_passing_installation_id_transponder_id_time_id",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropIndex(
                name: "ix_passing_time",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropColumn(
                name: "installation_id",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropColumn(
                name: "source_passing_id",
                schema: "timing",
                table: "passing");

            migrationBuilder.CreateTable(
                name: "sample",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample", x => x.id);
                    table.ForeignKey(
                        name: "fk_sample_passing_end_id",
                        column: x => x.end_id,
                        principalSchema: "timing",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sample_passing_start_id",
                        column: x => x.start_id,
                        principalSchema: "timing",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sample_end_id",
                schema: "timing",
                table: "sample",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_start_id_end_id",
                schema: "timing",
                table: "sample",
                columns: new[] { "start_id", "end_id" },
                unique: true);
        }
    }
}
