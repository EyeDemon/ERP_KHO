using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSpGetInventoryInOutReportDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sp = @"
ALTER PROCEDURE sp_GetInventoryInOutReport
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL,
    @WarehouseId INT = NULL,
    @ProductId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        p.Id AS ProductId,
        p.Code AS ProductCode,
        p.Name AS ProductName,
        ISNULL(u.Name, '') AS UnitName,
        w.Id AS WarehouseId,
        w.Name AS WarehouseName,
        
        CAST(ISNULL(SUM(CASE 
            WHEN @FromDate IS NOT NULL AND t.TransactionDate < @FromDate THEN 
                CASE WHEN t.TransactionType IN (0, 2) THEN t.Quantity ELSE -t.Quantity END
            ELSE 0.0 
        END), 0.0) AS DECIMAL(18,4)) AS OpeningQuantity,
        
        CAST(ISNULL(SUM(CASE 
            WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) 
             AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate) THEN
                CASE WHEN t.TransactionType IN (0, 2) THEN t.Quantity ELSE 0.0 END
            ELSE 0.0 
        END), 0.0) AS DECIMAL(18,4)) AS InQuantity,
        
        CAST(ISNULL(SUM(CASE 
            WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) 
             AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate) THEN
                CASE WHEN t.TransactionType IN (1, 3) THEN t.Quantity ELSE 0.0 END
            ELSE 0.0 
        END), 0.0) AS DECIMAL(18,4)) AS OutQuantity

    INTO #TempReport
    FROM InventoryTransactions t
    JOIN Products p ON t.ProductId = p.Id
    LEFT JOIN Units u ON p.UnitId = u.Id
    JOIN Warehouses w ON t.WarehouseId = w.Id
    WHERE (@WarehouseId IS NULL OR t.WarehouseId = @WarehouseId)
      AND (@ProductId IS NULL OR t.ProductId = @ProductId)
      AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate)
    GROUP BY p.Id, p.Code, p.Name, u.Name, w.Id, w.Name;

    SELECT 
        ProductId,
        ProductCode,
        ProductName,
        UnitName,
        WarehouseId,
        WarehouseName,
        OpeningQuantity,
        InQuantity,
        OutQuantity,
        CAST((OpeningQuantity + InQuantity - OutQuantity) AS DECIMAL(18,4)) AS ClosingQuantity
    FROM #TempReport
    ORDER BY ProductCode, WarehouseName;
    
    DROP TABLE #TempReport;
END
";
            migrationBuilder.Sql(sp);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var downSp = @"
ALTER PROCEDURE sp_GetInventoryInOutReport
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL,
    @WarehouseId INT = NULL,
    @ProductId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        p.Id AS ProductId,
        p.Code AS ProductCode,
        p.Name AS ProductName,
        ISNULL(u.Name, '') AS UnitName,
        w.Id AS WarehouseId,
        w.Name AS WarehouseName,
        
        ISNULL(SUM(CASE 
            WHEN @FromDate IS NOT NULL AND t.TransactionDate < @FromDate THEN 
                CASE WHEN t.TransactionType IN (0, 2) THEN t.Quantity ELSE -t.Quantity END
            ELSE 0 
        END), 0) AS OpeningQuantity,
        
        ISNULL(SUM(CASE 
            WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) 
             AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate) THEN
                CASE WHEN t.TransactionType IN (0, 2) THEN t.Quantity ELSE 0 END
            ELSE 0 
        END), 0) AS InQuantity,
        
        ISNULL(SUM(CASE 
            WHEN (@FromDate IS NULL OR t.TransactionDate >= @FromDate) 
             AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate) THEN
                CASE WHEN t.TransactionType IN (1, 3) THEN t.Quantity ELSE 0 END
            ELSE 0 
        END), 0) AS OutQuantity

    INTO #TempReport
    FROM InventoryTransactions t
    JOIN Products p ON t.ProductId = p.Id
    LEFT JOIN Units u ON p.UnitId = u.Id
    JOIN Warehouses w ON t.WarehouseId = w.Id
    WHERE (@WarehouseId IS NULL OR t.WarehouseId = @WarehouseId)
      AND (@ProductId IS NULL OR t.ProductId = @ProductId)
      AND (@ToDate IS NULL OR t.TransactionDate <= @ToDate)
    GROUP BY p.Id, p.Code, p.Name, u.Name, w.Id, w.Name;

    SELECT 
        ProductId,
        ProductCode,
        ProductName,
        UnitName,
        WarehouseId,
        WarehouseName,
        OpeningQuantity,
        InQuantity,
        OutQuantity,
        (OpeningQuantity + InQuantity - OutQuantity) AS ClosingQuantity
    FROM #TempReport
    ORDER BY ProductCode, WarehouseName;
    
    DROP TABLE #TempReport;
END
";
            migrationBuilder.Sql(downSp);        }
    }
}
