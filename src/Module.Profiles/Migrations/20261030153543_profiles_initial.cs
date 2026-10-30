using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Profiles.Migrations
{
    /// <inheritdoc />
    public partial class profiles_initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "profiles");

            migrationBuilder.CreateTable(
                name: "language_preference",
                schema: "profiles",
                columns: table => new
                {
                    issuer = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    subject = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    language = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_language_preference", x => new { x.issuer, x.subject });
                    table.CheckConstraint("ck_language_preference_language", "language IN ('en', 'nb-NO')");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "language_preference",
                schema: "profiles");
        }
    }
}
