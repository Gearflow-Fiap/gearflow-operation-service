using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialOperationStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operation");

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "operation",
                columns: table => new
                {
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_code = table.Column<int>(type: "integer", nullable: true),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_tombstone = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    state_format = table.Column<int>(type: "integer", nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    diagnostic_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    diagnostic_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    execution_authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    execution_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    execution_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations", x => x.service_order_id);
                    table.CheckConstraint("ck_operation_identity", "service_order_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_operation_tombstone", "NOT is_tombstone OR (NOT is_active AND status = 'Canceled' AND vehicle_id IS NULL)");
                    table.CheckConstraint("ck_operation_version", "version > 0 AND state_format = 1");
                });

            migrationBuilder.CreateTable(
                name: "command_receipts",
                schema: "operation",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_receipts", x => x.intent_id);
                    table.CheckConstraint("ck_receipt_fingerprint", "length(fingerprint) = 64");
                    table.ForeignKey(
                        name: "FK_command_receipts_operations_service_order_id",
                        column: x => x.service_order_id,
                        principalSchema: "operation",
                        principalTable: "operations",
                        principalColumn: "service_order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "histories",
                schema: "operation",
                columns: table => new
                {
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_histories", x => new { x.service_order_id, x.version });
                    table.CheckConstraint("ck_history_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_histories_operations_service_order_id",
                        column: x => x.service_order_id,
                        principalSchema: "operation",
                        principalTable: "operations",
                        principalColumn: "service_order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "operation",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    event_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_outbox_events_command_receipts_causation_id",
                        column: x => x.causation_id,
                        principalSchema: "operation",
                        principalTable: "command_receipts",
                        principalColumn: "intent_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outbox_events_operations_service_order_id",
                        column: x => x.service_order_id,
                        principalSchema: "operation",
                        principalTable: "operations",
                        principalColumn: "service_order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_command_receipts_service_order_id",
                schema: "operation",
                table: "command_receipts",
                column: "service_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_operations_created_at_service_order_id",
                schema: "operation",
                table: "operations",
                columns: new[] { "created_at", "service_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_operations_is_active_status_created_at_service_order_id",
                schema: "operation",
                table: "operations",
                columns: new[] { "is_active", "status", "created_at", "service_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_causation_id",
                schema: "operation",
                table: "outbox_events",
                column: "causation_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_occurred_at_event_id",
                schema: "operation",
                table: "outbox_events",
                columns: new[] { "occurred_at", "event_id" },
                filter: "dispatched_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_service_order_id_version",
                schema: "operation",
                table: "outbox_events",
                columns: new[] { "service_order_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "histories",
                schema: "operation");

            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "operation");

            migrationBuilder.DropTable(
                name: "command_receipts",
                schema: "operation");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "operation");
        }
    }
}
