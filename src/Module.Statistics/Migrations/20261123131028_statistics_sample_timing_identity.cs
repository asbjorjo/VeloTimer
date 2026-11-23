using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_sample_timing_identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "timing_point_end_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "timing_point_start_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_sample_timing_identity",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "timing_point_start_id", "time_start", "timing_point_end_id", "time_end" },
                unique: true,
                filter: "timing_point_start_id IS NOT NULL AND timing_point_end_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sample_timing_points_pair",
                schema: "statistics",
                table: "sample",
                sql: "(timing_point_start_id IS NULL AND timing_point_end_id IS NULL) OR (timing_point_start_id IS NOT NULL AND timing_point_end_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Silently discarding source IDs would make later reconciliation impossible.
            migrationBuilder.Sql("""
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.sample
                               WHERE timing_point_start_id IS NOT NULL OR timing_point_end_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot downgrade sample timing identity while keyed samples exist.';
                    END IF;
                END;
                $rollback$;
                """);
            migrationBuilder.DropIndex(
                name: "ux_sample_timing_identity",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sample_timing_points_pair",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropColumn(
                name: "timing_point_end_id",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropColumn(
                name: "timing_point_start_id",
                schema: "statistics",
                table: "sample");
        }
    }
}
