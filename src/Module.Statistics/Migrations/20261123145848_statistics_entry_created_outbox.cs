using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_entry_created_outbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "entries_created_at_utc",
                schema: "statistics",
                table: "sample_complete_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.DropIndex(
                name: "ix_sample_complete_outbox_transponder_id_id",
                schema: "statistics", table: "sample_complete_outbox");
            migrationBuilder.CreateIndex(
                name: "ix_sample_complete_outbox_transponder_id_id",
                schema: "statistics", table: "sample_complete_outbox",
                columns: new[] { "transponder_id", "id" },
                filter: "delivered_at_utc IS NULL OR entries_created_at_utc IS NULL");

            migrationBuilder.CreateTable(
                name: "entry_created_outbox",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    single = table.Column<bool>(type: "boolean", nullable: false),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_created_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entry_created_outbox_entry_id",
                schema: "statistics",
                table: "entry_created_outbox",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entry_created_outbox_transponder_id_id",
                schema: "statistics",
                table: "entry_created_outbox",
                columns: new[] { "transponder_id", "id" },
                filter: "delivered_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.entry_created_outbox) THEN
                        RAISE EXCEPTION 'Cannot drop EntryCreated outbox while retained rows exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "entry_created_outbox",
                schema: "statistics");

            migrationBuilder.DropIndex(
                name: "ix_sample_complete_outbox_transponder_id_id",
                schema: "statistics", table: "sample_complete_outbox");
            migrationBuilder.DropColumn(
                name: "entries_created_at_utc",
                schema: "statistics",
                table: "sample_complete_outbox");
            migrationBuilder.CreateIndex(
                name: "ix_sample_complete_outbox_transponder_id_id",
                schema: "statistics", table: "sample_complete_outbox",
                columns: new[] { "transponder_id", "id" },
                filter: "delivered_at_utc IS NULL");
        }
    }
}
