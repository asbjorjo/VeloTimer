using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTime.Storage.Migrations
{
    public partial class PrivateProfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                schema: "velotimer",
                table: "rider",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_public",
                schema: "velotimer",
                table: "rider");
        }
    }
}
