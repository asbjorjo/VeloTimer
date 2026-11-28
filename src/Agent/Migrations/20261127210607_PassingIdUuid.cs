using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Agent.Migrations
{
    /// <inheritdoc />
    public partial class PassingIdUuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agent passing IDs become PassingObservedV1.SourcePassingId and must be UUIDs. Alpha policy:
            // rows whose ID is not a UUID are deleted rather than migrated.
            migrationBuilder.Sql("""
                DELETE FROM agent.passing
                WHERE id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';
                ALTER TABLE agent.passing ALTER COLUMN id TYPE uuid USING id::uuid;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "id",
                schema: "agent",
                table: "passing",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
