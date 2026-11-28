using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class AddLoopStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_passing_conflict_installation_id_source_passing_id_reason_t",
                schema: "timing",
                table: "passing_conflict");

            migrationBuilder.DropIndex(
                name: "ix_passing_timing_point_id",
                schema: "timing",
                table: "passing");

            migrationBuilder.AddColumn<int>(
                name: "hits",
                schema: "timing",
                table: "rejected_passing",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "strength",
                schema: "timing",
                table: "rejected_passing",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "hits",
                schema: "timing",
                table: "passing_conflict",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "strength",
                schema: "timing",
                table: "passing_conflict",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "hits",
                schema: "timing",
                table: "passing",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "strength",
                schema: "timing",
                table: "passing",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "loop_status",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    noise = table.Column<double>(type: "double precision", nullable: false),
                    loop_triggers = table.Column<int>(type: "integer", nullable: true),
                    decoder_temperature = table.Column<double>(type: "double precision", nullable: true),
                    input_voltage = table.Column<double>(type: "double precision", nullable: true),
                    time_synchronised = table.Column<bool>(type: "boolean", nullable: true),
                    satellites_in_use = table.Column<int>(type: "integer", nullable: true),
                    decoder_id = table.Column<string>(type: "text", nullable: true),
                    noise_shares_strength_scale = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loop_status", x => x.id);
                    table.ForeignKey(
                        name: "fk_loop_status_installation_installation_id",
                        column: x => x.installation_id,
                        principalSchema: "timing",
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_loop_status_timing_point_timing_point_id",
                        column: x => x.timing_point_id,
                        principalSchema: "timing",
                        principalTable: "timing_point",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loop_status_rollup",
                schema: "timing",
                columns: table => new
                {
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    samples = table.Column<int>(type: "integer", nullable: false),
                    noise_sum = table.Column<double>(type: "double precision", nullable: false),
                    noise_min = table.Column<double>(type: "double precision", nullable: false),
                    noise_max = table.Column<double>(type: "double precision", nullable: false),
                    temperature_samples = table.Column<int>(type: "integer", nullable: false),
                    temperature_sum = table.Column<double>(type: "double precision", nullable: false),
                    temperature_min = table.Column<double>(type: "double precision", nullable: true),
                    temperature_max = table.Column<double>(type: "double precision", nullable: true),
                    voltage_samples = table.Column<int>(type: "integer", nullable: false),
                    voltage_sum = table.Column<double>(type: "double precision", nullable: false),
                    voltage_min = table.Column<double>(type: "double precision", nullable: true),
                    voltage_max = table.Column<double>(type: "double precision", nullable: true),
                    trigger_samples = table.Column<int>(type: "integer", nullable: false),
                    triggers_sum = table.Column<long>(type: "bigint", nullable: false),
                    sync_samples = table.Column<int>(type: "integer", nullable: false),
                    unsynchronised_samples = table.Column<int>(type: "integer", nullable: false),
                    shared_scale_samples = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loop_status_rollup", x => new { x.timing_point_id, x.bucket_start });
                    table.ForeignKey(
                        name: "fk_loop_status_rollup_installation_installation_id",
                        column: x => x.installation_id,
                        principalSchema: "timing",
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_loop_status_rollup_timing_point_timing_point_id",
                        column: x => x.timing_point_id,
                        principalSchema: "timing",
                        principalTable: "timing_point",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passing_conflict_installation_id_source_passing_id_reason_t",
                schema: "timing",
                table: "passing_conflict",
                columns: new[] { "installation_id", "source_passing_id", "reason", "transponder_id", "timing_point_id", "time", "low_battery", "low_strength", "low_hits", "strength", "hits" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_passing_timing_point_id_time",
                schema: "timing",
                table: "passing",
                columns: new[] { "timing_point_id", "time" });

            migrationBuilder.CreateIndex(
                name: "ix_passing_timing_point_id_time1",
                schema: "timing",
                table: "passing",
                columns: new[] { "timing_point_id", "time" },
                filter: "low_strength OR low_hits OR low_battery");

            migrationBuilder.CreateIndex(
                name: "ix_loop_status_installation_id_time",
                schema: "timing",
                table: "loop_status",
                columns: new[] { "installation_id", "time" });

            migrationBuilder.CreateIndex(
                name: "ix_loop_status_timing_point_id_time",
                schema: "timing",
                table: "loop_status",
                columns: new[] { "timing_point_id", "time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loop_status_rollup_installation_id_bucket_start",
                schema: "timing",
                table: "loop_status_rollup",
                columns: new[] { "installation_id", "bucket_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loop_status",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "loop_status_rollup",
                schema: "timing");

            migrationBuilder.DropIndex(
                name: "ix_passing_conflict_installation_id_source_passing_id_reason_t",
                schema: "timing",
                table: "passing_conflict");

            migrationBuilder.DropIndex(
                name: "ix_passing_timing_point_id_time",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropIndex(
                name: "ix_passing_timing_point_id_time1",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropColumn(
                name: "hits",
                schema: "timing",
                table: "rejected_passing");

            migrationBuilder.DropColumn(
                name: "strength",
                schema: "timing",
                table: "rejected_passing");

            migrationBuilder.DropColumn(
                name: "hits",
                schema: "timing",
                table: "passing_conflict");

            migrationBuilder.DropColumn(
                name: "strength",
                schema: "timing",
                table: "passing_conflict");

            migrationBuilder.DropColumn(
                name: "hits",
                schema: "timing",
                table: "passing");

            migrationBuilder.DropColumn(
                name: "strength",
                schema: "timing",
                table: "passing");

            migrationBuilder.CreateIndex(
                name: "ix_passing_conflict_installation_id_source_passing_id_reason_t",
                schema: "timing",
                table: "passing_conflict",
                columns: new[] { "installation_id", "source_passing_id", "reason", "transponder_id", "timing_point_id", "time", "low_battery", "low_strength", "low_hits" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_passing_timing_point_id",
                schema: "timing",
                table: "passing",
                column: "timing_point_id");
        }
    }
}
