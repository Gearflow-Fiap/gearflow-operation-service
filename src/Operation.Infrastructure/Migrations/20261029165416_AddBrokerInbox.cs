using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broker_inbox",
                schema: "operation",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    command_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reply_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reply_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reply_json = table.Column<string>(type: "jsonb", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_inbox", x => x.message_id);
                    table.CheckConstraint("ck_broker_inbox_hash", "length(fingerprint) = 64");
                });

            migrationBuilder.CreateIndex(
                name: "IX_broker_inbox_is_completed_received_at",
                schema: "operation",
                table: "broker_inbox",
                columns: new[] { "is_completed", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_broker_inbox_processed_at_message_id",
                schema: "operation",
                table: "broker_inbox",
                columns: new[] { "processed_at", "message_id" },
                filter: "reply_json IS NOT NULL AND published_at IS NULL AND is_completed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_inbox",
                schema: "operation");
        }
    }
}
