using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class PassingIndices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source",
                schema: "velotimer",
                table: "passings",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "ix_passings_source",
                schema: "velotimer",
                table: "passings",
                column: "source");

            migrationBuilder.CreateIndex(
                name: "ix_passings_time",
                schema: "velotimer",
                table: "passings",
                column: "time");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_passings_source",
                schema: "velotimer",
                table: "passings");

            migrationBuilder.DropIndex(
                name: "ix_passings_time",
                schema: "velotimer",
                table: "passings");

            migrationBuilder.AlterColumn<string>(
                name: "source",
                schema: "velotimer",
                table: "passings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
