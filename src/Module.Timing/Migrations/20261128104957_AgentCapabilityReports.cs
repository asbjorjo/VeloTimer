using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class AgentCapabilityReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_capabilities",
                schema: "timing",
                table: "installation",
                type: "jsonb",
                nullable: false,
                // Existing installations have not reported yet: an empty capability list.
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "agent_type",
                schema: "timing",
                table: "installation",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_version",
                schema: "timing",
                table: "installation",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "capabilities_reported_at",
                schema: "timing",
                table: "installation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "control_protocol_version",
                schema: "timing",
                table: "installation",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_capabilities",
                schema: "timing",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "agent_type",
                schema: "timing",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "agent_version",
                schema: "timing",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "capabilities_reported_at",
                schema: "timing",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "control_protocol_version",
                schema: "timing",
                table: "installation");
        }
    }
}
