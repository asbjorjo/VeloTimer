using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSeenToInstallation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_seen",
                schema: "timing",
                table: "installation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transponder_owner",
                schema: "timing",
                columns: table => new
                {
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owned_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    owned_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_owner", x => new { x.transponder_id, x.owner_id });
                    table.ForeignKey(
                        name: "fk_transponder_owner_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "timing",
                        principalTable: "transponder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transponder_owner",
                schema: "timing");

            migrationBuilder.DropColumn(
                name: "last_seen",
                schema: "timing",
                table: "installation");
        }
    }
}
