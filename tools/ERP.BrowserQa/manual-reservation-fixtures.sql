-- Executed only through the existing exact marker/Run ID guarded BrowserQA helper.
SET NOCOUNT ON;
BEGIN TRAN;
DECLARE @warehouse int=(SELECT Id FROM Warehouses WHERE Code='QA-WH01'),
 @foreign int=(SELECT Id FROM Warehouses WHERE Code='QA-WH02'),
 @admin int=(SELECT Id FROM Users WHERE Username='qa_admin_browser'),
 @readerRole int=(SELECT Id FROM Roles WHERE RoleName='QAReader');
INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
 SELECT @readerRole,Id,SYSUTCDATETIME(),@admin FROM Permissions WHERE Code='reservation.read';
UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE Id=@readerRole;
-- Login once as Admin; the case downgrades the database role before exercising the old JWT.
UPDATE Users SET RoleId=(SELECT Id FROM Roles WHERE RoleName='Admin') WHERE Username='qa_reader_browser';
UPDATE Units SET Name=N'Cái' WHERE Code='EA';
UPDATE Products SET Name=N'Sản phẩm kiểm thử mã' WHERE Code='LOOKUP-COLLIDE';
UPDATE Products SET Name=N'Sản phẩm kiểm thử quét' WHERE Code='BARCODE-TARGET';
INSERT Units(Code,Name,DecimalPlaces,IsActive,CreatedAt) VALUES('MR-EA',N'Cái giữ hàng',4,1,SYSUTCDATETIME());
DECLARE @unit int=SCOPE_IDENTITY();
INSERT Products(Code,Name,UnitId,IsActive,CreatedAt) VALUES('MR-QA',N'Sản phẩm kiểm thử giữ hàng',@unit,1,SYSUTCDATETIME());
DECLARE @product int=SCOPE_IDENTITY();
INSERT InventoryStocks(ProductId,WarehouseId,LocationId,Quantity,ReservedQuantity,Status,LastUpdated)
 SELECT @product,WarehouseId,Id,100,0,0,SYSUTCDATETIME() FROM WarehouseLocations
 WHERE (WarehouseId=@warehouse AND Code='STORAGE-A') OR (WarehouseId=@foreign AND Code='LEGACY');
INSERT Products(Code,Name,UnitId,IsActive,CreatedAt) VALUES('MR-INELIGIBLE',N'Sản phẩm không được giữ',@unit,1,SYSUTCDATETIME());
DECLARE @ineligible int=SCOPE_IDENTITY();
INSERT WarehouseLocations(WarehouseId,Code,Name,LocationType,IsActive,IsBlocked,IsPickable,IsReceivable,IsSystemManaged,CreatedAt,CreatedBy) VALUES
(@warehouse,'MR-RECEIVING',N'Vị trí nhận kiểm thử',0,1,0,1,0,0,SYSUTCDATETIME(),@admin),
(@warehouse,'MR-INACTIVE',N'Vị trí không hoạt động',1,0,0,1,0,0,SYSUTCDATETIME(),@admin),
(@warehouse,'MR-BLOCKED',N'Vị trí bị khóa',1,1,1,1,0,0,SYSUTCDATETIME(),@admin),
(@warehouse,'MR-NONPICK',N'Vị trí không thể lấy hàng',1,1,0,0,0,0,SYSUTCDATETIME(),@admin);
INSERT InventoryStocks(ProductId,WarehouseId,LocationId,Quantity,ReservedQuantity,Status,LastUpdated)
 SELECT @ineligible,@warehouse,Id,100,0,0,SYSUTCDATETIME() FROM WarehouseLocations WHERE WarehouseId=@warehouse AND Code LIKE 'MR-%';
INSERT InventoryStocks(ProductId,WarehouseId,LocationId,Quantity,ReservedQuantity,Status,LastUpdated)
 SELECT @ineligible,@warehouse,Id,100,0,CASE WHEN Code='DAMAGED-A' THEN 1 ELSE 2 END,SYSUTCDATETIME() FROM WarehouseLocations WHERE WarehouseId=@warehouse AND Code IN ('DAMAGED-A','REJECTED-A');
COMMIT;
SELECT @product AS product,@unit AS unit,@warehouse AS warehouse,@foreign AS foreignWarehouse,@ineligible AS ineligible FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
