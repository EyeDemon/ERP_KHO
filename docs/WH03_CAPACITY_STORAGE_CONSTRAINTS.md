# WH-03 — Capacity & Storage Constraints

Status: **LIVE / PRODUCTION**

Production route: `/warehouse-structure`

## Production data model

### Product storage profile

Capacity metrics are defined per base UOM:

- `StorageClass`
- `UnitWeightKg`
- `UnitVolumeM3`
- `UnitPalletEquivalent`

All fields are nullable for legacy compatibility. Positive numeric values are enforced when present.

### WarehouseLocation constraints

- `StorageClass`
- `MaxWeightKg`
- `MaxVolumeM3`
- `MaxPalletEquivalent`

Constraint updates reuse the existing WarehouseLocation rowversion contract. System-managed RECEIVING/LEGACY locations cannot receive user-managed capacity constraints.

## Capacity dashboard

`GET /api/putaway-tasks/location-capacity?warehouseId=...`

The endpoint is warehouse-scoped and calculates current usage from `InventoryStocks.Quantity` multiplied by each Product base-UOM profile.

States:

- `Available`
- `NearCapacity` at 85% or above on any configured dimension
- `OverCapacity`
- `ProfileIncomplete`
- `CompatibilityConflict`
- `Blocked`
- `Inactive`

Legacy stock is never guessed. If an enabled constraint cannot be calculated because existing stock lacks the required product profile, the state is `ProfileIncomplete`.

## Putaway enforcement

Before a putaway movement mutates inventory:

1. lock the destination WarehouseLocation;
2. validate warehouse/status/location type;
3. convert entered quantity to base quantity;
4. lock/read the Product storage profile;
5. enforce Storage Class compatibility;
6. calculate projected weight / volume / pallet-equivalent using all positive stock already in the destination;
7. reject if profile data is insufficient for an enabled constraint;
8. reject if projected usage exceeds any configured maximum;
9. only then mutate source/destination InventoryStock and append the immutable movement.

Concurrent moves to the same destination are serialized by the location lock.

## Compatibility

- Existing Products and Locations may keep all WH-03 fields null.
- Legacy Product update payloads preserve the current storage profile unless `UpdateStorageProfile=true`.
- Legacy WarehouseLocation update payloads preserve constraints unless `UpdateConstraints=true`.
- Null Storage Class means no class restriction.
- Null capacity means that dimension is not limited.

## UI

The production warehouse page shows the capacity dashboard plus editable location constraints for users with `location.manage`.

The Products page exposes the base-UOM storage profile under existing product create/update permissions.

The Blueprint WH-03 panel remains a preview and is not used as the production data source.
