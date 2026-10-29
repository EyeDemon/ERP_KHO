# Warehouse authorization: resource-existence gate (2026-10-09)

## Correctness gap
`WarehouseAuthorizationService.CanAccessWarehouseAsync` previously returned `true` for every positive, zero or negative warehouse ID when the caller was a global administrator. This was a **membership bypass**, not a valid proof that a warehouse resource existed. Consumers of `EnsureWarehouseAccessAsync` could proceed with nonexistent warehouse IDs and fail later with misleading SQL FK errors instead of a uniform not-found response.

## Implemented
- Reject nonpositive warehouse IDs for all callers before database queries.
- For an authenticated global administrator, check `Warehouses.AnyAsync(Id == warehouseId)` before authorizing a single warehouse. Administrators still see all **existing** warehouses through `GetAccessibleWarehouseIdsAsync`.
- For ordinary users, retain the existing user-to-warehouse membership predicate. A missing or unassigned warehouse remains indistinguishable to the caller through `NotFoundException` (404), preventing warehouse enumeration.
- No schema changes, ledger writes, privilege grants, or changes to Notion/Blueprint status.

## Regression
`WarehouseAuthorizationExistenceTests` exercises existing, missing, zero and negative IDs in EF InMemory; SQL Server test creates an actual warehouse in a rolled-back transaction and verifies global-admin access, nonexistent warehouse rejection, and non-admin isolation.

## Release gate
This change is on Draft PR #31. CI and preview alone are not browser QA or staging API/SQL approval. Do not merge or promote Blueprint capabilities to `live` on this evidence alone.
