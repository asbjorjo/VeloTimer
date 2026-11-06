using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class timing_initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "timing");

            migrationBuilder.CreateTable(
                name: "installation",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    timing_system = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transponder",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_id = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    system = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "timing_point",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_id = table.Column<string>(type: "text", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timing_point", x => x.id);
                    table.ForeignKey(
                        name: "fk_timing_point_installation_installation_id",
                        column: x => x.installation_id,
                        principalSchema: "timing",
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "passing",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    low_battery = table.Column<bool>(type: "boolean", nullable: false),
                    low_strength = table.Column<bool>(type: "boolean", nullable: false),
                    low_hits = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passing", x => x.id);
                    table.ForeignKey(
                        name: "fk_passing_timing_point_timing_point_id",
                        column: x => x.timing_point_id,
                        principalSchema: "timing",
                        principalTable: "timing_point",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_passing_transponder_transponder_id",
                        column: x => x.transponder_id,
                        principalSchema: "timing",
                        principalTable: "transponder",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sample",
                schema: "timing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample", x => x.id);
                    table.ForeignKey(
                        name: "fk_sample_passing_end_id",
                        column: x => x.end_id,
                        principalSchema: "timing",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sample_passing_start_id",
                        column: x => x.start_id,
                        principalSchema: "timing",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passing_timing_point_id",
                schema: "timing",
                table: "passing",
                column: "timing_point_id");

            migrationBuilder.CreateIndex(
                name: "ix_passing_transponder_id_time_timing_point_id",
                schema: "timing",
                table: "passing",
                columns: new[] { "transponder_id", "time", "timing_point_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_end_id",
                schema: "timing",
                table: "sample",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_start_id_end_id",
                schema: "timing",
                table: "sample",
                columns: new[] { "start_id", "end_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timing_point_installation_id_system_id",
                schema: "timing",
                table: "timing_point",
                columns: new[] { "installation_id", "system_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transponder_system_system_id",
                schema: "timing",
                table: "transponder",
                columns: new[] { "system", "system_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sample",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "passing",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "timing_point",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "transponder",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "installation",
                schema: "timing");
        }
    }
}
