using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Server.Data.Migrations
{
    public partial class SolaCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimingLoops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoopId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Updated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimingLoops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transponders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Updated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transponders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Passings",
                columns: table => new
                {
                    TransponderId = table.Column<long>(type: "bigint", nullable: false),
                    LoopId = table.Column<long>(type: "bigint", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Updated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passings", x => new { x.Time, x.TransponderId, x.LoopId });
                    table.ForeignKey(
                        name: "FK_Passings_TimingLoops_LoopId",
                        column: x => x.LoopId,
                        principalTable: "TimingLoops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Passings_Transponders_TransponderId",
                        column: x => x.TransponderId,
                        principalTable: "Transponders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Passings_LoopId",
                table: "Passings",
                column: "LoopId");

            migrationBuilder.CreateIndex(
                name: "IX_Passings_TransponderId_Time_LoopId",
                table: "Passings",
                columns: new[] { "TransponderId", "Time", "LoopId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Passings");

            migrationBuilder.DropTable(
                name: "TimingLoops");

            migrationBuilder.DropTable(
                name: "Transponders");
        }
    }
}
