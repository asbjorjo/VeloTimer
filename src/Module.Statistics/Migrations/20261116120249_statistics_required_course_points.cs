using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_required_course_points : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL 11+ constant defaults backfill legacy rows without a table rewrite.
            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_entry_validity",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.AddColumn<Guid>(
                name: "required_course_layout_id",
                schema: "statistics",
                table: "statistics_item_config",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "required_course_point_ids",
                schema: "statistics",
                table: "statistics_item_config",
                type: "uuid[]",
                nullable: true,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<bool>(
                name: "required_course_points_satisfied",
                schema: "statistics",
                table: "statistics_entry",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Preserve the existing NOT VALID convention: enforce future writes without
            // scanning all historical results. No historical classifications are rewritten.
            migrationBuilder.Sql("""
                ALTER TABLE statistics.statistics_entry
                ADD CONSTRAINT ck_statistics_entry_validity
                CHECK ((is_valid AND invalid_reason IS NULL) OR
                    (NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3, 4)))
                NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fail closed rather than discard configured snapshots or relabel checkpoint
            // failures as an unrelated old reason. The locks exclude concurrent writes
            // between this guard and the column drops, within EF's migration transaction.
            migrationBuilder.Sql("""
                LOCK TABLE statistics.statistics_item_config, statistics.statistics_entry
                    IN ACCESS EXCLUSIVE MODE;
                DO $downgrade$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.statistics_item_config
                        WHERE required_course_layout_id IS NOT NULL
                            OR cardinality(required_course_point_ids) > 0)
                        OR EXISTS (SELECT 1 FROM statistics.statistics_entry
                            WHERE NOT required_course_points_satisfied OR invalid_reason = 4)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade required course points: configured requirements or checkpoint failures exist';
                    END IF;
                END
                $downgrade$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_entry_validity",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "required_course_layout_id",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "required_course_point_ids",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "required_course_points_satisfied",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.Sql("""
                ALTER TABLE statistics.statistics_entry
                ADD CONSTRAINT ck_statistics_entry_validity
                CHECK ((is_valid AND invalid_reason IS NULL) OR
                    (NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3)))
                NOT VALID;
                """);
        }
    }
}
