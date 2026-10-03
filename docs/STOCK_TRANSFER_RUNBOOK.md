# Stock Transfer Runbook

## State machine

```text
Draft -> Approved -> InTransit -> Received -> Completed
Draft -> Cancelled
Approved -> Cancelled
```

- Only `Draft` can be edited.
- `Approved` reserves no stock. Dispatch atomically decreases source stock and changes the transfer to `InTransit` in one transaction.
- Receive atomically increases destination stock by `ReceivedQuantity` only and changes the transfer to `Received` in one transaction.
- Transfers cannot be cancelled after dispatch. Reversing an in-transit transfer requires a future explicit reversal workflow; stock is never silently restored.
- Completing a `Received` transfer confirms its recorded discrepancy.

## In-transit and discrepancy

In-transit quantity is derived from documents as `DispatchedQuantity - ReceivedQuantity`; no separate balance table exists. Received, missing and damaged quantities are non-negative and their sum cannot exceed dispatched quantity. Missing and damaged stock is not added to destination availability. Excess receipt is not accepted by this version and requires a future discrepancy approval workflow.

## Authorization

- Admin can access all warehouses.
- Manager can create, approve, dispatch, receive, complete and cancel within warehouse scope.
- WarehouseStaff can create, dispatch, receive, complete and cancel within warehouse scope.
- Viewer is read-only.
- Source access is required for create, approve, dispatch and cancel. Destination access is required for receive and complete. Read queries are scoped to transfers where either warehouse is accessible; both rows and total counts are filtered in SQL.
- `StockTransfer:RequireDifferentApprover` enables separation of creator and approver without a code change.

## Migration

Apply in an approved non-production environment:

```powershell
dotnet ef database update --project ERP.Infrastructure --startup-project ERP.Api
```

Rollback only this module:

```powershell
dotnet ef database update 20260824114540_AddRefreshTokenSessions --project ERP.Infrastructure --startup-project ERP.Api
```

Reapply with the first command. Migration `20260824121209_AddStockTransfers` creates `StockTransfers`, `StockTransferDetails`, constraints and indexes and adds the filtered idempotency index for transfer inventory transactions. It does not modify existing business rows. Do not apply directly to production without the normal backup and deployment approval process.

## Deferred scope

Company/Branch transfers, damaged-goods warehouses, excess-receipt approval, transfer reversal after dispatch and a dedicated transfer Excel export are not included.
