BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    -- Read-only preflight. This statement must remain before every DDL or data mutation.
    IF EXISTS (SELECT 1 FROM InventoryStocks WHERE Quantity < 0 OR ReservedQuantity < 0 OR ReservedQuantity > Quantity)
        THROW 51010, 'Unsafe export workflow migration: inventory contains negative or over-reserved stock.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM InventoryTransactions
        WHERE ReferenceType = 'ExportReceipt' AND TransactionType = 1 AND ReferenceId IS NOT NULL
        GROUP BY ReferenceId, ProductId, WarehouseId
        HAVING COUNT(*) > 1
    )
        THROW 51010, 'Unsafe export workflow migration: duplicate Export transactions exist.', 1;

    IF EXISTS
    (
        SELECT 1 FROM ExportReceipts e WHERE e.Status = 1 AND
        (
            NOT EXISTS (SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id)
            OR EXISTS (SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id AND d.Quantity <= 0)
            OR EXISTS
            (
                SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id AND 1 <>
                (SELECT COUNT(*) FROM InventoryTransactions t WHERE t.ReferenceType = 'ExportReceipt' AND t.ReferenceId = e.Id AND t.TransactionType = 1 AND t.ProductId = d.ProductId AND t.WarehouseId = e.WarehouseId AND t.Quantity = d.Quantity)
            )
            OR
            (
                (SELECT COUNT(*) FROM InventoryTransactions t WHERE t.ReferenceType = 'ExportReceipt' AND t.ReferenceId = e.Id AND t.TransactionType = 1)
                <> (SELECT COUNT(*) FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id)
            )
            OR EXISTS (SELECT 1 FROM StockReservations r WHERE r.SourceType = 'ExportReceipt' AND r.SourceId = e.Id AND r.Status IN (0, 1))
        )
    )
        THROW 51010, 'Unsafe legacy export receipt backfill: Approved receipt does not have an exact one-to-one Export transaction ledger or has an active reservation.', 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    ALTER TABLE [ExportReceipts] ADD [DispatchMode] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    ALTER TABLE [ExportReceipts] ADD [DispatchedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    ALTER TABLE [ExportReceipts] ADD [DispatchedBy] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    UPDATE ExportReceipts SET Status = 3, DispatchMode = 1, DispatchedAt = ApprovedAt, DispatchedBy = ApprovedBy WHERE Status = 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InventoryTransactions_ExportReceiptReference] ON [InventoryTransactions] ([ReferenceType], [ReferenceId], [TransactionType], [ProductId], [WarehouseId]) WHERE [ReferenceType] = ''ExportReceipt''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    CREATE INDEX [IX_ExportReceipts_DispatchedBy] ON [ExportReceipts] ([DispatchedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    ALTER TABLE [ExportReceipts] ADD CONSTRAINT [FK_ExportReceipts_Users_DispatchedBy] FOREIGN KEY ([DispatchedBy]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825104054_AddExportReceiptDispatchWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825104054_AddExportReceiptDispatchWorkflow', N'10.0.9');
END;

COMMIT;
GO

