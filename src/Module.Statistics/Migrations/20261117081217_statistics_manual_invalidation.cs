using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_manual_invalidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "manual_invalidation_changed_at_utc",
                schema: "statistics",
                table: "statistics_entry",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manual_invalidation_changed_by_user_id",
                schema: "statistics",
                table: "statistics_entry",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manual_invalidation_note",
                schema: "statistics",
                table: "statistics_entry",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_entry_validity",
                schema: "statistics",
                table: "statistics_entry");

            // Preserve NOT VALID: enforce future writes without scanning historical entries.
            migrationBuilder.Sql("""
                ALTER TABLE statistics.statistics_entry
                ADD CONSTRAINT ck_statistics_entry_validity
                CHECK ((is_valid AND invalid_reason IS NULL) OR
                    (NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3, 4, 5)) AND
                    (invalid_reason IS DISTINCT FROM 5 OR
                        (manual_invalidation_changed_by_user_id IS NOT NULL AND
                         manual_invalidation_changed_at_utc IS NOT NULL)))
                NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fail closed rather than silently discard active states or retained audit data.
            migrationBuilder.Sql("""
                LOCK TABLE statistics.statistics_entry IN ACCESS EXCLUSIVE MODE;
                DO $downgrade$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.statistics_entry
                        WHERE invalid_reason = 5
                            OR manual_invalidation_changed_by_user_id IS NOT NULL
                            OR manual_invalidation_changed_at_utc IS NOT NULL
                            OR manual_invalidation_note IS NOT NULL)
                    THEN
                        RAISE EXCEPTION USING MESSAGE = $message$Manual invalidation states or audit data prevent downgrade$message$;
                    END IF;
                END
                $downgrade$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_entry_validity",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "manual_invalidation_changed_at_utc",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "manual_invalidation_changed_by_user_id",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "manual_invalidation_note",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.Sql("""
                ALTER TABLE statistics.statistics_entry
                ADD CONSTRAINT ck_statistics_entry_validity
                CHECK ((is_valid AND invalid_reason IS NULL) OR
                    (NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3, 4)))
                NOT VALID;
                """);
        }
    }
}
