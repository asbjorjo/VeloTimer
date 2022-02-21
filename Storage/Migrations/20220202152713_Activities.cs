using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VeloTime.Storage.Migrations
{
    public partial class Activities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<long>(type: "bigint", nullable: true),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_track_track_id",
                        column: x => x.track_id,
                        principalSchema: "velotimer",
                        principalTable: "track",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_activity_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "velotimer",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_track_id",
                schema: "velotimer",
                table: "activity",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_transponder_id",
                schema: "velotimer",
                table: "activity",
                column: "transponder_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_activity_id",
                schema: "velotimer",
                table: "session",
                column: "activity_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "activity",
                schema: "velotimer");
        }
    }
}
