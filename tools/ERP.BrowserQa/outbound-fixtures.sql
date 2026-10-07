-- Only executed through the existing exact marker/Run ID guarded helper.
SET NOCOUNT ON;
BEGIN TRAN;
UPDATE Units SET Name=N'Cái' WHERE Code='EA';
UPDATE Products SET Name=N'Sản phẩm kiểm thử mã' WHERE Code='LOOKUP-COLLIDE';
UPDATE Products SET Name=N'Sản phẩm kiểm thử quét' WHERE Code='BARCODE-TARGET';
DECLARE @warehouse int=(SELECT Id FROM Warehouses WHERE Code='QA-WH01'),
 @readerRole int=(SELECT Id FROM Roles WHERE RoleName='QAReader'),
 @admin int=(SELECT Id FROM Users WHERE Username='qa_admin_browser');
INSERT RolePermissions(RoleId,PermissionId,GrantedAt)
 SELECT @readerRole,Id,SYSUTCDATETIME() FROM Permissions WHERE Code IN ('export_receipt.read','export_receipt.dispatch');
UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE Id=@readerRole;
INSERT Units(Code,Name,DecimalPlaces,IsActive,CreatedAt) VALUES('OUT-EA',N'Cái',4,1,SYSUTCDATETIME());
DECLARE @unit int=SCOPE_IDENTITY();
INSERT Products(Code,Name,UnitId,IsActive,CreatedAt) VALUES('OUT-QA',N'Sản phẩm kiểm thử xuất kho',@unit,1,SYSUTCDATETIME());
DECLARE @product int=SCOPE_IDENTITY();
INSERT InventoryStocks(ProductId,WarehouseId,LocationId,Quantity,ReservedQuantity,Status,LastUpdated)
 SELECT @product,@warehouse,Id,CASE WHEN Code='STORAGE-A' THEN 40 ELSE 60 END,0,0,SYSUTCDATETIME() FROM WarehouseLocations
 WHERE WarehouseId=@warehouse AND Code IN ('STORAGE-A','STORAGE-B');
INSERT Products(Code,Name,UnitId,IsActive,CreatedAt) VALUES('OUT-INELIGIBLE',N'Sản phẩm không được xuất',@unit,1,SYSUTCDATETIME());
DECLARE @ineligible int=SCOPE_IDENTITY();
INSERT WarehouseLocations(WarehouseId,Code,Name,LocationType,IsActive,IsBlocked,IsPickable,IsReceivable,IsSystemManaged,CreatedAt,CreatedBy) VALUES
(@warehouse,'OUT-RECEIVING',N'Vị trí nhận kiểm thử',0,1,0,1,0,0,SYSUTCDATETIME(),@admin),
(@warehouse,'OUT-INACTIVE',N'Vị trí không hoạt động',1,0,0,1,0,0,SYSUTCDATETIME(),@admin),
(@warehouse,'OUT-BLOCKED',N'Vị trí bị khóa',1,1,1,1,0,0,SYSUTCDATETIME(),@admin),
(@warehouse,'OUT-NONPICK',N'Vị trí không thể lấy hàng',1,1,0,0,0,0,SYSUTCDATETIME(),@admin);
INSERT InventoryStocks(ProductId,WarehouseId,LocationId,Quantity,ReservedQuantity,Status,LastUpdated)
SELECT @ineligible,@warehouse,Id,100,0,0,SYSUTCDATETIME() FROM WarehouseLocations WHERE WarehouseId=@warehouse AND Code LIKE 'OUT-%';
INSERT InventoryStocks(ProductId,WarehouseId,LocationId,Quantity,ReservedQuantity,Status,LastUpdated)
SELECT @ineligible,@warehouse,Id,100,0,CASE WHEN Code='DAMAGED-A' THEN 1 ELSE 2 END,SYSUTCDATETIME() FROM WarehouseLocations WHERE WarehouseId=@warehouse AND Code IN ('DAMAGED-A','REJECTED-A');
COMMIT;
SELECT @product AS product,@warehouse AS warehouse,@unit AS unit,@ineligible AS ineligible FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
