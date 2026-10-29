using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessPartnersAndReceiptAssociations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierCodeSnapshot",
                table: "ImportReceipts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "ImportReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNameSnapshot",
                table: "ImportReceipts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCodeSnapshot",
                table: "ExportReceipts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "ExportReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameSnapshot",
                table: "ExportReceipts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessPartners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsSupplier = table.Column<bool>(type: "bit", nullable: false),
                    IsCustomer = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPartners", x => x.Id);
                    table.CheckConstraint("CK_BusinessPartners_Role", "[IsSupplier] = 1 OR [IsCustomer] = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportReceipts_SupplierId",
                table: "ImportReceipts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportReceipts_CustomerId",
                table: "ExportReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessPartners_Code",
                table: "BusinessPartners",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExportReceipts_BusinessPartners_CustomerId",
                table: "ExportReceipts",
                column: "CustomerId",
                principalTable: "BusinessPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ImportReceipts_BusinessPartners_SupplierId",
                table: "ImportReceipts",
                column: "SupplierId",
                principalTable: "BusinessPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExportReceipts_BusinessPartners_CustomerId",
                table: "ExportReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportReceipts_BusinessPartners_SupplierId",
                table: "ImportReceipts");

            migrationBuilder.DropTable(
                name: "BusinessPartners");

            migrationBuilder.DropIndex(
                name: "IX_ImportReceipts_SupplierId",
                table: "ImportReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ExportReceipts_CustomerId",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "SupplierCodeSnapshot",
                table: "ImportReceipts");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "ImportReceipts");

            migrationBuilder.DropColumn(
                name: "SupplierNameSnapshot",
                table: "ImportReceipts");

            migrationBuilder.DropColumn(
                name: "CustomerCodeSnapshot",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "CustomerNameSnapshot",
                table: "ExportReceipts");
        }
    }
}
