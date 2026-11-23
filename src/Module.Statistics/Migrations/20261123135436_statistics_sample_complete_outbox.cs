using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_sample_complete_outbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sample_complete_outbox",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    course_point_start = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_end = table.Column<Guid>(type: "uuid", nullable: false),
                    distance = table.Column<double>(type: "double precision", nullable: false),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_complete_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sample_complete_outbox_sample_id",
                schema: "statistics",
                table: "sample_complete_outbox",
                column: "sample_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_complete_outbox_transponder_id_id",
                schema: "statistics",
                table: "sample_complete_outbox",
                columns: new[] { "transponder_id", "id" },
                filter: "delivered_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.sample_complete_outbox) THEN
                        RAISE EXCEPTION 'Cannot drop SampleComplete outbox while retained rows exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "sample_complete_outbox",
                schema: "statistics");
        }
    }
}
