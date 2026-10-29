using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLaborCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_jobs",
                schema: "operation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    price_cents = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_jobs", x => x.id);
                    table.CheckConstraint("ck_catalog_job", "price_cents >= 0 AND version > 0 AND length(trim(name)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_jobs_is_active_name_id",
                schema: "operation",
                table: "catalog_jobs",
                columns: new[] { "is_active", "name", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_jobs",
                schema: "operation");
        }
    }
}
