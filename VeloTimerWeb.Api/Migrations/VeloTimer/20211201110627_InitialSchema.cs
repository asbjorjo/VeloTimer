using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class InitialSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "velotimer");

            migrationBuilder.CreateTable(
                name: "rider",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rider", x => x.id);
                    table.UniqueConstraint("ak_rider_user_id", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "statistics_item",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    label = table.Column<string>(type: "text", nullable: true),
                    distance = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "track",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    length = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transponder_type",
                schema: "velotimer",
                columns: table => new
                {
                    system = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_type", x => x.system);
                });

            migrationBuilder.CreateTable(
                name: "track_statistics_item",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    statistics_item_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_statistics_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_statistics_item_statistics_item_statistics_item_id",
                        column: x => x.statistics_item_id,
                        principalSchema: "velotimer",
                        principalTable: "statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "timing_loop",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<long>(type: "bigint", nullable: false),
                    loop_id = table.Column<int>(type: "integer", nullable: false),
                    distance = table.Column<double>(type: "double precision", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timing_loop", x => x.id);
                    table.UniqueConstraint("ak_timing_loop_track_id_loop_id", x => new { x.track_id, x.loop_id });
                    table.ForeignKey(
                        name: "fk_timing_loop_track_track_id",
                        column: x => x.track_id,
                        principalSchema: "velotimer",
                        principalTable: "track",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transponder",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    label = table.Column<string>(type: "text", nullable: true),
                    system_id = table.Column<string>(type: "text", nullable: false),
                    timing_system = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder", x => x.id);
                    table.UniqueConstraint("ak_transponder_timing_system_system_id", x => new { x.timing_system, x.system_id });
                    table.ForeignKey(
                        name: "fk_transponder_transponder_type_timing_system",
                        column: x => x.timing_system,
                        principalSchema: "velotimer",
                        principalTable: "transponder_type",
                        principalColumn: "system",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_segment",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    start_id = table.Column<long>(type: "bigint", nullable: false),
                    end_id = table.Column<long>(type: "bigint", nullable: false),
                    length = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_segment", x => x.id);
                    table.UniqueConstraint("ak_track_segment_start_id_end_id", x => new { x.start_id, x.end_id });
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
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "passing",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    loop_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passing", x => x.id);
                    table.UniqueConstraint("ak_passing_time_transponder_id_loop_id", x => new { x.time, x.transponder_id, x.loop_id });
                    table.ForeignKey(
                        name: "fk_passing_timing_loop_loop_id",
                        column: x => x.loop_id,
                        principalSchema: "velotimer",
                        principalTable: "timing_loop",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_passing_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transponder_ownership",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transponder_id = table.Column<long>(type: "bigint", nullable: true),
                    owner_id = table.Column<long>(type: "bigint", nullable: true),
                    owned_from = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    owned_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_ownership", x => x.id);
                    table.ForeignKey(
                        name: "fk_transponder_ownership_rider_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "velotimer",
                        principalTable: "rider",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transponder_ownership_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transponder_statistics_item",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    statistics_item_id = table.Column<long>(type: "bigint", nullable: false),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    time = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_statistics_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_transponder_statistics_item_track_statistics_item_statistic~",
                        column: x => x.statistics_item_id,
                        principalSchema: "velotimer",
                        principalTable: "track_statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transponder_statistics_item_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "track_statistics_segment",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order = table.Column<int>(type: "integer", nullable: false),
                    segment_id = table.Column<long>(type: "bigint", nullable: false),
                    element_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_statistics_segment", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_statistics_segment_track_segment_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_statistics_segment_track_statistics_item_element_id",
                        column: x => x.element_id,
                        principalSchema: "velotimer",
                        principalTable: "track_statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_segment_passing",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_segment_id = table.Column<long>(type: "bigint", nullable: false),
                    transponder_id = table.Column<long>(type: "bigint", nullable: false),
                    time = table.Column<double>(type: "double precision", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    start_id = table.Column<long>(type: "bigint", nullable: false),
                    end_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_segment_passing", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_passing_end_id",
                        column: x => x.end_id,
                        principalSchema: "velotimer",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_passing_start_id",
                        column: x => x.start_id,
                        principalSchema: "velotimer",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_track_segment_track_segment_id",
                        column: x => x.track_segment_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transponder_statistics_segment",
                schema: "velotimer",
                columns: table => new
                {
                    transponder_statistics_item_id = table.Column<long>(type: "bigint", nullable: false),
                    track_segment_passing_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_statistics_segment", x => new { x.transponder_statistics_item_id, x.track_segment_passing_id });
                    table.ForeignKey(
                        name: "fk_transponder_statistics_segment_track_segment_passing_track_~",
                        column: x => x.track_segment_passing_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment_passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transponder_statistics_segment_transponder_statistics_item_~",
                        column: x => x.transponder_statistics_item_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder_statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passing_loop_id",
                schema: "velotimer",
                table: "passing",
                column: "loop_id");

            migrationBuilder.CreateIndex(
                name: "ix_passing_source_id",
                schema: "velotimer",
                table: "passing",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_passing_time",
                schema: "velotimer",
                table: "passing",
                column: "time");

            migrationBuilder.CreateIndex(
                name: "ix_passing_transponder_id",
                schema: "velotimer",
                table: "passing",
                column: "transponder_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_end_id",
                schema: "velotimer",
                table: "track_segment",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_end_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_start_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "start_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_start_time_end_time",
                schema: "velotimer",
                table: "track_segment_passing",
                columns: new[] { "start_time", "end_time" });

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_track_segment_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "track_segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_transponder_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "transponder_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_item_statistics_item_id",
                schema: "velotimer",
                table: "track_statistics_item",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_segment_element_id",
                schema: "velotimer",
                table: "track_statistics_segment",
                column: "element_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_segment_segment_id",
                schema: "velotimer",
                table: "track_statistics_segment",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_ownership_owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_ownership_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "transponder_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_item_end_time_start_time",
                schema: "velotimer",
                table: "transponder_statistics_item",
                columns: new[] { "end_time", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_item_statistics_item_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_item_transponder_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                column: "transponder_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_segment_track_segment_passing_id",
                schema: "velotimer",
                table: "transponder_statistics_segment",
                column: "track_segment_passing_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_statistics_segment",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_ownership",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_statistics_segment",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "rider",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "track_segment_passing",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_statistics_item",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "passing",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "track_segment",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "track_statistics_item",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "timing_loop",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "statistics_item",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_type",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "track",
                schema: "velotimer");
        }
    }
}
