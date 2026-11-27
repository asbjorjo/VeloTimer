using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class TransponderTypeAndRejectedPassings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Issue #139: transponders are typed by chip (MYLAPS ProChip, discriminator 1, unchanged) rather than by
            // decoder, so existing rows keep their ownerships. Rows of type 1 whose ID cannot be a ProChip ID
            // (non-numeric, or at or below the 0x6000000 code offset) were never valid; delete them and their passings.
            const string invalid = """
                SELECT id FROM timing.transponder
                WHERE system = 1
                  AND CASE WHEN system_id ~ '^[0-9]{1,18}$' THEN system_id::bigint <= 100663296 ELSE true END
                """;
            migrationBuilder.Sql($"DELETE FROM timing.passing_conflict WHERE transponder_id IN ({invalid});");
            migrationBuilder.Sql($"DELETE FROM timing.passing WHERE transponder_id IN ({invalid});");
            migrationBuilder.Sql($"DELETE FROM timing.transponder WHERE id IN ({invalid});");

            migrationBuilder.RenameColumn(
                name: "system",
                schema: "timing",
                table: "transponder",
                newName: "type");

            migrationBuilder.RenameIndex(
                name: "ix_transponder_system_system_id",
                schema: "timing",
                table: "transponder",
                newName: "ix_transponder_type_system_id");

            migrationBuilder.CreateTable(
                name: "rejected_passing",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    source_passing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    transponder_type = table.Column<string>(type: "text", nullable: false),
                    transponder_id = table.Column<string>(type: "text", nullable: false),
                    loop_id = table.Column<string>(type: "text", nullable: false),
                    low_battery = table.Column<bool>(type: "boolean", nullable: false),
                    low_strength = table.Column<bool>(type: "boolean", nullable: false),
                    low_hits = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rejected_passing", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rejected_passing_agent_id_source_passing_id",
                schema: "timing",
                table: "rejected_passing",
                columns: new[] { "agent_id", "source_passing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rejected_passing_last_seen_at",
                schema: "timing",
                table: "rejected_passing",
                column: "last_seen_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rejected_passing",
                schema: "timing");

            migrationBuilder.RenameColumn(
                name: "type",
                schema: "timing",
                table: "transponder",
                newName: "system");

            migrationBuilder.RenameIndex(
                name: "ix_transponder_type_system_id",
                schema: "timing",
                table: "transponder",
                newName: "ix_transponder_system_system_id");
        }
    }
}
