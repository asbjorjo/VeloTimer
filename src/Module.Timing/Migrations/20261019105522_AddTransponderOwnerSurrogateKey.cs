using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Timing.Migrations
{
    /// <inheritdoc />
    public partial class AddTransponderOwnerSurrogateKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "timing",
                table: "transponder_owner",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE timing.transponder_owner SET id = gen_random_uuid() WHERE id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "timing",
                table: "transponder_owner",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropPrimaryKey(
                name: "pk_transponder_owner",
                schema: "timing",
                table: "transponder_owner");

            migrationBuilder.AddPrimaryKey(
                name: "pk_transponder_owner",
                schema: "timing",
                table: "transponder_owner",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_owner_transponder_id",
                schema: "timing",
                table: "transponder_owner",
                column: "transponder_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_transponder_owner",
                schema: "timing",
                table: "transponder_owner");

            migrationBuilder.DropIndex(
                name: "ix_transponder_owner_transponder_id",
                schema: "timing",
                table: "transponder_owner");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "timing",
                table: "transponder_owner");

            migrationBuilder.AddPrimaryKey(
                name: "pk_transponder_owner",
                schema: "timing",
                table: "transponder_owner",
                columns: new[] { "transponder_id", "owner_id" });
        }
    }
}
