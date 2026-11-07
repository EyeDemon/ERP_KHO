using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261018083500_AddAuditLogCompositeIndexes")]
public sealed class AddAuditLogCompositeIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_EntityName_Action_EntityId_Timestamp",
            table: "AuditLogs",
            columns: new[] { "EntityName", "Action", "EntityId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_EntityName_WarehouseId_Timestamp",
            table: "AuditLogs",
            columns: new[] { "EntityName", "WarehouseId", "Timestamp" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_EntityName_Action_EntityId_Timestamp",
            table: "AuditLogs");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_EntityName_WarehouseId_Timestamp",
            table: "AuditLogs");
    }
}
