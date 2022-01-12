using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTimerWeb.Api.Migrations.Identity
{
    public partial class IdentityServerKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keys",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    use = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    algorithm = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_x509_certificate = table.Column<bool>(type: "bit", nullable: false),
                    data_protected = table.Column<bool>(type: "bit", nullable: false),
                    data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_persisted_grants_consumed_time",
                schema: "identity",
                table: "persisted_grants",
                column: "consumed_time");

            migrationBuilder.CreateIndex(
                name: "ix_keys_use",
                schema: "identity",
                table: "keys",
                column: "use");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keys",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_persisted_grants_consumed_time",
                schema: "identity",
                table: "persisted_grants");
        }
    }
}
