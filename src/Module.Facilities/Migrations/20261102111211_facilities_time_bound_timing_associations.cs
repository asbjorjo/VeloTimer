using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Facilities.Migrations
{
    /// <inheritdoc />
    public partial class facilities_time_bound_timing_associations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                throw new NotSupportedException("This migration requires PostgreSQL range exclusion constraints.");

            // The migration runs in one transaction. Lock before preflight so an old writer cannot
            // add an ambiguous mapping between validation and backfill.
            migrationBuilder.Sql("""
                LOCK TABLE facility.course_point IN ACCESS EXCLUSIVE MODE;
                DO $$
                DECLARE duplicate_timing_point uuid;
                BEGIN
                    SELECT timing_point INTO duplicate_timing_point
                    FROM facility.course_point
                    WHERE timing_point IS NOT NULL
                    GROUP BY timing_point HAVING count(*) > 1
                    ORDER BY timing_point LIMIT 1;
                    IF duplicate_timing_point IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot backfill timing associations: legacy timing point % is mapped to multiple coursepoints. Resolve legacy duplicates explicitly before retrying.', duplicate_timing_point
                            USING ERRCODE = '23505';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_course_layout_facility_facility_id",
                schema: "facility",
                table: "course_layout");

            migrationBuilder.DropForeignKey(
                name: "fk_course_segment_course_layout_course_layout_id",
                schema: "facility",
                table: "course_segment");

            migrationBuilder.DropForeignKey(
                name: "fk_course_segment_course_point_end_id",
                schema: "facility",
                table: "course_segment");

            migrationBuilder.DropForeignKey(
                name: "fk_course_segment_course_point_start_id",
                schema: "facility",
                table: "course_segment");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:public.btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "course_point_timing_association",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_point_timing_association", x => x.id);
                    table.CheckConstraint("ck_course_point_timing_association_finite_bounds", "(valid_from_utc IS NULL OR isfinite(valid_from_utc)) AND (valid_to_utc IS NULL OR isfinite(valid_to_utc))");
                    table.CheckConstraint("ck_course_point_timing_association_interval", "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc");
                    table.ForeignKey(
                        name: "fk_course_point_timing_association_course_point_course_point_id",
                        column: x => x.course_point_id,
                        principalSchema: "facility",
                        principalTable: "course_point",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Reuse the coursepoint UUID as the association UUID: deterministic, unique in this
            // new table, and no additional UUID-generation extension is needed.
            migrationBuilder.Sql("""
                INSERT INTO facility.course_point_timing_association
                    (id, course_point_id, timing_point_id, valid_from_utc, valid_to_utc)
                SELECT id, id, timing_point, NULL, NULL
                FROM facility.course_point WHERE timing_point IS NOT NULL;

                ALTER TABLE facility.course_point_timing_association
                    ADD CONSTRAINT ex_timing_association_timing_point
                    EXCLUDE USING gist
                    (timing_point_id public.gist_uuid_ops WITH =,
                     tstzrange(valid_from_utc, valid_to_utc, '[)') WITH &&)
                    DEFERRABLE INITIALLY IMMEDIATE;

                ALTER TABLE facility.course_point_timing_association
                    ADD CONSTRAINT ex_timing_association_course_point
                    EXCLUDE USING gist
                    (course_point_id public.gist_uuid_ops WITH =,
                     tstzrange(valid_from_utc, valid_to_utc, '[)') WITH &&)
                    DEFERRABLE INITIALLY IMMEDIATE;
                """);

            migrationBuilder.DropColumn(
                name: "timing_point",
                schema: "facility",
                table: "course_point");

            migrationBuilder.CreateIndex(
                name: "ix_course_point_timing_association_course_point_id_valid_from_",
                schema: "facility",
                table: "course_point_timing_association",
                columns: new[] { "course_point_id", "valid_from_utc", "valid_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_course_point_timing_association_timing_point_id_valid_from_",
                schema: "facility",
                table: "course_point_timing_association",
                columns: new[] { "timing_point_id", "valid_from_utc", "valid_to_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_course_layout_facility_facility_id",
                schema: "facility",
                table: "course_layout",
                column: "facility_id",
                principalSchema: "facility",
                principalTable: "facility",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_course_segment_course_layout_course_layout_id",
                schema: "facility",
                table: "course_segment",
                column: "course_layout_id",
                principalSchema: "facility",
                principalTable: "course_layout",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_course_segment_course_point_end_id",
                schema: "facility",
                table: "course_segment",
                column: "end_id",
                principalSchema: "facility",
                principalTable: "course_point",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_course_segment_course_point_start_id",
                schema: "facility",
                table: "course_segment",
                column: "start_id",
                principalSchema: "facility",
                principalTable: "course_point",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Time-bound association history cannot be represented by the legacy mutable column. " +
                "Automatic downgrade is refused to avoid deleting history; use an approved recovery plan.");
        }
    }
}
