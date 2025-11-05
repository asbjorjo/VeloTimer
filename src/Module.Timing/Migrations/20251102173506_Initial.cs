using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "timing");

            migrationBuilder.CreateTable(
                name: "TimingSystem",
                schema: "timing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimingSystem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransponderType",
                schema: "timing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransponderType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installation",
                schema: "timing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Facility = table.Column<Guid>(type: "uuid", nullable: false),
                    TimingSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installation_TimingSystem_TimingSystemId",
                        column: x => x.TimingSystemId,
                        principalSchema: "timing",
                        principalTable: "TimingSystem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transponder",
                schema: "timing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transponder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transponder_TransponderType_TypeId",
                        column: x => x.TypeId,
                        principalSchema: "timing",
                        principalTable: "TransponderType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimingPoint",
                schema: "timing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemId = table.Column<string>(type: "text", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimingPoint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimingPoint_Installation_InstallationId",
                        column: x => x.InstallationId,
                        principalSchema: "timing",
                        principalTable: "Installation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Passing",
                schema: "timing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransponderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimingPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    LowBattery = table.Column<bool>(type: "boolean", nullable: false),
                    LowStrength = table.Column<bool>(type: "boolean", nullable: false),
                    LowHits = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Passing_TimingPoint_TimingPointId",
                        column: x => x.TimingPointId,
                        principalSchema: "timing",
                        principalTable: "TimingPoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Passing_Transponder_TransponderId",
                        column: x => x.TransponderId,
                        principalSchema: "timing",
                        principalTable: "Transponder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Installation_TimingSystemId",
                schema: "timing",
                table: "Installation",
                column: "TimingSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_Passing_TimingPointId",
                schema: "timing",
                table: "Passing",
                column: "TimingPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Passing_TransponderId",
                schema: "timing",
                table: "Passing",
                column: "TransponderId");

            migrationBuilder.CreateIndex(
                name: "IX_TimingPoint_InstallationId",
                schema: "timing",
                table: "TimingPoint",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_Transponder_TypeId",
                schema: "timing",
                table: "Transponder",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Passing",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "TimingPoint",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "Transponder",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "Installation",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "TransponderType",
                schema: "timing");

            migrationBuilder.DropTable(
                name: "TimingSystem",
                schema: "timing");
        }
    }
}
