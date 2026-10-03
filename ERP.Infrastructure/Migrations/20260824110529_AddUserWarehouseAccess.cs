using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWarehouseAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserWarehouses",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWarehouses", x => new { x.UserId, x.WarehouseId });
                    table.ForeignKey(
                        name: "FK_UserWarehouses_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserWarehouses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserWarehouses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_CreatedBy",
                table: "UserWarehouses",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_WarehouseId",
                table: "UserWarehouses",
                column: "WarehouseId");

            // Preserve existing access on rollout; administrators can reduce scope after deployment.
            migrationBuilder.Sql(
                """
                INSERT INTO UserWarehouses (UserId, WarehouseId, CreatedAt, CreatedBy)
                SELECT u.Id, w.Id, SYSUTCDATETIME(), u.Id
                FROM Users AS u
                CROSS JOIN Warehouses AS w;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserWarehouses");
        }
    }
}
