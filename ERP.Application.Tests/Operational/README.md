# Deferred Legacy Backup Test

Status: DEFERRED_BY_OWNER / OPERATIONAL_TEST.

Original full name: ERP.Application.Tests.SqlServerWarehouseMigrationSafetyTests.PreMigration_BackupAndInventory.

The method body and assertions are preserved from HEAD 6c20b48505da6e9db141f84e6fc0f212363e344f. This separate project is NOT in ERP.slnx and its source is excluded from ERP.Application.Tests. The test also has an unconditional owner-deferred Fact and Operational/Backup/DeferredByOwner metadata. Do not execute it or count it as PASS.

Before any future execution: obtain fresh owner authorization for the exact database, backup path and operational window; review the legacy INIT option, path uniqueness, permissions, output redaction and nonempty ERP_KHO_RUN_MIGRATION_BACKUP_TEST gate. The historical body can otherwise return without executing. Do not remove the deferred marker or provide execution flags under the current authorization.

The no-backup safety regression is a DIFFERENT test. It proves boundaries, not backup capability. Backup automation remains paused, ERP_KHO SIMPLE, scheduled backup task Disabled.
