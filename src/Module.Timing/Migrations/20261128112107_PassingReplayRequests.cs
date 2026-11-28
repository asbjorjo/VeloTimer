using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class PassingReplayRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passing_replay_request",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    loop_ids = table.Column<string>(type: "jsonb", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    read = table.Column<int>(type: "integer", nullable: false),
                    published = table.Column<int>(type: "integer", nullable: false),
                    new_in_agent_store = table.Column<int>(type: "integer", nullable: false),
                    recorded = table.Column<int>(type: "integer", nullable: false),
                    duplicate = table.Column<int>(type: "integer", nullable: false),
                    conflict = table.Column<int>(type: "integer", nullable: false),
                    rejected = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passing_replay_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_passing_replay_request_installation_installation_id",
                        column: x => x.installation_id,
                        principalSchema: "timing",
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passing_replay_request_installation_id_created_at",
                schema: "timing",
                table: "passing_replay_request",
                columns: new[] { "installation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_passing_replay_request_state_expires_at",
                schema: "timing",
                table: "passing_replay_request",
                columns: new[] { "state", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passing_replay_request",
                schema: "timing");
        }
    }
}
