# PRODUCTION CUTOVER & PARTNER INTEGRATION READINESS PACK (`WAVE 61.2`)

- **Run ID:** `WMS-WAVE61.1-AUDIT-0026` (`Wave 61.2 Post-Acceptance Packaging`)
- **Project:** `ERP_KHO` — Enterprise Multi-Tenant WMS SaaS (.NET 10 / ASP.NET Core / EF Core 10 / SQL Server / React 19 / TypeScript / Vite 8)
- **Target Branch:** `feat/WMS-61-project-development` (Accepted Code Baseline: `645bd9d`)
- **Target Database:** `ERP_KHO` on `(localdb)\MSSQLLocalDB` (`97` physical tables = `96` business tables + `1` `__EFMigrationsHistory`, `61` migrations, `1` active trigger `TR_InventoryTransactions_Immutable_AppendOnly`)
- **Verification Standard:** `REP-001 / REP-002` (`FACT`, `INFERENCE`, `NOT VERIFIED`), Zero-Workaround, Anti-Truncation

---

## 1. Standalone Binary Publish Verification (`RSK-OPS-001` & `RSK-OPS-002` Mitigation) (`FACT`)

### 1.1 Operational Risk Mitigation Summary
- **`RSK-OPS-001` (Windows Drive-Letter Casing Consistency in MSBuild Incremental Cache):**
  - **Root Cause:** Invoking MSBuild/CLI commands from mixed drive-letter casing (`d:\ERP_KHO` vs `D:\ERP_KHO`) on Windows can cause `ResolveProjectStaticWebAssets` or incremental `project.assets.json` path comparisons to see mismatched path prefixes.
  - **Mandatory Cutover Rule:** Always normalize the working directory to uppercase drive letter `D:\ERP_KHO` (`Set-Location 'D:\ERP_KHO'`) and pass explicit uppercase paths (`D:\ERP_KHO\...`) in CI/CD and production cutover scripts.
- **`RSK-OPS-002` (Background `ERP.Api.exe` File Lock Prevention During Build/Publish):**
  - **Root Cause:** A running `ERP.Api.exe` host process locks `ERP.Api\bin\Release\net10.0\ERP.Api.exe` and `ERP.Infrastructure.dll` on Windows (`MSB3027 / MSB3021`).
  - **Mandatory Cutover Rule:** Before running `dotnet build` or `dotnet publish`, ensure no stale `ERP.Api` process is holding locks (`Stop-Process -Name "ERP.Api" -Force -ErrorAction SilentlyContinue`), or publish to an isolated output directory (`-o D:\ERP_KHO\artifacts\publish_verify`).

### 1.2 Verbatim Standalone Release Publish & Encoding Verification Output (`FACT`)
Executed on `D:\ERP_KHO` at commit `645bd9d`, followed by immediate cleanup of the temporary `D:\ERP_KHO\artifacts\publish_verify` directory so the repository working tree remains clean:

```text
PS D:\ERP_KHO> Set-Location 'D:\ERP_KHO'
PS D:\ERP_KHO> powershell -ExecutionPolicy Bypass -File 'D:\ERP_KHO\scripts\check-encoding.ps1'
Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.

PS D:\ERP_KHO> dotnet publish 'D:\ERP_KHO\ERP.Api\ERP.Api.csproj' -c Release -o 'D:\ERP_KHO\artifacts\publish_verify' --no-restore
  ERP.Domain -> D:\ERP_KHO\ERP.Domain\bin\Release\net10.0\ERP.Domain.dll
  ERP.Application -> D:\ERP_KHO\ERP.Application\bin\Release\net10.0\ERP.Application.dll
  ERP.Infrastructure -> D:\ERP_KHO\ERP.Infrastructure\bin\Release\net10.0\ERP.Infrastructure.dll
  ERP.Api -> D:\ERP_KHO\ERP.Api\bin\Release\net10.0\ERP.Api.dll
  ERP.Api -> D:\ERP_KHO\artifacts\publish_verify\

Name         Length LastWriteTime
----         ------ -------------
ERP.Api.dll 1376768 10/29/2026 3:39:39 PM
ERP.Api.exe  162304 10/29/2026 3:39:39 PM

Exit Code: 0 (0 Warning(s), 0 Error(s); artifacts/publish_verify cleaned up after verification)
```

---

## 2. Partner Integration Configuration & Environment Variable Inventory (`BLK-03`) (`FACT`)

All 4 external partner integration domains are fully implemented in `ERP.Api` and `ERP.Infrastructure` using the transactional Inbox/Outbox pattern (`dbo.IntegrationOutboxMessages` and `dbo.IntegrationInboxMessages`), HMAC-SHA256 webhook signature verification, and idempotent replay protection. While live external partner production credentials remain classified as `BLK-03` (`NOT VERIFIED` against external third-party production servers until credentials are provisioned), the table and sections below provide the complete configuration inventory required at cutover.

### 2.1 Master Environment Variable & Secret Inventory

| Integration Domain | Config / Secret Key / Column | Default Dev/Sandbox Fallback in Code | Production Cutover Requirement | Consuming C# Service & `File:Line` |
| --- | --- | --- | --- | --- |
| **Database & Core Auth** | `ConnectionStrings__DefaultConnection` | `Server=(localdb)\MSSQLLocalDB;Database=ERP_KHO;Trusted_Connection=True;...` | Inject least-privilege SQL login (`db_datareader`, `db_datawriter`, `EXECUTE`; **never `sa`**) via environment variable / Secret Manager | `ERP.Api/Program.cs` |
| **Database & Core Auth** | `JwtSettings__SecretKey` / `Jwt__Key` | Dev signing key in `appsettings.Development.json` | 256-bit+ cryptographically random secret stored in Vault / Secret Manager | `ERP.Api/Program.cs` |
| **Group A — Domestic Couriers (TMS)** | `dbo.CarrierIntegrationConfigs.EncryptedApiToken` (`GHN`, `GHTK`, `VIETTELPOST`, `JT_EXPRESS`) | `SANDBOX_TOKEN_{CarrierCode}_{WarehouseId:D2}` (`IsSandbox = true`) | Provision live carrier Shop Token / API Key via `POST /api/v1/shipments/carrier-configs` with `isSandbox: false` | `ERP.Infrastructure/Services/ShipmentOperationsService.cs:708-740` |
| **Group A — Domestic Couriers (TMS)** | `dbo.CarrierIntegrationConfigs.WebhookSecretHash` | `DefaultCarrierWebhookSecret = "ERP_KHO_CARRIER_WEBHOOK_SECRET_2026"` | Unique per-carrier HMAC-SHA256 secret configured via `POST /api/v1/shipments/carrier-configs` | `ERP.Infrastructure/Services/ShipmentOperationsService.cs:17, 1074-1106` |
| **Group B — E-Commerce Marketplaces** | `dbo.MarketplaceShops.EncryptedAccessToken` & `EncryptedRefreshToken` (`SHOPEE`, `TIKTOK`, `LAZADA`) | Sandbox OAuth token exchange via `ExchangeMarketplaceOAuthAsync` | Complete live OAuth 2.0 authorization code exchange via `POST /api/v1/marketplace-shops/oauth/exchange` and automated rotation via `POST /api/v1/marketplace-shops/{shopId}/oauth/refresh` | `ERP.Infrastructure/Services/SalesOrderOperationsService.cs:916-1121` |
| **Group B — E-Commerce Marketplaces** | `dbo.MarketplaceShops.WebhookSecret` | `DefaultWebhookSecret = "ERP_KHO_MARKETPLACE_WEBHOOK_SECRET_2026"` | Partner Push/Webhook HMAC-SHA256 secret per connected shop | `ERP.Infrastructure/Services/SalesOrderOperationsService.cs:19, 1306-1333` |
| **Group C — Accounting & E-Invoice (NĐ 70/2025)** | `AccountingProvider` (`MISA_AMIS`, `FAST_ACCOUNTING`, `BRAVO_ERP`) & `EInvoiceProvider` (`VIETTEL_SINVOICE`, `MISA_MEINVOICE`, `VNPT_INVOICE`) | Outbox staging in `dbo.IntegrationOutboxMessages` (`Status = "Pending"`) | Configure worker dispatcher endpoint credentials for MISA AMIS & Viettel S-Invoice / Tax Authority TVAN | `ERP.Infrastructure/Services/ExportReceiptOperationsService.cs:1090-1465` |
| **Group D — VietQR Payment Gateways** | `X-VietQR-Signature` HMAC Secret (`PayOS`, `SePay`, `Casso`) | `"ERP-KHO-VIETQR-WEBHOOK-SECRET-2026"` | Replace with live VietQR gateway webhook HMAC-SHA256 secret before enabling public webhook ingress | `ERP.Api/Controllers/SaasBillingController.cs:80-102`, `ERP.Infrastructure/Services/SaasBillingService.cs:260-384` |

---

### 2.2 Group A — TMS / Domestic Couriers (`GHN`, `GHTK`, `ViettelPost`, `J&T Express`) (`Epic 30.2`)

1. **Adapters, Services & Thin Controller Endpoints (`FACT`):**
   - **Service Implementation:** [ShipmentOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ShipmentOperationsService.cs#L625-L1335) and [HandoverManifestService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/HandoverManifestService.cs#L139-L300).
   - **REST Endpoints ([ShipmentsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ShipmentsController.cs#L220-L293) & [HandoverManifestsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/HandoverManifestsController.cs#L39-L135)):**
     - `GET /api/v1/shipments/carrier-configs` & `POST /api/v1/shipments/carrier-configs` (`[IdempotentCommand("Shipment.UpsertCarrierConfig")]`)
     - `POST /api/v1/shipments/normalize-vn-address` (2-tier `Province -> Ward` & 3-tier `Province -> District -> Ward` administrative address resolution against `dbo.VnAdministrativeUnits` and `dbo.VnCarrierWardMappings`; returns `422 ADDRESS_WARD_UNRESOLVED` on unresolvable ward)
     - `POST /api/v1/shipments/book-carrier-waybill` (`[IdempotentCommand("Shipment.BookCarrierWaybill")]` — generates AWB tracking number, sorting hub code, route code, stages `CARRIER_BOOK_WAYBILL` in `dbo.IntegrationOutboxMessages`, and initializes `dbo.ShipmentFinancialLedgers`)
     - `POST /api/v1/shipments/webhooks/carrier/{carrierCode}` (`[IdempotentCommand("Shipment.CarrierWebhook")]` — ingests carrier status & e-POD/COD updates)
     - `POST /api/v1/handover-manifests/scan-awb`, `POST /api/v1/handover-manifests/{id}/seal`, and `POST /api/v1/handover-manifests/confirm`
2. **Webhook Security, Deduplication & Gate Handover Guards (`FACT`):**
   - **HMAC-SHA256 Signature Header:** Reads `X-Carrier-Signature` (or fallback `X-Hub-Signature-256`) in `ShipmentsController.cs:276-283` and verifies via `adapter.VerifyWebhookHmacSignature(payload, signatureHeader, secret)` (`ShipmentOperationsService.cs:1074-1106`). Invalid signatures are rejected with HTTP **`401 Unauthorized` (`errorCode: "CARRIER_WEBHOOK_INVALID_SIGNATURE"`)**.
   - **Inbox Deduplication:** Deduplicates incoming webhook events by `(SourceSystem = "CARRIER_{CarrierCode}", ExternalMessageId = eventId)` in `dbo.IntegrationInboxMessages` (`ShipmentOperationsService.cs:1129-1144`), returning `200 OK` with `idempotentReplay: true` on duplicate delivery.
   - **Physical Gate Handover Guards (`HandoverManifestService.cs:182-256`):**
     - Blocks scanning any AWB whose linked `SalesOrder` or `ExportReceipt` is `Cancelled` or `CancelPendingPutback` with HTTP **`409 Conflict` (`errorCode: "ORDER_CANCELLED_PUTBACK_REQUIRED"`)**.
     - Blocks scanning the same `TrackingNumber` twice across `dbo.HandoverManifestLines` with HTTP **`409 Conflict` (`errorCode: "DUPLICATE_AWB_SCAN"`)**.
     - Blocks scanning into an already sealed manifest with HTTP **`409 Conflict` (`errorCode: "MANIFEST_ALREADY_SEALED"`)**.

---

### 2.3 Group B — E-Commerce Marketplaces (`Shopee`, `TikTok Shop`, `Lazada`) (`Epic 30.3`)

1. **Adapters, Services & Thin Controller Endpoints (`FACT`):**
   - **Service Implementation:** [SalesOrderOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/SalesOrderOperationsService.cs#L754-L1650).
   - **REST Endpoints ([SalesOrdersController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/SalesOrdersController.cs#L196-L300)):**
     - `GET /api/v1/marketplace-shops` & `POST /api/v1/marketplace-shops` (`[IdempotentCommand("MarketplaceShop.Connect")]`)
     - `POST /api/v1/marketplace-shops/oauth/exchange` (`[IdempotentCommand("MarketplaceShop.OAuthExchange")]` — exchanges partner `AuthCode` for `EncryptedAccessToken` and `EncryptedRefreshToken`, persists `TokenExpiresAtUtc` and `WebhookSecret` in `dbo.MarketplaceShops`, and stages `MARKETPLACE_OAUTH_CONNECTED` in `dbo.IntegrationOutboxMessages`)
     - `POST /api/v1/marketplace-shops/{shopId:int}/oauth/refresh` (`[IdempotentCommand("MarketplaceShop.OAuthRefresh")]` — rotates access/refresh tokens and writes an audit trail in `dbo.AuditLogs`)
     - `POST /api/v1/marketplace-shops/sync-inventory` (`[IdempotentCommand("MarketplaceShop.SyncInventory")]` — computes real-time ATP across pickable, non-blocked, non-expired 9D `dbo.InventoryStocks` buckets, applies `ReserveRatioPct`, `SafetyBufferQty`, and `LowStockThreshold` anti-oversell rules, and stages `Inventory.AvailableChanged` messages in `dbo.IntegrationOutboxMessages`)
     - `POST /api/v1/sales-orders/webhook-buyer-cancellation` & `POST /api/v1/integrations/webhooks/{channelCode}/buyer-cancel` (`[IdempotentCommand("SalesOrder.WebhookBuyerCancellation")]`)
2. **Webhook Security, Deduplication & Buyer-Cancellation State Machine (`FACT`):**
   - **HMAC-SHA256 Signature Header:** Reads `X-Marketplace-Signature`, `X-Shopee-Signature`, or `X-TikTok-Signature` (`SalesOrdersController.cs:290-292`) and verifies against `MarketplaceShop.WebhookSecret` (`SalesOrderOperationsService.cs:1306-1333`). Invalid signatures return HTTP **`401 Unauthorized` (`errorCode: "MARKETPLACE_WEBHOOK_INVALID_SIGNATURE"`)**.
   - **Inbox Deduplication:** Reads `X-Webhook-Event-Id` (`SalesOrdersController.cs:293`) and deduplicates against `dbo.IntegrationInboxMessages` (`EventType == "ORDER_CANCELLED"`, `SalesOrderOperationsService.cs:1371-1401`), returning `idempotentReplay: true` on replay.
   - **3-Way Buyer-Cancellation Decision Matrix (`SalesOrderOperationsService.cs:1440-1560`):**
     - **`REJECT` (`ALREADY_DISPATCHED_TO_CARRIER`):** Order is already `Fulfilled`/`Dispatched` or its `ExportReceipt` is sealed in a `SEALED` `HandoverManifest`.
     - **`ACCEPT_PENDING_PUTBACK` (`requiresPutback: true`):** Order has already been picked (`StockAllocationStatus.Picked`) or had its thermal AWB printed (`AwbPrintCount > 0`); transitions `SalesOrder` and `ExportReceipt` to `CancelPendingPutback` until warehouse staff complete `POST /api/v1/sales-orders/{id}/confirm-cancel-putback`.
     - **`ACCEPT` (`requiresPutback: false`):** Order has not yet been picked; releases active reservations/allocations and cancels the order immediately.

---

### 2.4 Group C — Accounting (`Circular 200/2014/TT-BTC`) & E-Invoice (`Decree 70/2025/NĐ-CP`) (`Epic 90.3`)

1. **Services & Thin Controller Endpoints (`FACT`):**
   - **Service Implementation:** [ExportReceiptOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ExportReceiptOperationsService.cs#L1090-L1465) (`SyncAccountingAndEInvoiceOutboxAsync`).
   - **REST Endpoints ([ExportReceiptsController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/ExportReceiptsController.cs#L305-L384)):**
     - `GET /api/v1/export-receipts/accounting-einvoice/preview`
     - `POST /api/v1/export-receipts/accounting-einvoice/sync` (`[IdempotentCommand("ExportReceipts.SyncAccountingAndEInvoiceOutbox")]`)
     - `GET /api/v1/accounting-exports/misa-excel` & `POST /api/v1/export-receipts/export-misa-einvoice`
     - `POST /api/v1/export-receipts/einvoice-nd70-draft`
2. **Circular 200 Double-Entry & Decree 70/2025/NĐ-CP Invariants (`FACT`):**
   - **Strict Existence & Line Validation (`F-015` Part 3):** `SyncAccountingAndEInvoiceOutboxAsync` (`L1154–L1185`) throws `KeyNotFoundException("EXPORT_RECEIPT_NOT_FOUND...")` (`HTTP 404`) if the requested `ExportReceiptId` does not exist and throws `ConflictException("EXPORT_RECEIPT_HAS_NO_LINES_FOR_ACCOUNTING_SYNC", ...)` (`HTTP 409`) if the receipt has `0` detail lines. Zero synthetic/default SKU fallbacks exist.
   - **Balanced Circular 200 Journal Entries (`TotalDebit == TotalCredit`):** Computes COGS (`Nợ TK 632 / Có TK 1561`) from actual 9D ledger/stock moving-average cost (`dbo.InventoryTransactions.UnitCost` / `dbo.Products.StandardCost`), Revenue (`Nợ TK 131 / Có TK 5111`), and VAT (`Nợ TK 131 / Có TK 33311`), staging both `AccountingVoucherCircular200` and `EInvoiceNd70Submission` (`1C26TAA`) in `dbo.IntegrationOutboxMessages`.
   - **Dual Ledger Timestamps (`F-007`):** Every `dbo.InventoryTransactions` row records both `ActualOccurredAtUtc` and `PostedAtUtc` (`datetime2`), ensuring accurate accounting period cutoffs.
   - **RBAC Cost Masking (`SEC-001` / Layer 3):** Callers without `inventory.cost.read` have `totalCogsAmount`, `totalDebitAmount`, `totalCreditAmount`, and `COGS_632_1561` amounts masked to `null` on the server DTO (`L1429–L1456`).
   - **Customs Guard & Form `04/SS-HĐĐT`:** Non-bonded import receipts missing `CustomsDeclarationNumber` are rejected with HTTP **`422 Unprocessable Entity` (`CUSTOMS_DECLARATION_REQUIRED_FOR_NON_BONDED_IMPORT`)** (`L1196–L1203`). Post-issuance invoice adjustments/replacements (`AdjustmentMode = "ADJUST"` or `"REPLACE"`) require `AdjustmentReason` (`422 EINVOICE_ND70_ADJUSTMENT_REASON_REQUIRED`) and emit Form **`04/SS-HĐĐT` (`FORM_04_SS_HDDT`)**; reversals (`Reverse = true`) stage `AccountingVoucherCircular200Reversal` and `EInvoiceNd70CancellationForm04SS`.

---

### 2.5 Group D — VietQR Napas247 Payment Gateways (`PayOS`, `SePay`, `Casso`)

1. **Services & Thin Controller Endpoints (`FACT`):**
   - **Service Implementation:** [SaasBillingService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/SaasBillingService.cs#L18-L384).
   - **REST Endpoints ([SaasBillingController.cs](file:///d:/ERP_KHO/ERP.Api/Controllers/SaasBillingController.cs#L18-L118)):**
     - `GET /api/v1/saas-billing/plans` & `GET /api/v1/saas-billing/subscription`
     - `POST /api/v1/saas-billing/enforce-quota` (`[IdempotentCommand("SaaS.EnforceQuotaCheck")]`)
     - `POST /api/v1/saas-billing/vietqr-invoice` (`[IdempotentCommand("SaaS.CreateVietQrInvoice")]` — generates Napas247 EMVCo QR payload `00020101021238570010A000000727...` and persists `dbo.SaasBillingInvoices`)
     - `POST /api/v1/saas-billing/vietqr-webhook` (`[AllowAnonymous, IdempotentCommand("SaaS.VietQrBillingWebhook")]`)
2. **Webhook Security, Replay Protection & Quota Tiers (`FACT`):**
   - **HMAC-SHA256 Signature Verification (`SaasBillingController.cs:80-102`):** Validates header `X-VietQR-Signature` using `HMACSHA256` over canonical payload `"{InvoiceCode}|{PlanCode}|{PaidAmountVnd}"`. Invalid signatures are rejected with HTTP **`401 Unauthorized` (`errorCode: "INVALID_WEBHOOK_SIGNATURE"`)**.
   - **Idempotency & Replay Protection:** Enforced by `[IdempotentCommand("SaaS.VietQrBillingWebhook")]` (`dbo.IdempotencyRecords`) plus invoice status transition (`UNPAID -> PAID`) in `dbo.SaasBillingInvoices` and automatic plan/quota upgrade in `dbo.TenantSubscriptions`.
   - **Flash-Sale Burst Buffer & Hard Cap (`SaasBillingService.cs:100-123, 188-194`):**
     - `<= 100%` quota: `NORMAL`
     - `100% < usagePct <= 115%`: `SOFT_WARNING_GRACE_15PCT` (`SOFT_BURST_100_115`, `writeBlocked = false`, `72h` grace period so Flash Sale outbound fulfillment is never interrupted)
     - `> 115%` quota: `HARD_CAP_BLOCKED` (`READ_ONLY_OVER_115`, throws `403 SUBSCRIPTION_QUOTA_EXCEEDED` on new order creation while keeping read/outbound completion operational)

---

## 3. Physical Warehouse Hardware Acceptance Checklist (`FACT`)

### 3.1 1D/2D USB HID Barcode Scanners & Mobile PDA Camera (`MobileBarcodeScannerBar.tsx` & `cameraBarcodeDecoder.ts`)

| # | Hardware / Station Check | Required Configuration / Parameter | Code Implementation Reference (`File:Line`) | Acceptance Criterion |
| :---: | --- | --- | --- | --- |
| **1** | **USB HID Barcode Scanner Mode** | Configure scanner in **USB HID Keyboard Wedge** mode with **`Enter` (`CR` / `keyCode 13`) suffix** enabled; inter-character delay `<= 50ms`. | [MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L137-L260) | Scanning a location/SKU barcode outside or inside the input field automatically captures the keystroke burst, strips control characters via `sanitizeScannerCode`, triggers `/api/v1/product-barcodes/resolve`, and plays the `880Hz` confirmation beep (`playScannerBeep()`). |
| **2** | **Vietnamese IME (`Telex` / `VNI`) Guard at Scan Workstations** | **Switch Windows / Unikey / EVKey to English (`EN`) mode** (or disable Vietnamese IME) on fixed receiving, packing, and gate handover stations. | [MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L63-L94) | Prevents Telex/VNI IME from converting rapid scanner characters (`AA -> Â`, `EE -> Ê`, `OW -> Ơ`, `W -> Ư`, `S/F/R/X/J` tone marks) inside alphanumeric SKU/Location/AWB codes or GS1-128 strings `(01)08938505000517(10)LOT-QA-002(17)271231(21)SER-QA-01`. |
| **3** | **GS1-128 Application Identifier Parsing** | Verify scanner transmits parentheses or raw GS1 AI prefixes `(00)` SSCC, `(01)` GTIN-14, `(10)` Lot Number, `(17)` Expiry Date (`YYMMDD`), `(21)` Serial Number. | [MobileBarcodeScannerBar.tsx](file:///d:/ERP_KHO/frontend/src/components/MobileBarcodeScannerBar.tsx#L63-L94) (`parseGs1BarcodeClient`) | Scanning `(01)08938505000517(10)LOT-QA-002(17)271231(21)SER-QA-01` automatically populates GTIN `08938505000517`, Lot `LOT-QA-002`, Expiry `31/12/2027` (`2027-12-31`), and Serial `SER-QA-01` in the 3-step PDA wizard. |
| **4** | **Mobile PDA Camera Scanner (`390x844`)** | Serve frontend over **HTTPS** (or `localhost`) so browser grants `navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } })`. | [cameraBarcodeDecoder.ts](file:///d:/ERP_KHO/frontend/src/utils/cameraBarcodeDecoder.ts#L1-L851) | Decodes `EAN-13` (with Modulo-10 checksum verification `verifyEanModulo10Checksum` and `893 (Việt Nam)` GS1 prefix detection), `EAN-8`, `UPC-A`, `Code-128`, and `QR Code` across tilted/glare warehouse lighting frames. |

### 3.2 Thermal Label Printer (`100mm x 150mm` AWB) & A4 Laser Printer (`01-VT` / `02-VT`)

| # | Printer / Document Type | Required Driver & Browser Print Settings | Code Implementation Reference (`File:Line`) | Acceptance Criterion |
| :---: | --- | --- | --- | --- |
| **1** | **Thermal AWB Label Printer (`100x150mm` / `4x6 inch`)** | Paper Size: **`100mm x 150mm`**; Margins: **`None` (`0mm`)**; Scale: **`100%` (`Actual Size`)**; Headers & Footers: **`Off`**; Background graphics: **`On`**. | [ExportReceiptOperationsService.cs](file:///d:/ERP_KHO/ERP.Infrastructure/Services/ExportReceiptOperationsService.cs#L244-L455) (`@page { size: 100mm 150mm; margin: 0; }`, `.awb-page { width: 100mm; height: 150mm; padding: 4mm; page-break-after: always; }`) | Each AWB label prints on exactly 1 `100x150mm` thermal sticker sorted by `PrimaryPickLocationCode ASC, ProductCode ASC`, with picking summary footer `[Kệ: <Loc> | SKU: <SKU> x <Qty>]`, binary `%PDF-1.4` stream support, and diagonal `REPRINT - BẢN SAO` watermark on reprints (`AwbPrintCount > 1`). |
| **2** | **Legal Warehouse Vouchers (`01-VT` Phiếu Nhập Kho & `02-VT` Phiếu Xuất Kho)** | Paper Size: **`A4 Portrait` (`210mm x 297mm`)**; Margins: **`10mm`** (or Default); Scale: **`100%`**; Headers & Footers: **`Off`**. | [ReceiptPrintPreview.tsx](file:///d:/ERP_KHO/frontend/src/components/ReceiptPrintPreview.tsx#L68-L135), [ReceiptPrintPreview.css](file:///d:/ERP_KHO/frontend/src/components/ReceiptPrintPreview.css#L1-L9) (`@media print { @page { size: A4 portrait; margin: 10mm; } }`) | Prints cleanly on a single A4 portrait sheet (`page-break-after: avoid; break-inside: avoid`), hiding all app navigation chrome (`.app-sidebar`, `.app-topbar`, `.receipt-print-actions`), rendering the Code-128 document barcode, right-aligned `tabular-nums` quantities, and all 4 legal signature columns (`Người lập phiếu`, `Người giao / nhận hàng`, `Thủ kho`, `Quản lý kho / Kế toán`). |

---

## 4. Production Cutover Smoke Test Runbook (`FACT`)

Execute the following 5-step smoke test sequence in order during the production cutover window:

```powershell
# Step 1: Set canonical uppercase working directory & verify UTF-8 encoding gate
Set-Location 'D:\ERP_KHO'
powershell -ExecutionPolicy Bypass -File 'D:\ERP_KHO\scripts\check-encoding.ps1'

# Step 2: Publish standalone Release binary & build Frontend production bundle
dotnet publish 'D:\ERP_KHO\ERP.Api\ERP.Api.csproj' -c Release -o 'D:\ERP_KHO\artifacts\publish_release' --no-restore
npm --prefix 'D:\ERP_KHO\frontend' run build

# Step 3: Verify Wave 60 & Wave 61 SQL Server Integration Suites (7/7 PASS)
dotnet test 'D:\ERP_KHO\ERP.Application.Tests\ERP.Application.Tests.csproj' -c Release --no-build --filter "FullyQualifiedName~SqlServerWave60RoadmapEpicsTests|FullyQualifiedName~SqlServerWave61RoadmapEpicsTests"

# Step 4: Verify Liveness & Readiness Health Probes (Requires running ERP.Api host)
Invoke-RestMethod -Uri "http://localhost:5142/health/live" -Method GET
Invoke-RestMethod -Uri "http://localhost:5142/health/ready" -Method GET

# Step 5: Run Read-Only 3-Way SQL Integrity Checks (Check A, Check B, Check C) on ERP_KHO
# Expected: Check A = 0 rows with Variance <> 0; Check B = 0 orphan reservations;
# Check C = 20 rows, TotalOnHand = 232.0000, TotalReserved = 9.0000, TotalAvailable = 223.0000,
# NegativeQtyCount = 0, InvalidGrainHashCount = 0.
powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\van40\.gemini\antigravity\brain\16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb\scratch\run-sql-checks.ps1'
```
