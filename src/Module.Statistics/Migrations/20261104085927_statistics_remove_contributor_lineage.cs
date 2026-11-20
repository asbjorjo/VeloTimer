using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_remove_contributor_lineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Metadata only: restart every item, including idle/missing checkpoints and
            // cross-item dependencies. Never scan or reclassify historical entries here.
            // Starting at Simple also covers items not yet visited by rollout bootstrap.
            migrationBuilder.Sql("""
                INSERT INTO statistics.statistics_validity_revalidation AS checkpoint
                    (statistics_item_id, generation, is_pending, phase, last_entry_id)
                SELECT id, 1, TRUE, 0, NULL
                FROM statistics.statistics_item
                ORDER BY id
                ON CONFLICT (statistics_item_id) DO UPDATE
                SET generation = checkpoint.generation + 1,
                    is_pending = TRUE,
                    phase = 0,
                    last_entry_id = NULL;
                """);

            migrationBuilder.DropTable(
                name: "statistics_entry_contributor",
                schema: "statistics");

            migrationBuilder.DropColumn(
                name: "has_complete_contributor_lineage",
                schema: "statistics",
                table: "statistics_entry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema compatibility only: deleted provenance cannot be reconstructed.
            // Leave all completeness flags false and links empty; do not undo scheduling.
            migrationBuilder.AddColumn<bool>(
                name: "has_complete_contributor_lineage",
                schema: "statistics",
                table: "statistics_entry",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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
        }
    }
}
