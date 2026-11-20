using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Agent.Migrations
{
    /// <inheritdoc />
    public partial class AddPassingLowHits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "low_hits",
                schema: "agent",
                table: "passing",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "low_hits",
                schema: "agent",
                table: "passing");
        }
    }
}
