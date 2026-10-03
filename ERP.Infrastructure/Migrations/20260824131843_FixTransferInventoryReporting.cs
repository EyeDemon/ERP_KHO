using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixTransferInventoryReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE sp_GetInventoryInOutReport
                    @FromDate DATETIME2 = NULL,
                    @ToDate DATETIME2 = NULL,
                    @WarehouseId INT = NULL,
                    @ProductId INT = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;

                    -- TransactionType: Import=0, Export=1, AdjustmentIncrease=2,
                    -- AdjustmentDecrease=3, TransferOut=4, TransferIn=5.
                    IF EXISTS
                    (
                        SELECT 1
                        FROM InventoryTransactions t
                        WHERE t.TransactionType NOT IN (0, 1, 2, 3, 4, 5)
                          AND (@WarehouseId IS NULL OR t.WarehouseId = @WarehouseId)
                          AND (@ProductId IS NULL OR t.ProductId = @ProductId)
                          AND (@ToDate IS NULL OR t.TransactionDate < @ToDate)
                    )
                        THROW 51000, 'Unsupported inventory transaction type in report range.', 1;

                    SELECT
                        p.Id AS ProductId,
                        p.Code AS ProductCode,
                        p.Name AS ProductName,
                        ISNULL(u.Name, '') AS UnitName,
                        w.Id AS WarehouseId,
                        w.Name AS WarehouseName,
                        CAST(ISNULL(SUM(CASE
                            WHEN @FromDate IS NOT NULL AND t.TransactionDate < @FromDate THEN
                                CASE
                                    WHEN t.TransactionType IN (0, 2, 5) THEN t.Quantity
                                    WHEN t.TransactionType IN (1, 3, 4) THEN -t.Quantity
                                    ELSE 0
                                END
                            ELSE 0
                        END), 0) AS DECIMAL(18,4)) AS OpeningQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType = 0 THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS ImportQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType = 5 THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS TransferInQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType = 2 THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS AdjustmentIncreaseQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType IN (0, 2, 5) THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS InQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType = 1 THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS ExportQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType = 4 THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS TransferOutQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType = 3 THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS AdjustmentDecreaseQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate < @ToDate) AND t.TransactionType IN (1, 3, 4) THEN t.Quantity ELSE 0 END), 0) AS DECIMAL(18,4)) AS OutQuantity
                    INTO #TempReport
                    FROM InventoryTransactions t
                    JOIN Products p ON t.ProductId = p.Id
                    LEFT JOIN Units u ON p.UnitId = u.Id
                    JOIN Warehouses w ON t.WarehouseId = w.Id
                    WHERE (@WarehouseId IS NULL OR t.WarehouseId = @WarehouseId)
                      AND (@ProductId IS NULL OR t.ProductId = @ProductId)
                      AND (@ToDate IS NULL OR t.TransactionDate < @ToDate)
                    GROUP BY p.Id, p.Code, p.Name, u.Name, w.Id, w.Name;

                    SELECT *, CAST(OpeningQuantity + InQuantity - OutQuantity AS DECIMAL(18,4)) AS ClosingQuantity
                    FROM #TempReport
                    ORDER BY ProductCode, WarehouseName;

                    DROP TABLE #TempReport;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER PROCEDURE sp_GetInventoryInOutReport
                    @FromDate DATETIME2 = NULL,
                    @ToDate DATETIME2 = NULL,
                    @WarehouseId INT = NULL,
                    @ProductId INT = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT p.Id AS ProductId, p.Code AS ProductCode, p.Name AS ProductName,
                        ISNULL(u.Name, '') AS UnitName, w.Id AS WarehouseId, w.Name AS WarehouseName,
                        CAST(ISNULL(SUM(CASE WHEN @FromDate IS NOT NULL AND t.TransactionDate < @FromDate THEN CASE WHEN t.TransactionType IN (0, 2) THEN t.Quantity ELSE -t.Quantity END ELSE 0 END), 0) AS DECIMAL(18,4)) AS OpeningQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate) THEN CASE WHEN t.TransactionType IN (0, 2) THEN t.Quantity ELSE 0 END ELSE 0 END), 0) AS DECIMAL(18,4)) AS InQuantity,
                        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate) THEN CASE WHEN t.TransactionType IN (1, 3) THEN t.Quantity ELSE 0 END ELSE 0 END), 0) AS DECIMAL(18,4)) AS OutQuantity
                    INTO #TempReport
                    FROM InventoryTransactions t
                    JOIN Products p ON t.ProductId = p.Id
                    LEFT JOIN Units u ON p.UnitId = u.Id
                    JOIN Warehouses w ON t.WarehouseId = w.Id
                    WHERE (@WarehouseId IS NULL OR t.WarehouseId = @WarehouseId)
                      AND (@ProductId IS NULL OR t.ProductId = @ProductId)
                      AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate)
                    GROUP BY p.Id, p.Code, p.Name, u.Name, w.Id, w.Name;
                    SELECT ProductId, ProductCode, ProductName, UnitName, WarehouseId, WarehouseName,
                        OpeningQuantity, InQuantity, OutQuantity,
                        CAST(OpeningQuantity + InQuantity - OutQuantity AS DECIMAL(18,4)) AS ClosingQuantity
                    FROM #TempReport ORDER BY ProductCode, WarehouseName;
                    DROP TABLE #TempReport;
                END
                """);
        }
    }
}
