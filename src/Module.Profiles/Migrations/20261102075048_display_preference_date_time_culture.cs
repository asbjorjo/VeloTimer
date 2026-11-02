using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Profiles.Migrations
{
    /// <inheritdoc />
    public partial class display_preference_date_time_culture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "date_time_culture",
                schema: "profiles",
                table: "display_preference",
                type: "text",
                nullable: true,
                collation: "C");

            migrationBuilder.AddCheckConstraint(
                name: "ck_display_preference_date_time_culture",
                schema: "profiles",
                table: "display_preference",
                sql: "date_time_culture IS NULL OR date_time_culture IN ('browser', 'en', 'nb-NO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_display_preference_date_time_culture",
                schema: "profiles",
                table: "display_preference");

            migrationBuilder.DropColumn(
                name: "date_time_culture",
                schema: "profiles",
                table: "display_preference");
        }
    }
}
