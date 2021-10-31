using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations
{
    public partial class UseDateTimeOffset : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint("AK_Passings_Time_TransponderId_LoopId", "Passings");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "Time",
                table: "Passings",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddUniqueConstraint("AK_Passings_Time_TransponderId_LoopId", "Passings", new string[] { "Time", "TransponderId", "LoopId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint("AK_Passings_Time_TransponderId_LoopId", "Passings");
            
            migrationBuilder.AlterColumn<DateTime>(
                name: "Time",
                table: "Passings",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AddUniqueConstraint("AK_Passings_Time_TransponderId_LoopId", "Passings", new string[] { "Time", "TransponderId", "LoopId" });
        }
    }
}
