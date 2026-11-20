using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Profiles.Migrations
{
    /// <inheritdoc />
    public partial class display_preference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "display_preference",
                schema: "profiles",
                columns: table => new
                {
                    issuer = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    subject = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    distance_unit = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    speed_unit = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    time_mode = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    time_zone_id = table.Column<string>(type: "text", nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_display_preference", x => new { x.issuer, x.subject });
                    table.CheckConstraint("ck_display_preference_distance_unit", "distance_unit IN ('m', 'ft')");
                    table.CheckConstraint("ck_display_preference_speed_unit", "speed_unit IN ('km/h', 'mph', 'm/s')");
                    table.CheckConstraint("ck_display_preference_time_mode", "time_mode IN ('browser', 'utc', 'timeZone')");
                    table.CheckConstraint("ck_display_preference_time_mode_zone", "(time_mode = 'timeZone' AND time_zone_id IS NOT NULL) OR (time_mode IN ('browser', 'utc') AND time_zone_id IS NULL)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "display_preference",
                schema: "profiles");
        }
    }
}
