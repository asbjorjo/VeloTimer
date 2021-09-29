using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Data.Migrations
{
    public partial class RenameTransponderLabel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Transponders",
                newName: "Label");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Label",
                table: "Transponders",
                newName: "Name");
        }
    }
}
