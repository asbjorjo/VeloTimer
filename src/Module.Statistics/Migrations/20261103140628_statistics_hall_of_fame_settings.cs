using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_hall_of_fame_settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hall_of_fame_settings",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    sections_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hall_of_fame_settings", x => x.id);
                    table.CheckConstraint("ck_hall_of_fame_settings_sections_array", "jsonb_typeof(sections_json) = 'array'");
                    table.CheckConstraint("ck_hall_of_fame_settings_singleton", "id = 1");
                    table.CheckConstraint("ck_hall_of_fame_settings_version_nonnegative", "version >= 0");
                });

            migrationBuilder.InsertData(
                schema: "statistics",
                table: "hall_of_fame_settings",
                columns: new[] { "id", "sections_json", "version" },
                values: new object[] { 1, "[]", 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hall_of_fame_settings",
                schema: "statistics");

        }
    }
}
