using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class AlmostFreshStart : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_passings_timing_loops_loop_id",
                schema: "velotimer",
                table: "passings");

            migrationBuilder.DropForeignKey(
                name: "fk_passings_transponders_transponder_id",
                schema: "velotimer",
                table: "passings");

            migrationBuilder.DropForeignKey(
                name: "fk_timing_loops_tracks_track_id",
                schema: "velotimer",
                table: "timing_loops");

            migrationBuilder.DropForeignKey(
                name: "fk_transponders_transponder_type_timing_system",
                schema: "velotimer",
                table: "transponders");

            migrationBuilder.DropForeignKey(
                name: "fk_transponders_ownerships_riders_owner_id",
                schema: "velotimer",
                table: "transponders_ownerships");

            migrationBuilder.DropForeignKey(
                name: "fk_transponders_ownerships_transponders_transponder_id",
                schema: "velotimer",
                table: "transponders_ownerships");

            migrationBuilder.DropTable(
                name: "intermediate",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "segment_runs",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_names",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "segments",
                schema: "velotimer");

            migrationBuilder.DropPrimaryKey(
                name: "pk_transponders_ownerships",
                schema: "velotimer",
                table: "transponders_ownerships");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_transponders_timing_system_system_id",
                schema: "velotimer",
                table: "transponders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_transponders",
                schema: "velotimer",
                table: "transponders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_tracks",
                schema: "velotimer",
                table: "tracks");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_timing_loops_track_id_loop_id",
                schema: "velotimer",
                table: "timing_loops");

            migrationBuilder.DropPrimaryKey(
                name: "pk_timing_loops",
                schema: "velotimer",
                table: "timing_loops");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_riders_user_id",
                schema: "velotimer",
                table: "riders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_riders",
                schema: "velotimer",
                table: "riders");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_passings_time_transponder_id_loop_id",
                schema: "velotimer",
                table: "passings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_passings",
                schema: "velotimer",
                table: "passings");

            migrationBuilder.RenameTable(
                name: "transponders_ownerships",
                schema: "velotimer",
                newName: "transponder_ownership",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "transponders",
                schema: "velotimer",
                newName: "transponder",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "tracks",
                schema: "velotimer",
                newName: "track",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "timing_loops",
                schema: "velotimer",
                newName: "timing_loop",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "riders",
                schema: "velotimer",
                newName: "rider",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "passings",
                schema: "velotimer",
                newName: "passing",
                newSchema: "velotimer");

            migrationBuilder.RenameIndex(
                name: "ix_transponders_ownerships_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                newName: "ix_transponder_ownership_transponder_id");

            migrationBuilder.RenameIndex(
                name: "ix_transponders_ownerships_owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                newName: "ix_transponder_ownership_owner_id");

            migrationBuilder.RenameColumn(
                name: "source",
                schema: "velotimer",
                table: "passing",
                newName: "source_id");

            migrationBuilder.RenameIndex(
                name: "ix_passings_transponder_id",
                schema: "velotimer",
                table: "passing",
                newName: "ix_passing_transponder_id");

            migrationBuilder.RenameIndex(
                name: "ix_passings_time",
                schema: "velotimer",
                table: "passing",
                newName: "ix_passing_time");

            migrationBuilder.RenameIndex(
                name: "ix_passings_source",
                schema: "velotimer",
                table: "passing",
                newName: "ix_passing_source_id");

            migrationBuilder.RenameIndex(
                name: "ix_passings_loop_id",
                schema: "velotimer",
                table: "passing",
                newName: "ix_passing_loop_id");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateTime>(
                name: "owned_until",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "owned_from",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<int>(
                name: "loop_id",
                schema: "velotimer",
                table: "timing_loop",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateTime>(
                name: "time",
                schema: "velotimer",
                table: "passing",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AddPrimaryKey(
                name: "pk_transponder_ownership",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_transponder_timing_system_system_id",
                schema: "velotimer",
                table: "transponder",
                columns: new[] { "timing_system", "system_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_transponder",
                schema: "velotimer",
                table: "transponder",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_track",
                schema: "velotimer",
                table: "track",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_timing_loop_track_id_loop_id",
                schema: "velotimer",
                table: "timing_loop",
                columns: new[] { "track_id", "loop_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_timing_loop",
                schema: "velotimer",
                table: "timing_loop",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_rider_user_id",
                schema: "velotimer",
                table: "rider",
                column: "user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_rider",
                schema: "velotimer",
                table: "rider",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_passing_time_transponder_id_loop_id",
                schema: "velotimer",
                table: "passing",
                columns: new[] { "time", "transponder_id", "loop_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_passing",
                schema: "velotimer",
                table: "passing",
                column: "id");

            migrationBuilder.CreateTable(
                name: "track_segment",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    start_id = table.Column<long>(type: "bigint", nullable: true),
                    end_id = table.Column<long>(type: "bigint", nullable: true),
                    track_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_segment", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_segment_timing_loop_end_id",
                        column: x => x.end_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loop",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_segment_timing_loop_start_id",
                        column: x => x.start_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loop",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_segment_track_track_id",
                        column: x => x.track_id,
                        principalSchema: "velotimer",
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_end_id",
                schema: "velotimer",
                table: "track_segment",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_start_id",
                schema: "velotimer",
                table: "track_segment",
                column: "start_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_track_id",
                schema: "velotimer",
                table: "track_segment",
                column: "track_id");

            migrationBuilder.AddForeignKey(
                name: "fk_passing_timing_loop_loop_id",
                schema: "velotimer",
                table: "passing",
                column: "loop_id",
                principalSchema: "velotimer",
                principalTable: "timing_loop",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "passing",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_timing_loop_track_track_id",
                schema: "velotimer",
                table: "timing_loop",
                column: "track_id",
                principalSchema: "velotimer",
                principalTable: "track",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_transponder_type_timing_system",
                schema: "velotimer",
                table: "transponder",
                column: "timing_system",
                principalSchema: "velotimer",
                principalTable: "transponder_type",
                principalColumn: "system",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_ownership_rider_owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "owner_id",
                principalSchema: "velotimer",
                principalTable: "rider",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_ownership_transponder_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_passing_timing_loop_loop_id",
                schema: "velotimer",
                table: "passing");

            migrationBuilder.DropForeignKey(
                name: "fk_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "passing");

            migrationBuilder.DropForeignKey(
                name: "fk_timing_loop_track_track_id",
                schema: "velotimer",
                table: "timing_loop");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_transponder_type_timing_system",
                schema: "velotimer",
                table: "transponder");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_ownership_rider_owner_id",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_ownership_transponder_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.DropTable(
                name: "track_segment",
                schema: "velotimer");

            migrationBuilder.DropPrimaryKey(
                name: "pk_transponder_ownership",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_transponder_timing_system_system_id",
                schema: "velotimer",
                table: "transponder");

            migrationBuilder.DropPrimaryKey(
                name: "pk_transponder",
                schema: "velotimer",
                table: "transponder");

            migrationBuilder.DropPrimaryKey(
                name: "pk_track",
                schema: "velotimer",
                table: "track");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_timing_loop_track_id_loop_id",
                schema: "velotimer",
                table: "timing_loop");

            migrationBuilder.DropPrimaryKey(
                name: "pk_timing_loop",
                schema: "velotimer",
                table: "timing_loop");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_rider_user_id",
                schema: "velotimer",
                table: "rider");

            migrationBuilder.DropPrimaryKey(
                name: "pk_rider",
                schema: "velotimer",
                table: "rider");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_passing_time_transponder_id_loop_id",
                schema: "velotimer",
                table: "passing");

            migrationBuilder.DropPrimaryKey(
                name: "pk_passing",
                schema: "velotimer",
                table: "passing");

            migrationBuilder.RenameTable(
                name: "transponder_ownership",
                schema: "velotimer",
                newName: "transponders_ownerships",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "transponder",
                schema: "velotimer",
                newName: "transponders",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "track",
                schema: "velotimer",
                newName: "tracks",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "timing_loop",
                schema: "velotimer",
                newName: "timing_loops",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "rider",
                schema: "velotimer",
                newName: "riders",
                newSchema: "velotimer");

            migrationBuilder.RenameTable(
                name: "passing",
                schema: "velotimer",
                newName: "passings",
                newSchema: "velotimer");

            migrationBuilder.RenameIndex(
                name: "ix_transponder_ownership_transponder_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                newName: "ix_transponders_ownerships_transponder_id");

            migrationBuilder.RenameIndex(
                name: "ix_transponder_ownership_owner_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                newName: "ix_transponders_ownerships_owner_id");

            migrationBuilder.RenameColumn(
                name: "source_id",
                schema: "velotimer",
                table: "passings",
                newName: "source");

            migrationBuilder.RenameIndex(
                name: "ix_passing_transponder_id",
                schema: "velotimer",
                table: "passings",
                newName: "ix_passings_transponder_id");

            migrationBuilder.RenameIndex(
                name: "ix_passing_time",
                schema: "velotimer",
                table: "passings",
                newName: "ix_passings_time");

            migrationBuilder.RenameIndex(
                name: "ix_passing_source_id",
                schema: "velotimer",
                table: "passings",
                newName: "ix_passings_source");

            migrationBuilder.RenameIndex(
                name: "ix_passing_loop_id",
                schema: "velotimer",
                table: "passings",
                newName: "ix_passings_loop_id");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "owner_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "owned_until",
                schema: "velotimer",
                table: "transponders_ownerships",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "owned_from",
                schema: "velotimer",
                table: "transponders_ownerships",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<long>(
                name: "loop_id",
                schema: "velotimer",
                table: "timing_loops",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "time",
                schema: "velotimer",
                table: "passings",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddPrimaryKey(
                name: "pk_transponders_ownerships",
                schema: "velotimer",
                table: "transponders_ownerships",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_transponders_timing_system_system_id",
                schema: "velotimer",
                table: "transponders",
                columns: new[] { "timing_system", "system_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_transponders",
                schema: "velotimer",
                table: "transponders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_tracks",
                schema: "velotimer",
                table: "tracks",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_timing_loops_track_id_loop_id",
                schema: "velotimer",
                table: "timing_loops",
                columns: new[] { "track_id", "loop_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_timing_loops",
                schema: "velotimer",
                table: "timing_loops",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_riders_user_id",
                schema: "velotimer",
                table: "riders",
                column: "user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_riders",
                schema: "velotimer",
                table: "riders",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_passings_time_transponder_id_loop_id",
                schema: "velotimer",
                table: "passings",
                columns: new[] { "time", "transponder_id", "loop_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_passings",
                schema: "velotimer",
                table: "passings",
                column: "id");

            migrationBuilder.CreateTable(
                name: "segments",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    display_intermediates = table.Column<bool>(type: "bit", nullable: false),
                    end_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    max_time = table.Column<long>(type: "bigint", nullable: false),
                    min_time = table.Column<long>(type: "bigint", nullable: false),
                    require_intermediates = table.Column<bool>(type: "bit", nullable: false),
                    start_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_segments", x => x.id);
                    table.ForeignKey(
                        name: "fk_segments_timing_loops_end_id",
                        column: x => x.end_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_segments_timing_loops_start_id",
                        column: x => x.start_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transponder_names",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_names", x => x.id);
                    table.ForeignKey(
                        name: "fk_transponder_names_transponders_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intermediate",
                schema: "velotimer",
                columns: table => new
                {
                    segment_id = table.Column<long>(type: "bigint", nullable: false),
                    loop_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intermediate", x => new { x.segment_id, x.loop_id });
                    table.ForeignKey(
                        name: "fk_intermediate_segments_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "velotimer",
                        principalTable: "segments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_intermediate_timing_loops_loop_id",
                        column: x => x.loop_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "segment_runs",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    end_id = table.Column<long>(type: "bigint", nullable: false),
                    segment_id = table.Column<long>(type: "bigint", nullable: false),
                    start_id = table.Column<long>(type: "bigint", nullable: false),
                    time = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_segment_runs", x => x.id);
                    table.UniqueConstraint("ak_segment_runs_segment_id_start_id_end_id", x => new { x.segment_id, x.start_id, x.end_id });
                    table.ForeignKey(
                        name: "fk_segment_runs_passings_end_id",
                        column: x => x.end_id,
                        principalSchema: "velotimer",
                        principalTable: "passings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_segment_runs_passings_start_id",
                        column: x => x.start_id,
                        principalSchema: "velotimer",
                        principalTable: "passings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_segment_runs_segments_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "velotimer",
                        principalTable: "segments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_intermediate_loop_id",
                schema: "velotimer",
                table: "intermediate",
                column: "loop_id");

            migrationBuilder.CreateIndex(
                name: "ix_segment_runs_end_id",
                schema: "velotimer",
                table: "segment_runs",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_segment_runs_segment_id_time_start_id_end_id",
                schema: "velotimer",
                table: "segment_runs",
                columns: new[] { "segment_id", "time", "start_id", "end_id" });

            migrationBuilder.CreateIndex(
                name: "ix_segment_runs_start_id",
                schema: "velotimer",
                table: "segment_runs",
                column: "start_id");

            migrationBuilder.CreateIndex(
                name: "ix_segments_end_id",
                schema: "velotimer",
                table: "segments",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_segments_start_id",
                schema: "velotimer",
                table: "segments",
                column: "start_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_names_transponder_id",
                schema: "velotimer",
                table: "transponder_names",
                column: "transponder_id");

            migrationBuilder.AddForeignKey(
                name: "fk_passings_timing_loops_loop_id",
                schema: "velotimer",
                table: "passings",
                column: "loop_id",
                principalSchema: "velotimer",
                principalTable: "timing_loops",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_passings_transponders_transponder_id",
                schema: "velotimer",
                table: "passings",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_timing_loops_tracks_track_id",
                schema: "velotimer",
                table: "timing_loops",
                column: "track_id",
                principalSchema: "velotimer",
                principalTable: "tracks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponders_transponder_type_timing_system",
                schema: "velotimer",
                table: "transponders",
                column: "timing_system",
                principalSchema: "velotimer",
                principalTable: "transponder_type",
                principalColumn: "system",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponders_ownerships_riders_owner_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                column: "owner_id",
                principalSchema: "velotimer",
                principalTable: "riders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponders_ownerships_transponders_transponder_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
