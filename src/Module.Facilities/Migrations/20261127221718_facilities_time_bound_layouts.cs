using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Facilities.Migrations
{
    /// <inheritdoc />
    public partial class facilities_time_bound_layouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_course_segment_course_layout_course_layout_id",
                schema: "facility",
                table: "course_segment");

            migrationBuilder.RenameColumn(
                name: "course_layout_id",
                schema: "facility",
                table: "course_segment",
                newName: "course_layout_revision_id");

            migrationBuilder.RenameIndex(
                name: "ix_course_segment_course_layout_id",
                schema: "facility",
                table: "course_segment",
                newName: "ix_course_segment_course_layout_revision_id");

            migrationBuilder.CreateTable(
                name: "course_layout_revision",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_layout_revision", x => x.id);
                    table.CheckConstraint("ck_course_layout_revision_finite_bounds", "(valid_from_utc IS NULL OR isfinite(valid_from_utc)) AND (valid_to_utc IS NULL OR isfinite(valid_to_utc))");
                    table.CheckConstraint("ck_course_layout_revision_interval", "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc");
                    table.ForeignKey(
                        name: "fk_course_layout_revision_course_layout_course_layout_id",
                        column: x => x.course_layout_id,
                        principalSchema: "facility",
                        principalTable: "course_layout",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "facility_layout_assignment",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facility_layout_assignment", x => x.id);
                    table.CheckConstraint("ck_facility_layout_assignment_finite_bounds", "(valid_from_utc IS NULL OR isfinite(valid_from_utc)) AND (valid_to_utc IS NULL OR isfinite(valid_to_utc))");
                    table.CheckConstraint("ck_facility_layout_assignment_interval", "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc");
                    table.ForeignKey(
                        name: "fk_facility_layout_assignment_course_layout_course_layout_id",
                        column: x => x.course_layout_id,
                        principalSchema: "facility",
                        principalTable: "course_layout",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_facility_layout_assignment_facility_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "facility",
                        principalTable: "facility",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_course_layout_revision_course_layout_id_valid_from_utc_vali",
                schema: "facility",
                table: "course_layout_revision",
                columns: new[] { "course_layout_id", "valid_from_utc", "valid_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_facility_layout_assignment_course_layout_id_valid_from_utc_",
                schema: "facility",
                table: "facility_layout_assignment",
                columns: new[] { "course_layout_id", "valid_from_utc", "valid_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_facility_layout_assignment_facility_id_valid_from_utc_valid",
                schema: "facility",
                table: "facility_layout_assignment",
                columns: new[] { "facility_id", "valid_from_utc", "valid_to_utc" });

            // Alpha policy: existing layouts become one unbounded revision and an unbounded assignment each,
            // reusing the layout UUID for both so segments keep pointing at the right rows. Statistics
            // recomputes from passing facts, so nothing derived needs migrating.
            migrationBuilder.Sql("""
                INSERT INTO facility.course_layout_revision (id, course_layout_id, valid_from_utc, valid_to_utc)
                SELECT id, id, NULL, NULL FROM facility.course_layout;

                INSERT INTO facility.facility_layout_assignment (id, facility_id, course_layout_id, valid_from_utc, valid_to_utc)
                SELECT id, facility_id, id, NULL, NULL FROM facility.course_layout;

                ALTER TABLE facility.course_layout_revision
                    ADD CONSTRAINT ex_course_layout_revision_interval
                    EXCLUDE USING gist
                    (course_layout_id public.gist_uuid_ops WITH =,
                     tstzrange(valid_from_utc, valid_to_utc, '[)') WITH &&)
                    DEFERRABLE INITIALLY IMMEDIATE;

                ALTER TABLE facility.facility_layout_assignment
                    ADD CONSTRAINT ex_facility_layout_assignment_interval
                    EXCLUDE USING gist
                    (course_layout_id public.gist_uuid_ops WITH =,
                     tstzrange(valid_from_utc, valid_to_utc, '[)') WITH &&)
                    DEFERRABLE INITIALLY IMMEDIATE;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_course_segment_course_layout_revision_course_layout_revisio",
                schema: "facility",
                table: "course_segment",
                column: "course_layout_revision_id",
                principalSchema: "facility",
                principalTable: "course_layout_revision",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Dated layout revisions and assignments cannot be represented by the legacy in-place layout. " +
                "Automatic downgrade is refused to avoid deleting history.");
        }
    }
}
