using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Agent.Migrations
{
    /// <inheritdoc />
    public partial class agent_initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "agent");

            migrationBuilder.CreateTable(
                name: "passing",
                schema: "agent",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    transponder_type = table.Column<string>(type: "text", nullable: false),
                    transponder_id = table.Column<string>(type: "text", nullable: false),
                    loop_id = table.Column<string>(type: "text", nullable: false),
                    low_battery = table.Column<bool>(type: "boolean", nullable: false),
                    low_strength = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passing", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passing",
                schema: "agent");
        }
    }
}
