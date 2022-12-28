using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Storage.Migrations
{
    public partial class ActiveLayout : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_track_track_id",
                schema: "velotimer",
                table: "activity");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_ownership_rider_owner_id",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_ownership_transponder_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "active",
                schema: "velotimer",
                table: "track_segment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "active",
                schema: "velotimer",
                table: "track_layout",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "velotimer",
                table: "track",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "velotimer",
                table: "timing_loop",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                schema: "velotimer",
                table: "statistics_item",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "velotimer",
                table: "rider",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                schema: "velotimer",
                table: "rider",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                schema: "velotimer",
                table: "rider",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "track_id",
                schema: "velotimer",
                table: "activity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_track_track_id",
                schema: "velotimer",
                table: "activity",
                column: "track_id",
                principalSchema: "velotimer",
                principalTable: "track",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_ownership_rider_owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "owner_id",
                principalSchema: "velotimer",
                principalTable: "rider",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_ownership_transponder_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_track_track_id",
                schema: "velotimer",
                table: "activity");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_ownership_rider_owner_id",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.DropForeignKey(
                name: "fk_transponder_ownership_transponder_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership");

            migrationBuilder.DropColumn(
                name: "active",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.DropColumn(
                name: "active",
                schema: "velotimer",
                table: "track_layout");

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

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "velotimer",
                table: "track",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "velotimer",
                table: "timing_loop",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "label",
                schema: "velotimer",
                table: "statistics_item",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "velotimer",
                table: "rider",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                schema: "velotimer",
                table: "rider",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                schema: "velotimer",
                table: "rider",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<long>(
                name: "track_id",
                schema: "velotimer",
                table: "activity",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_track_track_id",
                schema: "velotimer",
                table: "activity",
                column: "track_id",
                principalSchema: "velotimer",
                principalTable: "track",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_ownership_rider_owner_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "owner_id",
                principalSchema: "velotimer",
                principalTable: "rider",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_ownership_transponder_transponder_id",
                schema: "velotimer",
                table: "transponder_ownership",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id");
        }
    }
}
