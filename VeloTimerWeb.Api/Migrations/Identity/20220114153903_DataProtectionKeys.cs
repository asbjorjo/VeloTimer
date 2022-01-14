using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTimerWeb.Api.Migrations.Identity
{
    public partial class DataProtectionKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dataprotection_keys",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    friendly_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dataprotection_keys", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dataprotection_keys",
                schema: "identity");
        }
    }
}
