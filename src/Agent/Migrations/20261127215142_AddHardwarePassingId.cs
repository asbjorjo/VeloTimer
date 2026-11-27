using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Agent.Migrations
{
    /// <inheritdoc />
    public partial class AddHardwarePassingId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hardware_passing_id",
                schema: "agent",
                table: "passing",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hardware_passing_id",
                schema: "agent",
                table: "passing");
        }
    }
}
