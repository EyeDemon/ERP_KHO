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

Partner master actions require database `partner.read/create/update/deactivate` grants. Import supplier assignment requires `receipt.update`; reading supplier choices separately requires `partner.read`. Export customer assignment retains outbound compatibility authorization. Both assignment paths enforce warehouse scope before checking receipt state or partner validity, so a foreign direct ID returns isolated 404 without a state/partner oracle. See `ACCUMULATED_PR_OWNER_ACCEPTANCE_REVIEW.md` for the acceptance regression.

`GET /api/business-partners?page=1&pageSize=20&search=ACME&role=supplier&active=true` returns the normal `PagedResult` shape. `role` accepts `supplier` or `customer`.

`POST /api/business-partners` does not use a concurrency token:

```json
{"code":" sup-01 ","name":"Nhà cung cấp mẫu","isSupplier":true,"isCustomer":false,"isActive":true,"phone":null,"email":null,"address":null}
```

The stored code is `SUP-01`. `PUT /api/business-partners/{id}` must repeat the immutable code and send the valid Base64 `rowVersion` returned by the latest GET/create/update response:

```json
{"code":"SUP-01","name":"Nhà cung cấp mẫu cập nhật","isSupplier":true,"isCustomer":false,"isActive":true,"phone":null,"email":null,"address":null,"rowVersion":"AAAAAAAAB9E="}
```

The token shown above is only an example shape; a client must not copy, invent, or send `null` for it. The decoded token must be exactly eight bytes. Missing, null, malformed, or stale tokens return HTTP 409 and the UI instructs the user to reload; the server never silently falls back to last-write-wins. Validation is reported as the repository's Vietnamese business-rule response. `DELETE /api/business-partners/{id}` succeeds only for an unreferenced record.

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

Snapshots are captured in the same transaction as the first transition out of Draft:

| Receipt | Transition | Snapshot behavior |
|---|---|---|
| Import | Draft → Approved | Capture Supplier Code/Name |
| Import | Draft → Cancelled | Capture Supplier Code/Name |
| Export | Draft → Approved | Capture Customer Code/Name |
| Export | Draft → Dispatched | Capture Customer Code/Name |
| Export | Draft → Cancelled | Capture Customer Code/Name |

Later transitions do not overwrite the snapshot. Draft DTOs show current partner data; all non-Draft DTOs show the snapshot. Old receipts remain nullable and are not backfilled with invented partners or snapshots.

Migration `AddBusinessPartnersAndReceiptAssociations` only adds the partner table, nullable receipt columns, indexes, checks, and restrictive foreign keys. It does not update inventory, reservation, ledger, document status, or historical migrations.

## Verification record

The feature checkpoint and follow-up at revision `7a7a1359558c0bfc060c7affbe8c38131b297025` supplied these inherited results; they were not rerun for the controlled-delay UI change:

| Evidence type | Result |
|---|---|
| SQL integration | Application 324/324 PASS on owned SQLEXPRESS Run ID `a54e79228d1142fa9772a7e98bab8e0a` |
| Direct HTTP | API 150/150 PASS; real authorization/workflow checks included Viewer 403, warehouse scope, maker/checker, inactive/role validation, explicit unlink, snapshots, and unchanged rejected-request data |
| Component tests | Frontend 41/41 PASS, lint PASS, production build PASS |
| Browser UI | Loopback frontend → real API → owned SQLEXPRESS verified partner management, import Supplier workflow, export Customer workflow, reload persistence, inactive linked display, errors, snapshots, and authorization UI; HTTP assertions are classified separately above |

Controlled-delay browser QA on 2026-09-14 used the working-tree candidate based on `7a7a1359558c0bfc060c7affbe8c38131b297025`, Run ID `23e2c5bc5be5481497691691d83a006a`, and database `ERP_KHO_BrowserQA_23e2c5bc5be5481497691691d83a006a`. A loopback-only Node reverse proxy forwarded each request to the real API and delayed mutation responses by 2500 ms after receiving the real response; it did not mock success or modify production code. Observed mutation elapsed times were 2529–3576 ms.

| Browser UI case | Mutation requests | UI and final data |
|---|---:|---|
| Create import receipt | 1 POST | Button disabled with `Đang lưu...`; one `DELAY-IMP-02` receipt after reload |
| Create export receipt | 1 POST | Button disabled with `Đang lưu...`; one `DELAY-EXP-01` receipt after reload |
| Assign Supplier on Draft | 1 PUT | Selector disabled with progress status; rapid unlink blocked; Supplier assigned |
| Unlink Supplier on Draft | 1 PUT | Selector disabled with progress status; rapid reassign blocked; SupplierId null |
| Assign Customer on Draft | 1 PUT | Selector disabled with progress status; rapid unlink blocked; Customer assigned |
| Unlink Customer on Draft | 1 PUT | Selector disabled with progress status; rapid reassign blocked; CustomerId null before the final assign check |
| Delayed duplicate Partner error | 1 POST, HTTP 409 | Form left enabled and reusable, values retained, Vietnamese error shown, no false success |

Before the UI fix, the same delayed import double-click sent two POST requests while backend idempotency produced only one row. The fix adds a synchronous in-flight guard plus disabled/loading feedback for receipt creation and partner assign/unlink. Final component regression tests cover all four mutation groups; the full frontend result is 45/45 PASS with lint and production build PASS. Final SQL evidence for the controlled-delay run recorded one import, one export, the expected final partner links, one pre-existing synthetic stock-seed transaction, and zero reservations; Draft catalog mutations created no inventory, reservation, or ledger side effect.

Runtime artifacts and credentials are excluded from Git. Browser automation used synthetic data and keyboard/mouse input; it is not physical scanner-device certification.
