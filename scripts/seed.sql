USE ERP_KHO;

-- Xóa dữ liệu cũ để test sạch
DELETE FROM InventoryTransactions;
DELETE FROM ExportReceiptDetails;
DELETE FROM ExportReceipts;
DELETE FROM InventoryStocks;
DELETE FROM Products;
DELETE FROM Warehouses;
DELETE FROM Users;
DELETE FROM Roles;
DELETE FROM Units;

-- Tạo Warehouse và Product
SET IDENTITY_INSERT Warehouses ON;
INSERT INTO Warehouses (Id, Code, Name, IsActive, CreatedAt) VALUES (1, 'WH01', 'Kho Chính', 1, GETDATE());
SET IDENTITY_INSERT Warehouses OFF;

SET IDENTITY_INSERT Roles ON;
INSERT INTO Roles (Id, Code, Name, IsActive, CreatedAt) VALUES (1, 'ADMIN', 'Quản trị viên', 1, GETDATE());
SET IDENTITY_INSERT Roles OFF;

SET IDENTITY_INSERT Users ON;
INSERT INTO Users (Id, Username, PasswordHash, FullName, Email, RoleId, IsActive, CreatedAt) VALUES (1, 'admin_test', '73661BA6D439CB48F5339C9012B39765EF67C9256F5BE9BAC8E7CBEEC58A9207:DA98C9AC1CDFD3149F01513CC0103C2C:100000:SHA256', 'Admin', 'admin@test.com', 1, 1, GETDATE());
SET IDENTITY_INSERT Users OFF;

SET IDENTITY_INSERT Units ON;
INSERT INTO Units (Id, Code, Name, IsActive, CreatedAt) VALUES (1, 'CAI', 'Cái', 1, GETDATE());
SET IDENTITY_INSERT Units OFF;

SET IDENTITY_INSERT Categories ON;
INSERT INTO Categories (Id, Code, Name, IsActive, CreatedAt) VALUES (1, 'CAT01', 'Category 1', 1, GETDATE());
SET IDENTITY_INSERT Categories OFF;

SET IDENTITY_INSERT Products ON;
INSERT INTO Products (Id, Code, Name, UnitId, IsActive, CreatedAt) VALUES (1, 'SP01', 'Sản phẩm Test', 1, 1, GETDATE());
SET IDENTITY_INSERT Products OFF;

-- Tạo Tồn kho 20
SET IDENTITY_INSERT InventoryStocks ON;
INSERT INTO InventoryStocks (Id, ProductId, WarehouseId, Quantity, LastUpdated) VALUES (1, 1, 1, 20, GETDATE());
SET IDENTITY_INSERT InventoryStocks OFF;

-- Tạo 5 Phiếu Xuất Nháp (Mỗi phiếu yêu cầu 10 cái -> Tổng 50 cái, nhưng tồn chỉ 20, nên chỉ 2 phiếu thành công, 3 phiếu sẽ 409 hoặc lỗi tồn)
SET IDENTITY_INSERT ExportReceipts ON;
INSERT INTO ExportReceipts (Id, Code, WarehouseId, Status, CreatedAt, CreatedBy) VALUES (1, 'EX-T01', 1, 0, GETDATE(), 1);
INSERT INTO ExportReceipts (Id, Code, WarehouseId, Status, CreatedAt, CreatedBy) VALUES (2, 'EX-T02', 1, 0, GETDATE(), 1);
INSERT INTO ExportReceipts (Id, Code, WarehouseId, Status, CreatedAt, CreatedBy) VALUES (3, 'EX-T03', 1, 0, GETDATE(), 1);
INSERT INTO ExportReceipts (Id, Code, WarehouseId, Status, CreatedAt, CreatedBy) VALUES (4, 'EX-T04', 1, 0, GETDATE(), 1);
INSERT INTO ExportReceipts (Id, Code, WarehouseId, Status, CreatedAt, CreatedBy) VALUES (5, 'EX-T05', 1, 0, GETDATE(), 1);
SET IDENTITY_INSERT ExportReceipts OFF;

SET IDENTITY_INSERT ExportReceiptDetails ON;
INSERT INTO ExportReceiptDetails (Id, ExportReceiptId, ProductId, Quantity, UnitPrice) VALUES (1, 1, 1, 10, 100);
INSERT INTO ExportReceiptDetails (Id, ExportReceiptId, ProductId, Quantity, UnitPrice) VALUES (2, 2, 1, 10, 100);
INSERT INTO ExportReceiptDetails (Id, ExportReceiptId, ProductId, Quantity, UnitPrice) VALUES (3, 3, 1, 10, 100);
INSERT INTO ExportReceiptDetails (Id, ExportReceiptId, ProductId, Quantity, UnitPrice) VALUES (4, 4, 1, 10, 100);
INSERT INTO ExportReceiptDetails (Id, ExportReceiptId, ProductId, Quantity, UnitPrice) VALUES (5, 5, 1, 10, 100);
SET IDENTITY_INSERT ExportReceiptDetails OFF;
