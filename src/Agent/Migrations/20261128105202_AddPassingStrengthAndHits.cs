using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Agent.Migrations
{
    /// <inheritdoc />
    public partial class AddPassingStrengthAndHits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "hits",
                schema: "agent",
                table: "passing",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "strength",
                schema: "agent",
                table: "passing",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hits",
                schema: "agent",
                table: "passing");

            migrationBuilder.DropColumn(
                name: "strength",
                schema: "agent",
                table: "passing");
        }
    }
}
