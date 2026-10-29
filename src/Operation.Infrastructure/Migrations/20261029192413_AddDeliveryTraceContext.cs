using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trace_parent",
                schema: "operation",
                table: "broker_inbox",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_state",
                schema: "operation",
                table: "broker_inbox",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trace_parent",
                schema: "operation",
                table: "broker_inbox");

            migrationBuilder.DropColumn(
                name: "trace_state",
                schema: "operation",
                table: "broker_inbox");
        }
    }
}
