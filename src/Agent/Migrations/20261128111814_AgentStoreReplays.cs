using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Agent.Migrations
{
    /// <inheritdoc />
    public partial class AgentStoreReplays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "replay_request",
                schema: "agent",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    loop_ids = table.Column<string>(type: "text", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    read = table.Column<int>(type: "integer", nullable: false),
                    published = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_replay_request", x => x.request_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passing_time_id",
                schema: "agent",
                table: "passing",
                columns: new[] { "time", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_replay_request_state",
                schema: "agent",
                table: "replay_request",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "replay_request",
                schema: "agent");

            migrationBuilder.DropIndex(
                name: "ix_passing_time_id",
                schema: "agent",
                table: "passing");
        }
    }
}
