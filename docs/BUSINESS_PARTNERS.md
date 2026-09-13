# Business partners

## Scope and rules

`BusinessPartner` is the shared master record for suppliers and customers. A record has an immutable, trimmed, upper-case `Code` (1–50 ASCII letters, digits, `.`, `_`, `-`), a required trimmed `Name` (1–200), at least one of `IsSupplier` or `IsCustomer`, an active flag, and optional phone, email, and address. Codes use a case-insensitive SQL Server unique index. Contact fields are descriptive and are not unique.

Inactive partners remain visible on existing receipts but cannot be newly assigned. A referenced supplier/customer role cannot be removed, and referenced partners cannot be deleted. SQL foreign keys use `RESTRICT`; mutations and assignments use serializable transactions so concurrent role/status changes cannot bypass validation.

| Business rule | Implementation | Evidence |
|---|---|---|
| One record may have both roles | role check in service and database check constraint | application and SQL constraint tests |
| Code is stable and unique | normalized create, immutable update, CI unique index | service/API and concurrent SQL tests |
| Only active matching roles may be assigned | create repository validation and dedicated assignment service | application/API tests |
| Legacy clients preserve links | new create fields are optional; updates use dedicated endpoints | DTO compatibility tests |
| Closed history does not change on rename | Code/Name snapshot captured when import is approved and when export first leaves Draft | workflow SQL tests |
| Warehouse access remains scoped | assignment passes through `IWarehouseAuthorizationService` | HTTP authorization tests |

## API contract

All authenticated roles may read partner master data. Admin and Manager may mutate it. Receipt endpoints retain their existing roles and warehouse scope.

`GET /api/business-partners?page=1&pageSize=20&search=ACME&role=supplier&active=true` returns the normal `PagedResult` shape. `role` accepts `supplier` or `customer`.

`POST /api/business-partners` and `PUT /api/business-partners/{id}` use:

```json
{"code":" sup-01 ","name":"Nhà cung cấp mẫu","isSupplier":true,"isCustomer":false,"isActive":true,"phone":null,"email":null,"address":null,"rowVersion":null}
```

The stored code is `SUP-01`. Update must repeat the immutable code and must send the last returned Base64 `rowVersion` (exactly eight decoded bytes). Missing, null, malformed, or stale tokens return HTTP 409 and the UI instructs the user to reload; the server never silently falls back to last-write-wins. Validation is reported as the repository's Vietnamese business-rule response. `DELETE /api/business-partners/{id}` succeeds only for an unreferenced record.

Create receipt DTOs accept nullable `supplierId` or `customerId`. Existing clients that omit these fields create an unassociated receipt. Association changes are explicit and Draft-only:

```http
PUT /api/importreceipts/42/supplier
Content-Type: application/json

{"partnerId":7}
```

```http
PUT /api/exportreceipts/84/customer
Content-Type: application/json

{"partnerId":null}
```

`null` removes the association. An omitted new field in legacy receipt operations never clears an existing association.

## Snapshot, migration, and compatibility

Import approval copies supplier Code/Name onto the receipt immediately before status becomes `Approved`. Export approval copies customer Code/Name on the first transition from `Draft` to `Approved` or `Dispatched`. A direct Draft-to-Cancelled transition also captures the current Code/Name in the cancellation transaction. Later transitions from Approved keep the original snapshot. Draft DTOs show current partner data; all non-Draft DTOs show the snapshot. Old receipts remain nullable and are not backfilled with invented partners or snapshots.

Migration `AddBusinessPartnersAndReceiptAssociations` only adds the partner table, nullable receipt columns, indexes, checks, and restrictive foreign keys. It does not update inventory, reservation, ledger, document status, or historical migrations.

## Verification record

Checkpoint verification used owned `SQLEXPRESS` database Run ID `b89c766051b54f52b858706d124e3acf`: Application 323/323 passed, API 150/150 passed, and frontend 40/40 passed.

Follow-up verification on 2026-09-13 covered the mandatory row-version contract, Draft cancellation snapshots, and the remaining full-stack matrix. Application tests passed 324/324 on owned Run ID `a54e79228d1142fa9772a7e98bab8e0a`; API tests passed 150/150; frontend tests passed 41/41 with lint and production build passing. Browser and direct HTTP checks ran against loopback-only Run ID `bf813fe88b9d4e8a8a0ff0e1e0fcd770` with frontend → API → owned SQLEXPRESS: export customer assign/change/unlink, active/role validation, maker/checker, closed snapshot stability after partner rename, closed-link rejection, inactive linked supplier workflow, dual-role filters, Viewer 403, warehouse-B direct-ID denial with unchanged data, referenced/unreferenced deletion, and double-submit create behavior all passed. The verified export had one receipt, one inventory transaction, and one consumed reservation; no duplicate stock mutation was observed.

The browser target and SQL harness databases were removed after exact ownership checks. Final owned QA database inventory, target process inventory, and persisted synthetic credential inventory were all zero. Keyboard/browser automation verifies web behavior; it is not physical scanner-device certification.

Browser target Run ID `1a54e2cab31c4b8982095ba646889983` bound API/frontend to loopback and used synthetic accounts/data. Verified through the real frontend/API/SQL path: dual-role create and uppercase normalization; duplicate conflict; Import Supplier create and reload; checker approval; closed-receipt snapshot after partner rename; Viewer read UI and real HTTP mutation denial (403). The run also found and fixed missing CORS permission for `Idempotency-Key`.

Remaining browser matrix: Export Customer create/assign/remove/snapshot, inactive selection retention, referenced-delete UI, explicit remove on both receipt types, double-submit under network delay, and full role/warehouse permutations. Status: **TESTING INCOMPLETE**. Runtime artifacts and credentials are excluded from Git. Physical scanner behavior is represented by keyboard input only.
