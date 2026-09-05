BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [CorrelationId] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [DestinationWarehouseId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [IdempotencyKeyHash] nchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [Reason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [RequestFingerprint] nchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [Result] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [Severity] nvarchar(32) NOT NULL DEFAULT N'Information';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [SourceWarehouseId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [WarehouseId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    CREATE TABLE [IdempotencyRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [CommandScope] nvarchar(160) NOT NULL,
        [KeyHash] nchar(64) NOT NULL,
        [RequestFingerprint] nchar(64) NOT NULL,
        [Status] int NOT NULL,
        [ResponseStatusCode] int NULL,
        [ResponseBody] nvarchar(max) NULL,
        [CorrelationId] nvarchar(64) NOT NULL,
        [WarehouseId] int NULL,
        [SourceWarehouseId] int NULL,
        [DestinationWarehouseId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IdempotencyRecords_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_ExpiresAtUtc] ON [IdempotencyRecords] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_UserId_CommandScope_KeyHash] ON [IdempotencyRecords] ([UserId], [CommandScope], [KeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831053639_AddApprovalIdempotencyAndAuditEnrichment', N'10.0.9');
END;

COMMIT;
GO
