using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class InitialSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "velotimer");

            migrationBuilder.CreateTable(
                name: "riders",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_riders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    length = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transponder_type",
                schema: "velotimer",
                columns: table => new
                {
                    system = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_type", x => x.system);
                });

            migrationBuilder.CreateTable(
                name: "timing_loops",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    loop_id = table.Column<long>(type: "bigint", nullable: false),
                    track_id = table.Column<long>(type: "bigint", nullable: false),
                    distance = table.Column<double>(type: "float", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timing_loops", x => x.id);
                    table.UniqueConstraint("ak_timing_loops_track_id_loop_id", x => new { x.track_id, x.loop_id });
                    table.ForeignKey(
                        name: "fk_timing_loops_tracks_track_id",
                        column: x => x.track_id,
                        principalSchema: "velotimer",
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transponders",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    system_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    timing_system = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponders", x => x.id);
                    table.UniqueConstraint("ak_transponders_timing_system_system_id", x => new { x.timing_system, x.system_id });
                    table.ForeignKey(
                        name: "fk_transponders_transponder_type_timing_system",
                        column: x => x.timing_system,
                        principalSchema: "velotimer",
                        principalTable: "transponder_type",
                        principalColumn: "system",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "segments",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    start_id = table.Column<long>(type: "bigint", nullable: false),
                    end_id = table.Column<long>(type: "bigint", nullable: false),
                    min_time = table.Column<long>(type: "bigint", nullable: false),
                    max_time = table.Column<long>(type: "bigint", nullable: false)
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
                name: "passings",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    loop_id = table.Column<long>(type: "bigint", nullable: false),
                    time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    source = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passings", x => x.id);
                    table.UniqueConstraint("ak_passings_time_transponder_id_loop_id", x => new { x.time, x.transponder_id, x.loop_id });
                    table.ForeignKey(
                        name: "fk_passings_timing_loops_loop_id",
                        column: x => x.loop_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_passings_transponders_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transponder_names",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                name: "transponders_ownerships",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    owned_from = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    owned_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponders_ownerships", x => x.id);
                    table.ForeignKey(
                        name: "fk_transponders_ownerships_riders_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "velotimer",
                        principalTable: "riders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transponders_ownerships_transponders_transponder_id",
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

            migrationBuilder.CreateIndex(
                name: "ix_intermediate_loop_id",
                schema: "velotimer",
                table: "intermediate",
                column: "loop_id");

            migrationBuilder.CreateIndex(
                name: "ix_passings_loop_id",
                schema: "velotimer",
                table: "passings",
                column: "loop_id");

            migrationBuilder.CreateIndex(
                name: "ix_passings_transponder_id",
                schema: "velotimer",
                table: "passings",
                column: "transponder_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_transponders_ownerships_owner_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponders_ownerships_transponder_id",
                schema: "velotimer",
                table: "transponders_ownerships",
                column: "transponder_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "intermediate",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "passings",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_names",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponders_ownerships",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "segments",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "riders",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponders",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "timing_loops",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_type",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "tracks",
                schema: "velotimer");
        }
    }
}
