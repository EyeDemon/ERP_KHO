using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenantSaaSIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseCode",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UnitCode",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_Code",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_Stocktakes_Code",
                table: "Stocktakes");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_ReservationCode",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_SourceType_SourceId_ProductId",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockAllocations_AllocationCode",
                table: "StockAllocations");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_ExternalOrderId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrderCode",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingTolerancePolicies_IsActive_EffectiveFromUtc",
                table: "ReceivingTolerancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingTolerancePolicies_ProductId_SupplierId_WarehouseId_Version",
                table: "ReceivingTolerancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingReasonCodes_Code_IsActive_EffectiveFromUtc",
                table: "ReceivingReasonCodes");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingReasonCodes_Code_Version",
                table: "ReceivingReasonCodes");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_SourceSystem_ExternalPoId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductCode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "UX_ProductCategories_Code",
                table: "ProductCategories");

            migrationBuilder.DropIndex(
                name: "UX_ProductBarcodes_Value",
                table: "ProductBarcodes");

            migrationBuilder.DropIndex(
                name: "IX_PickingTasks_SourceType_SourceId",
                table: "PickingTasks");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_CanonicalBucket",
                table: "InventoryStocks");

            migrationBuilder.DropIndex(
                name: "IX_ImportReceipts_Code",
                table: "ImportReceipts");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_UserId_CommandScope_KeyHash",
                table: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_HandlingUnits_Sscc",
                table: "HandlingUnits");

            migrationBuilder.DropIndex(
                name: "IX_ExportReceipts_Code",
                table: "ExportReceipts");

            migrationBuilder.DropIndex(
                name: "UX_BusinessPartners_Code",
                table: "BusinessPartners");

            migrationBuilder.DropIndex(
                name: "IX_Backorders_BackorderCode",
                table: "Backorders");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "YardSlots",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "WarehouseShifts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Warehouses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "WarehouseLocations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "WarehouseCalendars",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "WarehouseCalendarDays",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "UserWarehouses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "UserSessions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Units",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockTransfers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockTransferDetails",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Stocktakes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StocktakeDetails",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockReservations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockAllocations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ShortPickExceptions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ShipmentTrackingEvents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Shipments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ShipmentProofOfDeliveries",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ShipmentHandlingUnits",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesOrders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesOrderLines",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ReceivingTolerancePolicies",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ReceivingResolutionVersions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ReceivingReasonCodes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ReceivingObservationVersions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ReceivingObservationItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ReceivingDiscrepancies",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "QcPolicies",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PutawayTasks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PutawayTaskItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PurchaseOrders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PurchaseOrderLines",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProductUoms",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProductCategories",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProductBarcodes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PickingTasks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PickingTaskLines",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PackingSessions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "InventoryTransactions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "InventoryStocks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "InventorySerials",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "InventoryLots",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "InventoryLocks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "InventoryLocationMovements",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ImportReceipts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ImportReceiptInventoryIdentities",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "IdempotencyRecords",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "HandlingUnits",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "HandlingUnitContents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ExportReceipts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ExportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Docks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "DockAppointments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "DockAppointmentEvents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "BusinessPartners",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Backorders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AuditLogs",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Asns",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AsnLines",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 1,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 2,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 3,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 4,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 5,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 6,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 7,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingReasonCodes",
                keyColumn: "Id",
                keyValue: 8,
                column: "TenantId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ReceivingTolerancePolicies",
                keyColumn: "Id",
                keyValue: 1,
                column: "TenantId",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "UX_Warehouses_TenantId_Code",
                table: "Warehouses",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_TenantId_Username",
                table: "Users",
                columns: new[] { "TenantId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Units_TenantId_Code",
                table: "Units",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockTransfers_TenantId_Code",
                table: "StockTransfers",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Stocktakes_TenantId_Code",
                table: "Stocktakes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockReservations_TenantId_ReservationCode",
                table: "StockReservations",
                columns: new[] { "TenantId", "ReservationCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockReservations_TenantId_SourceType_SourceId_ProductId",
                table: "StockReservations",
                columns: new[] { "TenantId", "SourceType", "SourceId", "ProductId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockAllocations_TenantId_AllocationCode",
                table: "StockAllocations",
                columns: new[] { "TenantId", "AllocationCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesOrders_TenantId_ExternalOrderId",
                table: "SalesOrders",
                columns: new[] { "TenantId", "ExternalOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesOrders_TenantId_OrderCode",
                table: "SalesOrders",
                columns: new[] { "TenantId", "OrderCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingTolerancePolicies_ProductId",
                table: "ReceivingTolerancePolicies",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingTolerancePolicies_TenantId_IsActive_EffectiveFromUtc",
                table: "ReceivingTolerancePolicies",
                columns: new[] { "TenantId", "IsActive", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingTolerancePolicies_TenantId_ProductId_SupplierId_WarehouseId_Version",
                table: "ReceivingTolerancePolicies",
                columns: new[] { "TenantId", "ProductId", "SupplierId", "WarehouseId", "Version" },
                unique: true,
                filter: "[ProductId] IS NOT NULL AND [SupplierId] IS NOT NULL AND [WarehouseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReasonCodes_TenantId_Code_IsActive_EffectiveFromUtc",
                table: "ReceivingReasonCodes",
                columns: new[] { "TenantId", "Code", "IsActive", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ReceivingReasonCodes_TenantId_Code_Version",
                table: "ReceivingReasonCodes",
                columns: new[] { "TenantId", "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseOrders_TenantId_SourceSystem_ExternalPoId",
                table: "PurchaseOrders",
                columns: new[] { "TenantId", "SourceSystem", "ExternalPoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Products_TenantId_Code",
                table: "Products",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategories_TenantId_Code",
                table: "ProductCategories",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductBarcodes_TenantId_Value",
                table: "ProductBarcodes",
                columns: new[] { "TenantId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingTasks_TenantId_SourceType_SourceId",
                table: "PickingTasks",
                columns: new[] { "TenantId", "SourceType", "SourceId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_InventoryStocks_CanonicalBucket] ON [InventoryStocks] ([TenantId], [ProductId], [WarehouseId], [Status], [CanonicalLocationId], [CanonicalLotId], [CanonicalSerialId]);");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_ProductId",
                table: "InventoryStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "UX_ImportReceipts_TenantId_Code",
                table: "ImportReceipts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_UserId",
                table: "IdempotencyRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyRecords_TenantId_UserId_CommandScope_KeyHash",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "UserId", "CommandScope", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_HandlingUnits_TenantId_Sscc",
                table: "HandlingUnits",
                columns: new[] { "TenantId", "Sscc" },
                unique: true,
                filter: "[Sscc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ExportReceipts_TenantId_Code",
                table: "ExportReceipts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartners_TenantId_Code",
                table: "BusinessPartners",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Backorders_TenantId_BackorderCode",
                table: "Backorders",
                columns: new[] { "TenantId", "BackorderCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Warehouses_TenantId_Code",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "UX_Users_TenantId_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Units_TenantId_Code",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "UX_StockTransfers_TenantId_Code",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "UX_Stocktakes_TenantId_Code",
                table: "Stocktakes");

            migrationBuilder.DropIndex(
                name: "UX_StockReservations_TenantId_ReservationCode",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "UX_StockReservations_TenantId_SourceType_SourceId_ProductId",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "UX_StockAllocations_TenantId_AllocationCode",
                table: "StockAllocations");

            migrationBuilder.DropIndex(
                name: "UX_SalesOrders_TenantId_ExternalOrderId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "UX_SalesOrders_TenantId_OrderCode",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingTolerancePolicies_ProductId",
                table: "ReceivingTolerancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingTolerancePolicies_TenantId_IsActive_EffectiveFromUtc",
                table: "ReceivingTolerancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingTolerancePolicies_TenantId_ProductId_SupplierId_WarehouseId_Version",
                table: "ReceivingTolerancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingReasonCodes_TenantId_Code_IsActive_EffectiveFromUtc",
                table: "ReceivingReasonCodes");

            migrationBuilder.DropIndex(
                name: "UX_ReceivingReasonCodes_TenantId_Code_Version",
                table: "ReceivingReasonCodes");

            migrationBuilder.DropIndex(
                name: "UX_PurchaseOrders_TenantId_SourceSystem_ExternalPoId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "UX_Products_TenantId_Code",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "UX_ProductCategories_TenantId_Code",
                table: "ProductCategories");

            migrationBuilder.DropIndex(
                name: "UX_ProductBarcodes_TenantId_Value",
                table: "ProductBarcodes");

            migrationBuilder.DropIndex(
                name: "IX_PickingTasks_TenantId_SourceType_SourceId",
                table: "PickingTasks");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_CanonicalBucket",
                table: "InventoryStocks");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_ProductId",
                table: "InventoryStocks");

            migrationBuilder.DropIndex(
                name: "UX_ImportReceipts_TenantId_Code",
                table: "ImportReceipts");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_UserId",
                table: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "UX_IdempotencyRecords_TenantId_UserId_CommandScope_KeyHash",
                table: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "UX_HandlingUnits_TenantId_Sscc",
                table: "HandlingUnits");

            migrationBuilder.DropIndex(
                name: "UX_ExportReceipts_TenantId_Code",
                table: "ExportReceipts");

            migrationBuilder.DropIndex(
                name: "UX_BusinessPartners_TenantId_Code",
                table: "BusinessPartners");

            migrationBuilder.DropIndex(
                name: "UX_Backorders_TenantId_BackorderCode",
                table: "Backorders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "YardSlots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WarehouseShifts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WarehouseLocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WarehouseCalendars");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WarehouseCalendarDays");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserWarehouses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransferDetails");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Stocktakes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockAllocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ShortPickExceptions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ShipmentTrackingEvents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ShipmentProofOfDeliveries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ShipmentHandlingUnits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceivingTolerancePolicies");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceivingResolutionVersions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceivingReasonCodes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceivingObservationVersions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceivingObservationItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceivingDiscrepancies");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "QcPolicies");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PutawayTasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PutawayTaskItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductUoms");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductBarcodes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PickingTasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PickingTaskLines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PackingSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventoryStocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventorySerials");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventoryLots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventoryLocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventoryLocationMovements");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ImportReceipts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ImportReceiptInventoryIdentities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HandlingUnits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HandlingUnitContents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ExportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Docks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DockAppointments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DockAppointmentEvents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Backorders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Asns");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AsnLines");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseCode",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitCode",
                table: "Units",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_Code",
                table: "StockTransfers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_Code",
                table: "Stocktakes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ReservationCode",
                table: "StockReservations",
                column: "ReservationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_SourceType_SourceId_ProductId",
                table: "StockReservations",
                columns: new[] { "SourceType", "SourceId", "ProductId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockAllocations_AllocationCode",
                table: "StockAllocations",
                column: "AllocationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_ExternalOrderId",
                table: "SalesOrders",
                column: "ExternalOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrderCode",
                table: "SalesOrders",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingTolerancePolicies_IsActive_EffectiveFromUtc",
                table: "ReceivingTolerancePolicies",
                columns: new[] { "IsActive", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingTolerancePolicies_ProductId_SupplierId_WarehouseId_Version",
                table: "ReceivingTolerancePolicies",
                columns: new[] { "ProductId", "SupplierId", "WarehouseId", "Version" },
                unique: true,
                filter: "[ProductId] IS NOT NULL AND [SupplierId] IS NOT NULL AND [WarehouseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReasonCodes_Code_IsActive_EffectiveFromUtc",
                table: "ReceivingReasonCodes",
                columns: new[] { "Code", "IsActive", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReasonCodes_Code_Version",
                table: "ReceivingReasonCodes",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SourceSystem_ExternalPoId",
                table: "PurchaseOrders",
                columns: new[] { "SourceSystem", "ExternalPoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCode",
                table: "Products",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategories_Code",
                table: "ProductCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductBarcodes_Value",
                table: "ProductBarcodes",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingTasks_SourceType_SourceId",
                table: "PickingTasks",
                columns: new[] { "SourceType", "SourceId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_InventoryStocks_CanonicalBucket] ON [InventoryStocks] ([ProductId], [WarehouseId], [Status], [CanonicalLocationId], [CanonicalLotId], [CanonicalSerialId]);");

            migrationBuilder.CreateIndex(
                name: "IX_ImportReceipts_Code",
                table: "ImportReceipts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_UserId_CommandScope_KeyHash",
                table: "IdempotencyRecords",
                columns: new[] { "UserId", "CommandScope", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandlingUnits_Sscc",
                table: "HandlingUnits",
                column: "Sscc",
                unique: true,
                filter: "[Sscc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExportReceipts_Code",
                table: "ExportReceipts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartners_Code",
                table: "BusinessPartners",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Backorders_BackorderCode",
                table: "Backorders",
                column: "BackorderCode",
                unique: true);
        }
    }
}
