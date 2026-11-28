using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_validity_thresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL 11+ fast constant defaults retain legacy classifications without
            // rewriting measurements. Revalidation is asynchronous, not migration work.
            // The entry CHECK is NOT VALID to avoid scanning the historical table here;
            // PostgreSQL still enforces it for all subsequently inserted/updated rows.
            migrationBuilder.AddColumn<TimeSpan>(
                name: "max_duration",
                schema: "statistics",
                table: "statistics_item_config",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "min_duration",
                schema: "statistics",
                table: "statistics_item_config",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "max_duration",
                schema: "statistics",
                table: "statistics_item",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "min_duration",
                schema: "statistics",
                table: "statistics_item",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_complete_contributor_lineage",
                schema: "statistics",
                table: "statistics_entry",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "invalid_reason",
                schema: "statistics",
                table: "statistics_entry",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_valid",
                schema: "statistics",
                table: "statistics_entry",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "statistics_entry_contributor",
                schema: "statistics",
                columns: table => new
                {
                    statistics_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    contributor_entry_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_entry_contributor", x => new { x.statistics_entry_id, x.position });
                    table.CheckConstraint("ck_statistics_entry_contributor", "position >= 0 AND statistics_entry_id <> contributor_entry_id");
                    table.ForeignKey(
                        name: "fk_statistics_entry_contributor_statistics_entry_contributor_e",
                        column: x => x.contributor_entry_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_statistics_entry_contributor_statistics_entry_statistics_en",
                        column: x => x.statistics_entry_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "statistics_validity_revalidation",
                schema: "statistics",
                columns: table => new
                {
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    is_pending = table.Column<bool>(type: "boolean", nullable: false),
                    phase = table.Column<int>(type: "integer", nullable: false),
                    last_entry_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_validity_revalidation", x => x.statistics_item_id);
                    table.CheckConstraint("ck_statistics_validity_revalidation", "generation > 0 AND phase IN (0, 1)");
                    table.ForeignKey(
                        name: "fk_statistics_validity_revalidation_statistics_item_statistics",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statistics_item_config_statistics_item_id",
                schema: "statistics",
                table: "statistics_item_config",
                column: "statistics_item_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statistics_item_config_duration_bounds",
                schema: "statistics",
                table: "statistics_item_config",
                sql: "(min_duration IS NULL OR min_duration > INTERVAL '0 seconds') AND (max_duration IS NULL OR max_duration > INTERVAL '0 seconds') AND (min_duration IS NULL OR max_duration IS NULL OR min_duration <= max_duration)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statistics_item_duration_bounds",
                schema: "statistics",
                table: "statistics_item",
                sql: "(min_duration IS NULL OR min_duration > INTERVAL '0 seconds') AND (max_duration IS NULL OR max_duration > INTERVAL '0 seconds') AND (min_duration IS NULL OR max_duration IS NULL OR min_duration <= max_duration)");

            migrationBuilder.Sql("""
                ALTER TABLE statistics.statistics_entry
                ADD CONSTRAINT ck_statistics_entry_validity
                CHECK ((is_valid AND invalid_reason IS NULL) OR
                    (NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3)))
                NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_contributor_contributor_entry_id",
                schema: "statistics",
                table: "statistics_entry_contributor",
                column: "contributor_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_contributor_statistics_entry_id_contributo",
                schema: "statistics",
                table: "statistics_entry_contributor",
                columns: new[] { "statistics_entry_id", "contributor_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_statistics_validity_revalidation_is_pending_statistics_item",
                schema: "statistics",
                table: "statistics_validity_revalidation",
                columns: new[] { "is_pending", "statistics_item_id" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statistics_entry_contributor",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "statistics_validity_revalidation",
                schema: "statistics");

            migrationBuilder.DropIndex(
                name: "ix_statistics_item_config_statistics_item_id",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_item_config_duration_bounds",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_item_duration_bounds",
                schema: "statistics",
                table: "statistics_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statistics_entry_validity",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "max_duration",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "min_duration",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "max_duration",
                schema: "statistics",
                table: "statistics_item");

            migrationBuilder.DropColumn(
                name: "min_duration",
                schema: "statistics",
                table: "statistics_item");

            migrationBuilder.DropColumn(
                name: "has_complete_contributor_lineage",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "invalid_reason",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "is_valid",
                schema: "statistics",
                table: "statistics_entry");
        }
    }
}
