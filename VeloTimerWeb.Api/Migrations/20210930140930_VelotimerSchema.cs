using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations
{
    public partial class VelotimerSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Length = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transponders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transponders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimingLoops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoopId = table.Column<long>(type: "bigint", nullable: false),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    Distance = table.Column<double>(type: "float", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimingLoops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimingLoops_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Passings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransponderId = table.Column<long>(type: "bigint", nullable: false),
                    LoopId = table.Column<long>(type: "bigint", nullable: false),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passings", x => x.Id);
                    table.UniqueConstraint("AK_Passings_Time_TransponderId_LoopId", x => new { x.Time, x.TransponderId, x.LoopId });
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
                name: "IX_Passings_TransponderId",
                table: "Passings",
                column: "TransponderId");

            migrationBuilder.CreateIndex(
                name: "IX_TimingLoops_TrackId",
                table: "TimingLoops",
                column: "TrackId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Passings");

            migrationBuilder.DropTable(
                name: "TimingLoops");

            migrationBuilder.DropTable(
                name: "Transponders");

            migrationBuilder.DropTable(
                name: "Tracks");
        }
    }
}
