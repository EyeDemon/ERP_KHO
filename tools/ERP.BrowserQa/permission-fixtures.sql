-- Synthetic fixtures only, executed after exact BrowserQA marker verification.
SET NOCOUNT ON;
BEGIN TRAN;
DECLARE @admin int=(SELECT Id FROM Users WHERE Username='qa_admin_browser'),
 @manager int=(SELECT Id FROM Users WHERE Username='qa_manager_browser'),
 @viewer int=(SELECT Id FROM Users WHERE Username='qa_viewer_browser'),
 @warehouse int=(SELECT Id FROM Warehouses WHERE Code='QA-WH01'),
 @foreignWarehouse int=(SELECT Id FROM Warehouses WHERE Code='QA-WH02'),
 @adminRole int=(SELECT Id FROM Roles WHERE RoleName='Admin');
IF EXISTS(SELECT 1 FROM Users WHERE Username='qa_reader_browser') THROW 51010,'Fresh browser fixtures required.',1;
INSERT Roles(RoleName,Description) VALUES('QAReader',N'Chỉ xem phiếu nhập');
DECLARE @readerRole int=SCOPE_IDENTITY();
INSERT RolePermissions(RoleId,PermissionId,GrantedAt) SELECT @readerRole,Id,SYSUTCDATETIME() FROM Permissions WHERE Code='receipt.read';
INSERT Users(Username,PasswordHash,FullName,RoleId,IsActive,CreatedAt,FailedLoginCount,WarehouseAccessRevision,SecurityRevision)
 SELECT 'qa_reader_browser',PasswordHash,N'Người xem phiếu nhập',@readerRole,1,SYSUTCDATETIME(),0,0,0 FROM Users WHERE Id=@viewer;
DECLARE @reader int=SCOPE_IDENTITY();
INSERT UserWarehouses(UserId,WarehouseId,CreatedAt,CreatedBy) VALUES(@reader,@warehouse,SYSUTCDATETIME(),@admin);
INSERT Users(Username,PasswordHash,FullName,RoleId,IsActive,CreatedAt,FailedLoginCount,WarehouseAccessRevision,SecurityRevision,LockoutEnd)
 SELECT 'qa_locked_admin',PasswordHash,N'Quản trị viên đang khóa',@adminRole,1,SYSUTCDATETIME(),0,0,0,DATEADD(hour,1,SYSUTCDATETIME()) FROM Users WHERE Id=@admin;
DECLARE @i int=1,@pending int,@foreign int;
WHILE @i<=15
BEGIN
 INSERT ImportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt,Note)
 VALUES(CONCAT('QA-PENDING-',RIGHT(CONCAT('0',@i),2)),CASE WHEN @i<=12 THEN @warehouse ELSE @foreignWarehouse END,4,@admin,SYSUTCDATETIME(),N'Chứng từ tổng hợp kiểm thử');
 DECLARE @receipt int=SCOPE_IDENTITY();
 IF @i=1 SET @pending=@receipt;
 IF @i=13 SET @foreign=@receipt;
 INSERT AuditLogs(UserId,Action,EntityName,EntityId,Timestamp,OldValues,NewValues,WarehouseId,Result,Severity)
 VALUES(@admin,'ImportReceipt.Received','ImportReceipt',@receipt,SYSUTCDATETIME(),'Status: Draft','Status: Received',CASE WHEN @i<=12 THEN @warehouse ELSE @foreignWarehouse END,'Success','Info');
 SET @i=@i+1;
END;
INSERT ExportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt,Note) VALUES('QA-OUTBOUND',@warehouse,0,@admin,SYSUTCDATETIME(),N'Ngoài phạm vi cutover');
INSERT AuditLogs(UserId,Action,EntityName,EntityId,Timestamp,WarehouseId,Result,Severity) VALUES(@admin,'ExportReceipt.Created','ExportReceipt',SCOPE_IDENTITY(),SYSUTCDATETIME(),@warehouse,'Success','Info');
COMMIT;
SELECT @admin AS admin,@manager AS manager,@viewer AS viewer,@reader AS reader,@readerRole AS readerRole,@adminRole AS adminRole,
 (SELECT Id FROM Roles WHERE RoleName='Manager') AS managerRole,(SELECT Id FROM Roles WHERE RoleName='WarehouseStaff') AS staffRole,
 @warehouse AS warehouse,@foreignWarehouse AS foreignWarehouse,@pending AS pending,@foreign AS [foreign],
 (SELECT Id FROM ImportReceipts WHERE Code='QA-PENDING-02') AS checkerPending,
 (SELECT TOP 1 Id FROM ImportReceipts WHERE Status=6) AS posted,
 (SELECT TOP 1 Id FROM PutawayTasks ORDER BY Id) AS task,
 (SELECT Id FROM WarehouseLocations WHERE WarehouseId=@warehouse AND Code='STORAGE-A') AS storage
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
