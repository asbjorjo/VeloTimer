using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class InstallationFacilityAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_facility_assignment",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valid_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_facility_assignment", x => x.id);
                    table.CheckConstraint("ck_installation_facility_assignment_interval", "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc");
                    table.ForeignKey(
                        name: "fk_installation_facility_assignment_installation_installation_",
                        column: x => x.installation_id,
                        principalSchema: "timing",
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_installation_facility_assignment_facility_id",
                schema: "timing",
                table: "installation_facility_assignment",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_installation_facility_assignment_installation_id_valid_from",
                schema: "timing",
                table: "installation_facility_assignment",
                columns: new[] { "installation_id", "valid_from_utc" },
                unique: true);

            // Every current facility becomes an unbounded assignment (replay-safe plan Phase 4, C2). Timing has
            // no passings older than Phase 1's reset, so covering all history is exact.
            migrationBuilder.Sql("""
                INSERT INTO timing.installation_facility_assignment (id, installation_id, facility_id, valid_from_utc, valid_to_utc)
                SELECT gen_random_uuid(), id, facility, NULL, NULL FROM timing.installation
                WHERE facility <> '00000000-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_facility_assignment",
                schema: "timing");
        }
    }
}
