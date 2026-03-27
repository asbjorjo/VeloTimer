using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "statistics");

            migrationBuilder.CreateTable(
                name: "sample",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_start_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_end_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    distance = table.Column<double>(type: "double precision", nullable: false),
                    speed = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statistics_item",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    distance = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stats_profile",
                schema: "statistics",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_name = table.Column<string>(type: "text", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stats_profile", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "simple_statistics_item",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_start = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_end = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simple_statistics_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_simple_statistics_item_statistics_item_statistics_item_id",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "statistics_entry",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stats_profile_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    speed = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_statistics_entry_statistics_item_statistics_item_id",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_statistics_entry_stats_profile_stats_profile_user_id",
                        column: x => x.stats_profile_user_id,
                        principalSchema: "statistics",
                        principalTable: "stats_profile",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_simple_statistics_item_statistics_item_id",
                schema: "statistics",
                table: "simple_statistics_item",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_statistics_item_id",
                schema: "statistics",
                table: "statistics_entry",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_stats_profile_user_id",
                schema: "statistics",
                table: "statistics_entry",
                column: "stats_profile_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sample",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "simple_statistics_item",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "statistics_entry",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "statistics_item",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "stats_profile",
                schema: "statistics");
        }
    }
}
