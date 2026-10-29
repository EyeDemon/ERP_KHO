using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingDiscrepancyWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseDoorRejectedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseFinalReceivedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseObservedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DoorRejectedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalReceivedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FinalResolutionVersionId",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ObservedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ReceivingDiscrepancies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportReceiptId = table.Column<int>(type: "int", nullable: false),
                    ImportReceiptDetailId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingDiscrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingDiscrepancies_ImportReceiptDetails_ImportReceiptDetailId",
                        column: x => x.ImportReceiptDetailId,
                        principalTable: "ImportReceiptDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingDiscrepancies_ImportReceipts_ImportReceiptId",
                        column: x => x.ImportReceiptId,
                        principalTable: "ImportReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingReasonCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequiresNote = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAttachment = table.Column<bool>(type: "bit", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingReasonCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingTolerancePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AbsoluteQuantityTolerance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PercentageTolerance = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    OverageAllowed = table.Column<bool>(type: "bit", nullable: false),
                    ShortageAllowed = table.Column<bool>(type: "bit", nullable: false),
                    ValueTolerance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RequiresApprovalOutsideTolerance = table.Column<bool>(type: "bit", nullable: false),
                    ApproverTarget = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingTolerancePolicies", x => x.Id);
                    table.CheckConstraint("CK_ReceivingTolerancePolicies_Scope", "([ProductId] IS NOT NULL AND [SupplierId] IS NOT NULL) OR ([ProductId] IS NOT NULL AND [SupplierId] IS NULL AND [WarehouseId] IS NULL) OR ([ProductId] IS NULL AND [SupplierId] IS NULL)");
                    table.CheckConstraint("CK_ReceivingTolerancePolicies_Tolerance", "[AbsoluteQuantityTolerance] >= 0 AND [PercentageTolerance] >= 0 AND ([ValueTolerance] IS NULL OR [ValueTolerance] >= 0)");
                    table.ForeignKey(
                        name: "FK_ReceivingTolerancePolicies_BusinessPartners_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingTolerancePolicies_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivingTolerancePolicies_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingObservationVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceivingDiscrepancyId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PreviousObservationVersionId = table.Column<int>(type: "int", nullable: true),
                    ObservedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseObservedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ObservedUnitId = table.Column<int>(type: "int", nullable: false),
                    ObservedUnitCodeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConversionFactorSnapshot = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ConversionVersionSnapshot = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingObservationVersions", x => x.Id);
                    table.CheckConstraint("CK_ReceivingObservationVersions_Quantity", "[ObservedQuantity] >= 0 AND [BaseObservedQuantity] >= 0 AND [ConversionFactorSnapshot] > 0");
                    table.ForeignKey(
                        name: "FK_ReceivingObservationVersions_ReceivingDiscrepancies_ReceivingDiscrepancyId",
                        column: x => x.ReceivingDiscrepancyId,
                        principalTable: "ReceivingDiscrepancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingObservationVersions_ReceivingObservationVersions_PreviousObservationVersionId",
                        column: x => x.PreviousObservationVersionId,
                        principalTable: "ReceivingObservationVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReceivingResolutionVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceivingDiscrepancyId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PreviousResolutionVersionId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    DoorRejectedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseDoorRejectedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FinalReceivedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseFinalReceivedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: false),
                    ReasonCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReasonCategorySnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonVersionSnapshot = table.Column<int>(type: "int", nullable: false),
                    ReasonEffectiveAtUtcSnapshot = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReasonRequiresNoteSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    ReasonRequiresAttachmentSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    ReasonRequiresApprovalSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    TolerancePolicyId = table.Column<int>(type: "int", nullable: true),
                    TolerancePolicyVersionSnapshot = table.Column<int>(type: "int", nullable: true),
                    TolerancePolicySourceSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TolerancePolicyEffectiveAtUtcSnapshot = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AbsoluteToleranceSnapshot = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PercentageToleranceSnapshot = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    AllowedBaseToleranceSnapshot = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ValueToleranceSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    ResponsibleParty = table.Column<int>(type: "int", nullable: false),
                    SupplierClaimRequired = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedBy = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedBy = table.Column<int>(type: "int", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingResolutionVersions", x => x.Id);
                    table.CheckConstraint("CK_ReceivingResolutionVersions_Quantities", "[DoorRejectedQuantity] >= 0 AND [BaseDoorRejectedQuantity] >= 0 AND [FinalReceivedQuantity] >= 0 AND [BaseFinalReceivedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_ReceivingResolutionVersions_ReceivingDiscrepancies_ReceivingDiscrepancyId",
                        column: x => x.ReceivingDiscrepancyId,
                        principalTable: "ReceivingDiscrepancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingResolutionVersions_ReceivingResolutionVersions_PreviousResolutionVersionId",
                        column: x => x.PreviousResolutionVersionId,
                        principalTable: "ReceivingResolutionVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReceivingObservationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceivingObservationVersionId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScanReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    VoidedBy = table.Column<int>(type: "int", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByObservationVersionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingObservationItems", x => x.Id);
                    table.CheckConstraint("CK_ReceivingObservationItems_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReceivingObservationItems_ReceivingObservationVersions_ReceivingObservationVersionId",
                        column: x => x.ReceivingObservationVersionId,
                        principalTable: "ReceivingObservationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingObservationItems_ReceivingObservationVersions_SupersededByObservationVersionId",
                        column: x => x.SupersededByObservationVersionId,
                        principalTable: "ReceivingObservationVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ReceivingReasonCodes",
                columns: new[] { "Id", "Category", "Code", "EffectiveFromUtc", "EffectiveToUtc", "IsActive", "Name", "RequiresApproval", "RequiresAttachment", "RequiresNote", "Version" },
                values: new object[,]
                {
                    { 1, "Quantity", "UNDER_RECEIPT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Nhận thiếu", false, false, true, 1 },
                    { 2, "Quantity", "OVER_RECEIPT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Nhận thừa", false, false, true, 1 },
                    { 3, "Condition", "DAMAGED_ON_RECEIPT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Hư hỏng khi nhận", false, false, true, 1 },
                    { 4, "Identity", "WRONG_PRODUCT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Sai sản phẩm", false, false, true, 1 },
                    { 5, "Identity", "WRONG_LOT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Sai lô", false, false, true, 1 },
                    { 6, "Custody", "REJECTED_AT_DOOR", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Từ chối tại cửa", false, false, true, 1 },
                    { 7, "Uom", "UOM_MISMATCH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Sai đơn vị tính", false, false, true, 1 },
                    { 8, "Counting", "DUPLICATE_COUNT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Đếm trùng", false, false, true, 1 }
                });

            migrationBuilder.InsertData(
                table: "ReceivingTolerancePolicies",
                columns: new[] { "Id", "AbsoluteQuantityTolerance", "ApproverTarget", "EffectiveFromUtc", "EffectiveToUtc", "IsActive", "OverageAllowed", "PercentageTolerance", "ProductId", "RequiresApprovalOutsideTolerance", "ShortageAllowed", "SupplierId", "ValueTolerance", "Version", "WarehouseId" },
                values: new object[] { 1, 0m, "receiving_discrepancy.approve", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, 0m, null, true, true, null, null, 1, null });

            migrationBuilder.CreateIndex(
                name: "IX_ImportReceiptDetails_FinalResolutionVersionId",
                table: "ImportReceiptDetails",
                column: "FinalResolutionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingDiscrepancies_ImportReceiptDetailId",
                table: "ReceivingDiscrepancies",
                column: "ImportReceiptDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingDiscrepancies_ImportReceiptId_ImportReceiptDetailId",
                table: "ReceivingDiscrepancies",
                columns: new[] { "ImportReceiptId", "ImportReceiptDetailId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingObservationItems_ReceivingObservationVersionId",
                table: "ReceivingObservationItems",
                column: "ReceivingObservationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingObservationItems_SupersededByObservationVersionId",
                table: "ReceivingObservationItems",
                column: "SupersededByObservationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingObservationVersions_PreviousObservationVersionId",
                table: "ReceivingObservationVersions",
                column: "PreviousObservationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingObservationVersions_ReceivingDiscrepancyId_Version",
                table: "ReceivingObservationVersions",
                columns: new[] { "ReceivingDiscrepancyId", "Version" },
                unique: true);

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
                name: "IX_ReceivingResolutionVersions_PreviousResolutionVersionId",
                table: "ReceivingResolutionVersions",
                column: "PreviousResolutionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingResolutionVersions_ReceivingDiscrepancyId_Version",
                table: "ReceivingResolutionVersions",
                columns: new[] { "ReceivingDiscrepancyId", "Version" },
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
                name: "IX_ReceivingTolerancePolicies_SupplierId",
                table: "ReceivingTolerancePolicies",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingTolerancePolicies_WarehouseId",
                table: "ReceivingTolerancePolicies",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportReceiptDetails_ReceivingResolutionVersions_FinalResolutionVersionId",
                table: "ImportReceiptDetails",
                column: "FinalResolutionVersionId",
                principalTable: "ReceivingResolutionVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportReceiptDetails_ReceivingResolutionVersions_FinalResolutionVersionId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropTable(
                name: "ReceivingObservationItems");

            migrationBuilder.DropTable(
                name: "ReceivingReasonCodes");

            migrationBuilder.DropTable(
                name: "ReceivingResolutionVersions");

            migrationBuilder.DropTable(
                name: "ReceivingTolerancePolicies");

            migrationBuilder.DropTable(
                name: "ReceivingObservationVersions");

            migrationBuilder.DropTable(
                name: "ReceivingDiscrepancies");

            migrationBuilder.DropIndex(
                name: "IX_ImportReceiptDetails_FinalResolutionVersionId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseDoorRejectedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseFinalReceivedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseObservedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "DoorRejectedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "FinalReceivedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "FinalResolutionVersionId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "ObservedQuantity",
                table: "ImportReceiptDetails");
        }
    }
}
