using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                newName: "IX_WarehouseCode");

            migrationBuilder.RenameIndex(
                name: "IX_Units_Code",
                table: "Units",
                newName: "IX_UnitCode");

            migrationBuilder.RenameIndex(
                name: "IX_Products_Code",
                table: "Products",
                newName: "IX_ProductCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_WarehouseCode",
                table: "Warehouses",
                newName: "IX_Warehouses_Code");

            migrationBuilder.RenameIndex(
                name: "IX_UnitCode",
                table: "Units",
                newName: "IX_Units_Code");

            migrationBuilder.RenameIndex(
                name: "IX_ProductCode",
                table: "Products",
                newName: "IX_Products_Code");
        }
    }
}
