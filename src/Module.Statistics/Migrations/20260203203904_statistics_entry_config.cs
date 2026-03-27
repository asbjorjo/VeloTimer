using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_entry_config : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_simple_statistics_item_config_statistics_item_statistics_it",
                schema: "statistics",
                table: "simple_statistics_item_config");

            migrationBuilder.DropTable(
                name: "multi_statistics_item_config",
                schema: "statistics");

            migrationBuilder.DropPrimaryKey(
                name: "pk_simple_statistics_item_config",
                schema: "statistics",
                table: "simple_statistics_item_config");

            migrationBuilder.RenameTable(
                name: "simple_statistics_item_config",
                schema: "statistics",
                newName: "statistics_item_config",
                newSchema: "statistics");

            migrationBuilder.RenameIndex(
                name: "ix_simple_statistics_item_config_statistics_item_id_course_poi",
                schema: "statistics",
                table: "statistics_item_config",
                newName: "ix_statistics_item_config_statistics_item_id_course_point_star");

            migrationBuilder.AddColumn<Guid>(
                name: "statistics_item_config_id",
                schema: "statistics",
                table: "statistics_entry",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "course_point_start",
                schema: "statistics",
                table: "statistics_item_config",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "course_point_end",
                schema: "statistics",
                table: "statistics_item_config",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "discriminator",
                schema: "statistics",
                table: "statistics_item_config",
                type: "character varying(34)",
                maxLength: 34,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_config_id",
                schema: "statistics",
                table: "statistics_item_config",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "repetitions",
                schema: "statistics",
                table: "statistics_item_config",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_statistics_item_config",
                schema: "statistics",
                table: "statistics_item_config",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_statistics_item_config_id",
                schema: "statistics",
                table: "statistics_entry",
                column: "statistics_item_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_item_config_parent_config_id",
                schema: "statistics",
                table: "statistics_item_config",
                column: "parent_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_item_config_statistics_item_id_parent_config_id",
                schema: "statistics",
                table: "statistics_item_config",
                columns: new[] { "statistics_item_id", "parent_config_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_statistics_entry_statistics_item_config_statistics_item_con",
                schema: "statistics",
                table: "statistics_entry",
                column: "statistics_item_config_id",
                principalSchema: "statistics",
                principalTable: "statistics_item_config",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_statistics_item_config_statistics_item_config_parent_config",
                schema: "statistics",
                table: "statistics_item_config",
                column: "parent_config_id",
                principalSchema: "statistics",
                principalTable: "statistics_item_config",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_statistics_item_config_statistics_item_statistics_item_id",
                schema: "statistics",
                table: "statistics_item_config",
                column: "statistics_item_id",
                principalSchema: "statistics",
                principalTable: "statistics_item",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_statistics_entry_statistics_item_config_statistics_item_con",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropForeignKey(
                name: "fk_statistics_item_config_statistics_item_config_parent_config",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropForeignKey(
                name: "fk_statistics_item_config_statistics_item_statistics_item_id",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropIndex(
                name: "ix_statistics_entry_statistics_item_config_id",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropPrimaryKey(
                name: "pk_statistics_item_config",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropIndex(
                name: "ix_statistics_item_config_parent_config_id",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropIndex(
                name: "ix_statistics_item_config_statistics_item_id_parent_config_id",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "statistics_item_config_id",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "discriminator",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "parent_config_id",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.DropColumn(
                name: "repetitions",
                schema: "statistics",
                table: "statistics_item_config");

            migrationBuilder.RenameTable(
                name: "statistics_item_config",
                schema: "statistics",
                newName: "simple_statistics_item_config",
                newSchema: "statistics");

            migrationBuilder.RenameIndex(
                name: "ix_statistics_item_config_statistics_item_id_course_point_star",
                schema: "statistics",
                table: "simple_statistics_item_config",
                newName: "ix_simple_statistics_item_config_statistics_item_id_course_poi");

            migrationBuilder.AlterColumn<Guid>(
                name: "course_point_start",
                schema: "statistics",
                table: "simple_statistics_item_config",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "course_point_end",
                schema: "statistics",
                table: "simple_statistics_item_config",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_simple_statistics_item_config",
                schema: "statistics",
                table: "simple_statistics_item_config",
                column: "id");

            migrationBuilder.CreateTable(
                name: "multi_statistics_item_config",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repetitions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_multi_statistics_item_config", x => x.id);
                    table.ForeignKey(
                        name: "fk_multi_statistics_item_config_simple_statistics_item_config_",
                        column: x => x.parent_config_id,
                        principalSchema: "statistics",
                        principalTable: "simple_statistics_item_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_multi_statistics_item_config_statistics_item_statistics_ite",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_multi_statistics_item_config_parent_config_id",
                schema: "statistics",
                table: "multi_statistics_item_config",
                column: "parent_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_multi_statistics_item_config_statistics_item_id_parent_conf",
                schema: "statistics",
                table: "multi_statistics_item_config",
                columns: new[] { "statistics_item_id", "parent_config_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_simple_statistics_item_config_statistics_item_statistics_it",
                schema: "statistics",
                table: "simple_statistics_item_config",
                column: "statistics_item_id",
                principalSchema: "statistics",
                principalTable: "statistics_item",
                principalColumn: "id");
        }
    }
}
