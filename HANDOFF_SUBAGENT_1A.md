# HANDOFF REPORT — SUBAGENT 1A (DB Migration & Schema Architect)

## 1. Summary of Completed Work (`[P0-01]` & `OD-02` + Round 4 Gate 2 SQL Harness Causes C & D)

1. **Removed Runtime Raw DDL & Warning Suppression (`ERP.Infrastructure/Persistence/ErpKhoDbContext.cs`)**:
   - Completely removed `EnsureWave44SchemaSyncedIfNeeded()`, the 580+ line raw DDL string, static sync fields (`_wave44SchemaSyncedDatabases`, `_wave44SchemaSyncLock`), constructor call `EnsureWave44SchemaSyncedIfNeeded()` (`L17-L24`), and `OnConfiguring` (`PendingModelChangesWarning` suppression).
   - Configured `InventoryStock.GrainHash` as `HasMaxLength(32).IsRequired()` and added unique index `IX_InventoryStocks_Tenant_GrainHash` on `(TenantId, GrainHash)` in `OnModelCreating` (`ErpKhoDbContext.cs:787-793`).
   - Enforced `stockEntry.Entity.RefreshGrainHash()` (after synchronizing `LotId = resolvedLot.Id` and `SerialId = resolvedSerial.Id` when navigations are resolved) in `PrepareAndCollectMultiTenantChecks()` (`ErpKhoDbContext.cs:353-369`) after `TenantId` assignment and before `if (isBypass) return false;` so all `SaveChanges` / `SaveChangesAsync` paths guarantee a valid 32-byte SHA-256 `GrainHash`.

2. **Implemented Real 32-Byte SHA-256 `GrainHash` (`ERP.Domain/Entities/InventoryStock.cs`)**:
   - Implemented `RefreshGrainHash()` (`InventoryStock.cs:127-135`) computing 32-byte SHA-256 over `$"{TenantId}|{WarehouseId}|{ProductId}|{(int)Status}|{LocationId ?? -1}|{lotPart}|{serialPart}|{HandlingUnitId ?? -1}|{OwnerId ?? -1}"`, including fallback to `Lot.Id`/`LOT:{Lot.LotNumber}` and `Serial.Id`/`SER:{Serial.SerialNumber}` when navigation properties are populated before `LotId`/`SerialId` are assigned.
   - Wired `RefreshGrainHash()` into the parameterless constructor (`InventoryStock.cs:18-21`), all 9 grain property setters (`TenantId`, `ProductId`, `WarehouseId`, `LocationId`, `LotId`, `SerialId`, `HandlingUnitId`, `OwnerId`, `Status` at `InventoryStock.cs:25-113`), and the `Lot` / `Serial` navigation property setters (`InventoryStock.cs:158-184`), while preserving `ComputeCanonicalGrainHash(...)` (`InventoryStock.cs:137-152`) for existing test callers (`ReportsControllerTests.cs:5715`).

3. **Fixed Round 4 Gate 2 SQL Harness Causes C.1, C.2 & D**:
   - **Cause C.1 (`ERP.Infrastructure/Services/StockTransferService.cs:567-575`)**: Updated the raw SQL `MERGE INTO InventoryStocks WITH (HOLDLOCK) ... WHEN NOT MATCHED THEN INSERT` statement to populate `GrainHash` using `CAST(HASHBYTES('SHA2_256', CONVERT(varchar(256), CONCAT(source.TenantId, '|', source.WarehouseId, '|', source.ProductId, '|', source.Status, '|', ISNULL(source.LocationId, -1), '|-1|-1|-1|-1'))) AS varbinary(32))` alongside `LotId, SerialId, HandlingUnitId, AllocatedQuantity, LockedQuantity`.
   - **Cause C.2 (`ERP.Infrastructure/Services/ReceiptInventoryIdentityService.cs:147-222`)**: Persisted newly added `InventoryLot` (`if (lot is { Id: 0 }) await context.SaveChangesAsync(cancellationToken);`) and `InventorySerial` (`await context.SaveChangesAsync(cancellationToken);`) before querying or creating `InventoryStock`, and assigned `LotId = lot?.Id` and `SerialId = serial?.Id` explicitly on `InventoryStock` so multiple lots/serials in the same receipt get distinct SQL IDs (`> 0`) and distinct `GrainHash` values.
   - **Cause D (`ERP.Infrastructure/Migrations/20261023000000_Wave53FullSchemaSnapshotAndFkParity.cs:705-781` & `ERP.Infrastructure/Migrations/20261022120000_Wave48To51FullFiveLayerClosure.cs:589-618`)**:
     - Implemented idempotent `Down(MigrationBuilder)` in `20261023000000_Wave53FullSchemaSnapshotAndFkParity.cs` to drop `IX_InventoryStocks_Tenant_GrainHash`, alter `InventoryStocks.GrainHash` to `NULL`, and drop all Wave 45–53 foreign keys added in `20261023000000`.
     - Implemented idempotent `Down(MigrationBuilder)` in `20261022120000_Wave48To51FullFiveLayerClosure.cs` to drop all Wave 44–51 tables in child-first, parent-second order (`ShipmentFinancialLedgers`, `HandoverManifestLines`, `HandoverManifests`, `PackingVideoEvidences`, `CustomerReturnLines`, `CustomerReturns`, `StocktakeAdjustmentProposals`, `PromotionGiftRules`, `SupportImpersonationSessions`, `CarrierLocationMappings`, `VnAdministrativeUnits`, `ChannelSkuMappings`, `MarketplaceShops`, `ProductBundleComponents`, `SaasBillingInvoices`, `TenantSubscriptions`, `SaasPlans`, `IntegrationOutboxMessages`, `IntegrationInboxMessages`) so rollback tests can cleanly drop `Shipments`, `ExportReceipts`, `SalesOrders`, `Products`, and `Warehouses`.

4. **Completed FK & Index Configurations (`ERP.Infrastructure/Persistence/Configurations/Wave45OmnichannelAndSaasConfigurations.cs`)**:
   - Completed all missing FK configurations (`HasOne` / `WithMany` / `HasForeignKey` / `OnDelete`) on `PackingVideoEvidenceConfiguration` (`L264-L281`), `PromotionGiftRuleConfiguration` (`L303-L314`), `CustomerReturnConfiguration` (`L370-L405`), `CustomerReturnLineConfiguration` (`L427-L462`), and `StocktakeAdjustmentProposalConfiguration` (`L493-L534`).

## 2. Files Modified / Created

| File | Action | Key Line Coordinates |
|---|---|---|
| `ERP.Domain/Entities/InventoryStock.cs` | Modified | `L8-L113`, `L127-L135` (`RefreshGrainHash()`), `L158-L184` (`Lot`/`Serial` setters) |
| `ERP.Infrastructure/Persistence/ErpKhoDbContext.cs` | Modified | `L17-L24` (ctor cleanup), `L353-L369` (`RefreshGrainHash` in `PrepareAndCollectMultiTenantChecks`), `L787-L793` (`GrainHash` & `IX_InventoryStocks_Tenant_GrainHash` config) |
| `ERP.Infrastructure/Services/StockTransferService.cs` | Modified | `L567-L575` (`MERGE INTO InventoryStocks` with `GrainHash`) |
| `ERP.Infrastructure/Services/ReceiptInventoryIdentityService.cs` | Modified | `L147-L222` (`SaveChangesAsync` for new `InventoryLot`/`InventorySerial` & `LotId`/`SerialId` assignment) |
| `ERP.Infrastructure/Persistence/Configurations/Wave45OmnichannelAndSaasConfigurations.cs` | Modified | `L264-L281`, `L303-L314`, `L370-L405`, `L427-L462`, `L493-L534` (FK configurations) |
| `ERP.Infrastructure/Migrations/20261022120000_Wave48To51FullFiveLayerClosure.cs` | Modified | `L13-L618` (`Up` idempotent DDL & complete child-first `Down` table drops) |
| `ERP.Infrastructure/Migrations/20261023000000_Wave53FullSchemaSnapshotAndFkParity.cs` | Created/Modified | `L1-L781` (Wave 53 migration `Up` & complete FK/index `Down`) |
| `ERP.Infrastructure/Migrations/20261023000000_Wave53FullSchemaSnapshotAndFkParity.Designer.cs` | Created | `L1-L9423` (Migration attribute & full target model) |
| `ERP.Infrastructure/Migrations/ErpKhoDbContextModelSnapshot.cs` | Modified | `L1-L9420` (Full 100% synchronized EF Core model snapshot) |
| `HANDOFF_SUBAGENT_1A.md` | Updated | Handoff report |

## 3. Verification Results

- **Encoding Gate**: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1` passed with Exit Code `0`.
- **Unfiltered `IMigrationsModelDiffer` Parity Check**: `0` differences across the entire relational model.
