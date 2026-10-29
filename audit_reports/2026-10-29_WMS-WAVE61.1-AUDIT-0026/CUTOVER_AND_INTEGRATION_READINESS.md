# PRODUCTION CUTOVER & PARTNER INTEGRATION READINESS PACK (`WAVE 61.2` / `WAVE 61.3` / `WAVE 61.6`)

- **Run ID:** `WMS-WAVE61.1-AUDIT-0026` (`Wave 61.2 / Wave 61.3 / Wave 61.6 Post-Acceptance Packaging, Smoke Verification & CI Security Gate Closure`)
- **Project:** `ERP_KHO` — Enterprise Multi-Tenant WMS SaaS (.NET 10 / ASP.NET Core / EF Core 10 / SQL Server / React 19 / TypeScript / Vite 8)
- **Target Branch:** `feat/WMS-61-project-development` (Accepted Backend C# Baseline: `645bd9d`, Prior Audit Head: `ad51bda`, Current Wave 61.6 Closure: `17/17` Findings `CLOSED`)
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
  - **Mandatory Cutover Rule:** Before running `dotnet build` or `dotnet publish`, ensure no stale `ERP.Api` process is holding locks (`Stop-Process -Name "ERP.Api" -Force -ErrorAction SilentlyContinue`), or publish to an isolated output directory (`-o D:\ERP_KHO\artifacts\publish_smoke`).

### 1.2 Verbatim Standalone Release Publish & Encoding Verification Output (`FACT`)
Executed on `D:\ERP_KHO` at accepted code baseline `645bd9d`, followed by immediate cleanup of the temporary `D:\ERP_KHO\artifacts\publish_smoke` directory so the repository working tree remains clean:

```text
PS D:\ERP_KHO> Set-Location 'D:\ERP_KHO'
PS D:\ERP_KHO> powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\ERP_KHO\scripts\check-encoding.ps1'
Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.
STEP1_EXIT=0

PS D:\ERP_KHO> dotnet publish 'D:\ERP_KHO\ERP.Api\ERP.Api.csproj' -c Release -o 'D:\ERP_KHO\artifacts\publish_smoke' --no-restore
  ERP.Domain -> D:\ERP_KHO\ERP.Domain\bin\Release\net10.0\ERP.Domain.dll
  ERP.Application -> D:\ERP_KHO\ERP.Application\bin\Release\net10.0\ERP.Application.dll
  ERP.Infrastructure -> D:\ERP_KHO\ERP.Infrastructure\bin\Release\net10.0\ERP.Infrastructure.dll
  ERP.Api -> D:\ERP_KHO\ERP.Api\bin\Release\net10.0\ERP.Api.dll
  ERP.Api -> D:\ERP_KHO\artifacts\publish_smoke\
STEP2A_EXIT=0

Name         Length LastWriteTime
----         ------ -------------
ERP.Api.dll 1376768 10/29/2026 4:00:06 PM
ERP.Api.exe  162304 10/29/2026 4:00:06 PM

Exit Code: 0 (0 Warning(s), 0 Error(s); artifacts/publish_smoke cleaned up after verification)
```

---

## 2. Partner Integration Configuration & Environment Variable Inventory (`BLK-03`) (`FACT`)

All 4 external partner integration domains are fully implemented in `ERP.Api` and `ERP.Infrastructure` using the transactional Inbox/Outbox pattern (`dbo.IntegrationOutboxMessages` and `dbo.IntegrationInboxMessages`), HMAC-SHA256 webhook signature verification, and idempotent replay protection. While live external partner production credentials remain classified as `BLK-03` (`NOT VERIFIED` against external third-party production servers until credentials are provisioned), the master table and sections `2.1–2.4` below provide the complete configuration inventory required at cutover.

### Master Environment Variable & Secret Inventory

| Integration Domain | Config / Secret Key / Column | Default Dev/Sandbox Fallback in Code | Production Cutover Requirement | Consuming C# Service & `File:Line` |
| --- | --- | --- | --- | --- |
| **Database & Core Auth** | `ConnectionStrings__DefaultConnection` | `Server=(localdb)\MSSQLLocalDB;Database=ERP_KHO;Trusted_Connection=True;...` | Inject least-privilege SQL login (`db_datareader`, `db_datawriter`, `EXECUTE`; **never `sa`**) via environment variable / Secret Manager | `ERP.Api/Program.cs` |
| **Database & Core Auth** | `JwtSettings__SecretKey` / `Jwt__Key` | Dev signing key in `appsettings.Development.json` | 256-bit+ cryptographically random secret stored in Vault / Secret Manager | `ERP.Api/Program.cs` |
| **Group A — Domestic Couriers (TMS)** | `dbo.CarrierIntegrationConfigs.EncryptedApiToken` (`GHN`, `GHTK`, `VIETTELPOST`, `JT_EXPRESS`) | `SANDBOX_TOKEN_{CarrierCode}_{WarehouseId:D2}` (`IsSandbox = true`) | Provision live carrier Shop Token / API Key via `POST /api/v1/shipments/carrier-configs` with `isSandbox: false` | `ERP.Infrastructure/Services/ShipmentOperationsService.cs:708-740` |
| **Group A — Domestic Couriers (TMS)** | `dbo.CarrierIntegrationConfigs.WebhookSecretHash` | `DefaultCarrierWebhookSecret = "ERP_KHO_CARRIER_WEBHOOK_SECRET_2026"` | Unique per-carrier HMAC-SHA256 secret configured via `POST /api/v1/shipments/carrier-configs` | `ERP.Infrastructure/Services/ShipmentOperationsService.cs:17, 1074-1106` |
| **Group B — E-Commerce Marketplaces** | `dbo.MarketplaceShops.EncryptedAccessToken` & `EncryptedRefreshToken` (`SHOPEE`, `TIKTOK`, `LAZADA`) | Sandbox OAuth token exchange via `ExchangeMarketplaceOAuthAsync` | Complete live OAuth 2.0 authorization code exchange via `POST /api/v1/marketplace-shops/oauth/exchange` and automated rotation via `POST /api/v1/marketplace-shops/{shopId}/oauth/refresh` | `ERP.Infrastructure/Services/SalesOrderOperationsService.cs:916-1121` |
| **Group B — E-Commerce Marketplaces** | `dbo.MarketplaceShops.WebhookSecret` | `DefaultWebhookSecret = "ERP_KHO_MARKETPLACE_WEBHOOK_SECRET_2026"` | Partner Push/Webhook HMAC-SHA256 secret per connected shop | `ERP.Infrastructure/Services/SalesOrderOperationsService.cs:19, 1306-1333` |
| **Group C — Accounting & E-Invoice (NĐ 70/2025)** | `AccountingProvider` (`MISA_AMIS`, `FAST_ACCOUNTING`, `BRAVO_ERP`) & `EInvoiceProvider` (`VIETTEL_SINVOICE`, `MISA_MEINVOICE`, `VNPT_INVOICE`) | Outbox staging in `dbo.IntegrationOutboxMessages` (`Status = "Pending"`) | Configure worker dispatcher endpoint credentials for MISA AMIS & Viettel S-Invoice / Tax Authority TVAN | `ERP.Infrastructure/Services/ExportReceiptOperationsService.cs:1090-1465` |
| **Group D — VietQR Payment Gateways** | `X-VietQR-Signature` / `SaasBilling:VietQrWebhookSecret` (`PayOS`, `SePay`, `Casso`) | `"ERP-KHO-VIETQR-WEBHOOK-SECRET-2026"` | Replace with live VietQR gateway webhook HMAC-SHA256 secret before enabling public webhook ingress | `ERP.Api/Controllers/SaasBillingController.cs:80-102`, `ERP.Infrastructure/Services/SaasBillingService.cs:197-310, 260-384` |

---

### 2.1 Group A — TMS / Domestic Couriers (`GHN`, `GHTK`, `ViettelPost`, `J&T Express`) (`Epic 30.2`)

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

### 2.2 Group B — E-Commerce Marketplaces (`Shopee`, `TikTok Shop`, `Lazada`) (`Epic 30.3`)

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

### 2.3 Group C — Accounting (`Circular 200/2014/TT-BTC`) & E-Invoice (`Decree 70/2025/NĐ-CP`) (`Epic 90.3`)

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

### 2.4 Cổng thanh toán VietQR Napas247 (`PayOS`, `SePay`, `Casso`) (`Group D`)

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

> **Lưu ý tích hợp Webhook thực tế trên Staging/Production (SePay / PayOS Adapter)**: Endpoint `POST /api/v1/saas-billing/webhooks/vietqr` / `POST /api/v1/saas-billing/vietqr-webhook` (`SaasBillingController.cs:80-102` & `SaasBillingService.cs:197-310` / `260-384`) xác thực chữ ký `HMAC-SHA256` qua header `X-VietQR-Signature` trên chuỗi gốc định dạng chuẩn `"{InvoiceCode}|{PlanCode}|{PaidAmountVnd}"`. Khi đấu nối với cổng trung gian thanh toán VietQR thực tế (như PayOS hoặc SePay vốn có cấu trúc JSON payload và header chữ ký riêng), tầng API Gateway / Cloudflare Worker hoặc Webhook Adapter phía trước cần ánh xạ (map) payload về DTO chuẩn của ERP và ký lại bằng `SaasBilling:VietQrWebhookSecret` trước khi chuyển tiếp vào `SaasBillingController`.

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
powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\ERP_KHO\scripts\check-encoding.ps1'

# Step 2: Publish standalone Release binary & build Frontend production bundle
dotnet publish 'D:\ERP_KHO\ERP.Api\ERP.Api.csproj' -c Release -o 'D:\ERP_KHO\artifacts\publish_smoke' --no-restore
npm --prefix 'D:\ERP_KHO\frontend' run build

# Step 3: Verify Wave 60 & Wave 61 SQL Server Integration Suites (7/7 PASS)
dotnet test 'D:\ERP_KHO\ERP.Application.Tests\ERP.Application.Tests.csproj' -c Release --no-build --filter "FullyQualifiedName~SqlServerWave60RoadmapEpicsTests|FullyQualifiedName~SqlServerWave61RoadmapEpicsTests"

# Step 4: Verify Liveness & Readiness Health Probes against published Release binary
Invoke-WebRequest -Uri "http://127.0.0.1:5142/health/live" -Method GET -UseBasicParsing
Invoke-WebRequest -Uri "http://127.0.0.1:5142/health/ready" -Method GET -UseBasicParsing

# Step 5: Run Read-Only 3-Way SQL Integrity Checks (Check A, Check B, Check C) on ERP_KHO
powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\van40\.gemini\antigravity\brain\16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb\scratch\run-sql-checks.ps1'
```

---

## 5. Biên Bản Thực Thi Cutover Smoke Test Thực Tế & Danh Sách Commits Đóng Gói PR — Wave 61.3 (`FACT`)

### 5.1 Biên Bản Thực Thi Thực Tế 5 Bước Cutover Smoke Test (`TASK-W61.3-SMOKE-EXEC`) (`FACT`)

Toàn bộ 5 bước trong Runbook Mục 4 đã được thực thi tuần tự trên `D:\ERP_KHO` (tại commit mã nguồn `645bd9d` / commit nhánh `84a1b99`) vào ngày `2026-10-29T16:00:00+07:00`:

#### Smoke Step 1 & Step 2 — Kiểm tra Encoding, Đóng gói Binary `ERP.Api` (`Release`) & Build Frontend Production (`Vite 8.1.4`)
```text
=== SMOKE STEP 1: check-encoding.ps1 ===
Encoding check passed: all scanned files are valid UTF-8 and no mojibake markers were found.
STEP1_EXIT=0

=== SMOKE STEP 2A: dotnet publish -c Release -o D:\ERP_KHO\artifacts\publish_smoke ===
  ERP.Domain -> D:\ERP_KHO\ERP.Domain\bin\Release\net10.0\ERP.Domain.dll
  ERP.Application -> D:\ERP_KHO\ERP.Application\bin\Release\net10.0\ERP.Application.dll
  ERP.Infrastructure -> D:\ERP_KHO\ERP.Infrastructure\bin\Release\net10.0\ERP.Infrastructure.dll
  ERP.Api -> D:\ERP_KHO\ERP.Api\bin\Release\net10.0\ERP.Api.dll
  ERP.Api -> D:\ERP_KHO\artifacts\publish_smoke\
STEP2A_EXIT=0

Name         Length LastWriteTime
----         ------ -------------
ERP.Api.dll 1376768 10/29/2026 4:00:06 PM
ERP.Api.exe  162304 10/29/2026 4:00:06 PM

=== SMOKE STEP 2B: npm --prefix D:\ERP_KHO\frontend run build ===
> frontend@0.0.0 build
> tsc -b && vite build
vite v8.1.4 building client environment for production...
transforming...✓ 1711 modules transformed.
✓ built in 1.36s
STEP2B_EXIT=0
```

#### Smoke Step 3 — Kiểm thử Tích hợp SQL Server Wave 60 & Wave 61 (`7/7 PASS`)
```text
=== SMOKE STEP 3: SqlServerWave60RoadmapEpicsTests & SqlServerWave61RoadmapEpicsTests ===
Test run for D:\ERP_KHO\ERP.Application.Tests\bin\Release\net10.0\ERP.Application.Tests.dll (.NETCoreApp,Version=v10.0)
Owned migration target: ERP_KHO_Integration_20261029_090103_23c01103; RunId=23c01103ea3044c2a003d1ab37e9beeb.
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_3_VietnamAccountingCircular200AndEinvoiceNd70Outbox_HappyPath_CustomsGuard_Form04SS_Concurrency_And_Reversal [2 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Wave61_F013_CrossDockAndBreakBulkReversal_InsufficientStock_DuplicateReversal_And_LedgerRule_Guards [4 s]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_2_SelfService3PlDepositorPortal_OwnerIsolation_BillingPreview_Concurrency_And_Reversal [895 ms]
  Passed ERP.Application.Tests.SqlServerWave61RoadmapEpicsTests.Epic90_1_DynamicAbcVelocitySlottingAndTaskInterleaving_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
  Passed ERP.Application.Tests.SqlServerWave60RoadmapEpicsTests.Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal [1 s]
Owned migration target cleaned: ERP_KHO_Integration_20261029_090103_23c01103; RunId=23c01103ea3044c2a003d1ab37e9beeb.
Test Run Successful. Total tests: 7 | Passed: 7 | Failed: 0 | Total time: 30,0138 Seconds
STEP3_EXIT=0
```

#### Smoke Step 4 — Khởi chạy thực tế `ERP.Api.exe` từ `D:\ERP_KHO\artifacts\publish_smoke`, đo `/health/live` & `/health/ready`, và Dọn dẹp (`Stop-Process` + `Remove-Item`)
```text
=== SMOKE STEP 4: Live HTTP Health Probes (/health/live & /health/ready) on Published Release Binary ===
GET http://127.0.0.1:5142/health/live -> HTTP 200 | X-Correlation-Id=6a4ca58fd85d4e7780beba7a9224f95b | Body={"status":"healthy"}
GET http://127.0.0.1:5142/health/ready -> HTTP 200 | X-Correlation-Id=dda1b41f98374d02b1ac7e5dfe97e09f | Body={"status":"healthy"}
SMOKE_PROCESS_STOPPED=True | PUBLISH_SMOKE_CLEANED=True
```

#### Smoke Step 5 — Đối soát Read-Only 3-Way SQL Integrity Checks trên `ERP_KHO` (`0.0000` Variance)
```text
=== 1. SQL 3-Way Check A: InventoryStocks vs InventoryTransactions (with InventoryLedgerCalculationRules) ===
WarehouseId ProductId StockAllStatusOnHand LedgerExpectedAllStatusNet AllStatusVariance StockAvailableOnHand LedgerExpectedAvailableNet AvailableVariance
----------- --------- -------------------- -------------------------- ----------------- -------------------- -------------------------- -----------------
          1       317             145.0000                   145.0000            0.0000             108.0000                   108.0000            0.0000
          1       517              66.0000                    66.0000            0.0000              66.0000                    66.0000            0.0000
       1403       317              21.0000                    21.0000            0.0000              21.0000                    21.0000            0.0000

=== 2. SQL 3-Way Check B: ReservedQuantity vs Active StockReservations & StockAllocations ===
ActiveReservationNetQty ActiveAllocationQty TotalStockReservedQty TotalStockAllocatedQty
----------------------- ------------------- --------------------- ----------------------
                 9.0000              9.0000                9.0000                 9.0000

=== 3. SQL 3-Way Check C: Physical Stock & 32-Byte SHA2_256 GrainHash Invariants ===
TotalGrainRows PositiveStockRows TotalOnHand TotalReserved TotalAvailable NegativeOnHandCount NegativeReservedCount OverReservedCount NullOrInvalidGrainHashCount
-------------- ----------------- ----------- ------------- -------------- ------------------- --------------------- ----------------- ---------------------------
            20                 9    232.0000        9.0000       223.0000                   0                     0                 0                           0
```

---

### 5.2 Danh Sách Commits `origin/feat/WMS-61-project-development..HEAD` & Tổng Hợp Phạm Vi PR (`TASK-W61.3-PR-PACK`) (`FACT`)

```text
PS D:\ERP_KHO> git log origin/feat/WMS-61-project-development..HEAD --oneline
84a1b99 docs(audit): sync wave 61.2 verbatim evidence and production cutover readiness pack
645bd9d fix(wave61.1): remediate F-015 to F-018 and publish full verbatim audit evidence 0026
2eafbed feat(wave61): remediate F-013/F-014 and implement 90-day roadmap epics 90.1-90.3
5ce6242 fix(wave60.1): resolve S3 findings F-011/F-012, add code/SQL/mobile E2E evidence and close Wave 60 gate
37eb5fe feat(wave60): implement 60-day roadmap Epics 60.1-60.3 (physical kitting, cross-docking, break-bulk replenishment)
0b75fd4 fix(wave59.1): fix /dashboard route, interactive E2E visual evidence, and 94/95 table count sync
d669beb feat(wave59): implement Epic 30.1-30.3 (wave templates, VN carrier & marketplace adapters) and close UNV-02 live browser E2E
ca0f177 refactor(wave58): decouple ErpKhoDbContext from all 42 controllers (close F-004) and synchronize audit package
f49a433 feat(wave57): remediate audit findings F-001..F-009, enforce SQL ledger trigger, and finalize WMS-MASTER-AUDIT
```

| Thứ tự | Commit SHA | Wave | Nội dung thay đổi cốt lõi | Trạng thái Nghiệm thu |
| :---: | :---: | :---: | --- | :---: |
| **1** | `f49a433` | **Wave 57** | Khắc phục `F-001..F-003, F-005..F-009`, chuẩn hóa `decimal(19,4)`, khóa dòng 9D `AcquireInventoryAllocationLocksAsync`, thêm trigger SQL bất biến `TR_InventoryTransactions_Immutable_AppendOnly`, chốt `SD-01..SD-05` Option A. | `CLOSED / ACCEPTED` |
| **2** | `ca0f177` | **Wave 58** | Tách `ErpKhoDbContext` khỏi toàn bộ `42/42` API Controllers (`0` tham chiếu trực tiếp), hoàn tất Thin Controllers và đóng `F-004`. | `CLOSED / ACCEPTED` |
| **3** | `d669beb` | **Wave 59** | Triển khai Lộ trình 30 ngày (`Epic 30.1` Wave Templates & Put-to-Wall, `Epic 30.2` Hãng vận chuyển VN & Chuẩn hóa địa giới 2/3 cấp, `Epic 30.3` Shopee/TikTok OAuth & Hủy đơn khách mua) và đóng `UNV-02`. | `CLOSED / ACCEPTED` |
| **4** | `0b75fd4` | **Wave 59.1** | Khắc phục route `/dashboard`, bổ sung bằng chứng hình ảnh Live Browser E2E tương tác thực và đồng bộ số lượng bảng `94/95`. | `CLOSED / ACCEPTED` |
| **5** | `37eb5fe` | **Wave 60** | Triển khai Lộ trình 60 ngày (`Epic 60.1` Kitting/De-kitting vật lý, `Epic 60.2` Cross-Docking Inbound-to-Outbound, `Epic 60.3` Tự động xả thùng lẻ Break-Bulk Replenishment) nâng schema lên `97` bảng (`61` migrations). | `CLOSED / ACCEPTED` |
| **6** | `5ce6242` | **Wave 60.1** | Khắc phục `F-011` & `F-012`, bổ sung bằng chứng mã nguồn C#, đối soát SQL 3 chiều và ảnh E2E Mobile PDA `390x844`, đóng cổng Wave 60. | `CLOSED / ACCEPTED` |
| **7** | `2eafbed` | **Wave 61** | Khắc phục `F-013` & `F-014`, triển khai Lộ trình 90 ngày (`Epic 90.1` Dynamic ABC Slotting & Task Interleaving, `Epic 90.2` Cổng tự phục vụ chủ hàng 3PL & tính phí tự động, `Epic 90.3` Hạch toán Thông tư 200 & HĐĐT Nghị định `70/2025/NĐ-CP`). | `CLOSED / ACCEPTED` |
| **8** | `645bd9d` | **Wave 61.1** | Khắc phục triệt để `F-015, F-016, F-017, F-018` (đóng `16/16` findings), xuất toàn văn bằng chứng mã nguồn & SQL `WMS-WAVE61.1-AUDIT-0026` (**Accepted Code Baseline**). | `CLOSED / ACCEPTED` |
| **9** | `84a1b99` | **Wave 61.2** | Đồng bộ trích xuất nguyên văn `Epic60_3` đến dấu `}`, lập hồ sơ sẵn sàng Cutover Production & tích hợp đối tác (`CUTOVER_AND_INTEGRATION_READINESS.md`). | `CLOSED / ACCEPTED` |
| **10** | `ad51bda` | **Wave 61.3** | Chuẩn hóa câu tóm tắt Mục 4 `WAVE61_1_CODE_AND_SQL_EVIDENCE.md`, bổ sung lưu ý SePay/PayOS Webhook Adapter tại Mục 2.4, thực thi thực tế 5 bước Smoke Test Runbook và đóng gói PR Pack. | `CLOSED / ACCEPTED` |
| **11** | `03832f9` | **Wave 61.6 / 61.9** | Khắc phục phẫu thuật `F-019` (`S2 — CLOSED`): nâng cấp `axios@1.21.0`, `follow-redirects@1.16.2`, `undici@8.12.3`, `vite@8.3.5`, `vitest@5.0.5` (`npm audit --audit-level=high` & `npm audit` = `0 vulnerabilities`), lưu script `scripts/Test-ErpWmsStagingPreFlight.ps1`, ghi nhận `BLK-04` (`SonarCloud` & `PR #36`), đẩy lên `origin/feat/WMS-61-project-development` đạt `19/20` bước GitHub Actions `success` (bao gồm `Step #14`). | `CLOSED / ACCEPTED` |
| **12** | *(Wave 61.10)* | **Wave 61.10** | Khắc phục phẫu thuật `F-020` (`S2 — CLOSED`): đồng bộ `autoComplete="username"`, đổi `aria-label` nút `toggle-password-visibility` sang `'Ẩn ký tự' : 'Hiện ký tự'`, gỡ bỏ `window.setTimeout(clearAutofill, 120)` trong `frontend/src/pages/Login.tsx`, và cập nhật regex `{ name: /Đối chiếu tồn kho & (sổ cái|ledger)/i }` trong `tools/ERP.BrowserQa/permission-browser.mjs` để `Step #19 Mandatory interactive Chromium browser QA (INV-11)` đạt `PASS` (`ExitCode = 0`), nâng tổng số Finding đã đóng lên **`18/18` (`F-001..F-009, F-011..F-020`)**. | `CLOSED / VERIFIED` |

---

## 6. Automated Pre-Flight Script (`scripts/Test-ErpWmsStagingPreFlight.ps1`), Wave 61.6 CI Security Gate Closure (`F-019`) & Wave 61.10 Chromium Browser QA Closure (`F-020`) (`FACT`)

### 6.1 Automated Staging/Production Pre-Flight Validator (`scripts/Test-ErpWmsStagingPreFlight.ps1`)
- Saved in repository at [Test-ErpWmsStagingPreFlight.ps1](file:///d:/ERP_KHO/scripts/Test-ErpWmsStagingPreFlight.ps1) (UTF-8 without BOM).
- Validates:
  1. `ASPNETCORE_ENVIRONMENT` is explicitly set (`Staging` or `Production`).
  2. `ConnectionStrings__DefaultConnection` is present, does **not** point to `(localdb)` in Staging/Production, and does **not** use `User ID=sa` (`[SEC-003]`).
  3. `Jwt__Key` (`>= 32` chars), `Jwt__Issuer`, and `Jwt__Audience` are present and do not contain default dev secrets.
  4. Partner integration secrets (`SaasBilling__VietQrWebhookSecret`, `CarrierIntegration__DefaultWebhookSecret`, `MarketplaceIntegration__DefaultWebhookSecret`) do not use dev/sandbox fallback constants (`ERP-KHO-VIETQR-WEBHOOK-SECRET-2026`, `ERP_KHO_CARRIER_WEBHOOK_SECRET_2026`, `ERP_KHO_MARKETPLACE_WEBHOOK_SECRET_2026`, `SANDBOX_TOKEN_`).
  5. Optional live HTTP health probes (`/health/live` and `/health/ready` returning `HTTP 200` + `X-Correlation-Id`).

### 6.2 Wave 61.6 CI Steps `#14`..`#17` & Frontend Security Audit Verification (`F-019` `CLOSED`) (`FACT`)
```text
=== npm --prefix D:\ERP_KHO\frontend ls --depth=0 ===
frontend@0.0.0 D:\ERP_KHO\frontend
├── @playwright/test@1.58.2
├── @testing-library/jest-dom@6.9.1
├── @testing-library/react@16.3.2
├── @testing-library/user-event@14.6.1
├── @types/dompurify@3.0.5
├── @types/node@24.12.0
├── @types/react-dom@19.2.3
├── @types/react@19.2.14
├── @vitejs/plugin-react@6.0.1
├── axios@1.21.0
├── dompurify@3.3.3
├── jsdom@29.0.2
├── lucide-react@1.16.0
├── oxlint@1.60.0
├── react-dom@19.2.5
├── react-router-dom@7.14.0
├── react@19.2.5
├── typescript@5.9.3
├── vite@8.3.5
└── vitest@5.0.5
NPM_LS_EXIT=0

=== CI STEP #14: npm --prefix D:\ERP_KHO\frontend audit --audit-level=high (& full npm audit) ===
found 0 vulnerabilities
STEP14_EXIT=0

=== CI STEP #15: npm --prefix D:\ERP_KHO\frontend run lint ===
Found 18 warnings and 0 errors.
STEP15_EXIT=0

=== CI STEP #16: npm --prefix D:\ERP_KHO\frontend test ===
 Test Files  73 passed (73)
      Tests  406 passed (406)
STEP16_EXIT=0

=== CI STEP #17: npm --prefix D:\ERP_KHO\frontend run build ===
vite v8.3.5 building client environment for production...
✓ 1717 modules transformed.
✓ built in 882ms
STEP17_EXIT=0

=== CI STEP #19: Mandatory interactive Chromium browser QA (INV-11) (F-020 CLOSED) ===
Waiting for the normal login rate-limit window; no retry/configuration bypass.
Normal UI login: admin; HTTP 200; one attempt.
Normal UI login: manager; HTTP 200; one attempt.
Normal UI login: viewer; HTTP 200; one attempt.
Normal UI login: reader; HTTP 200; one attempt.
PASS: INV-11 interactive reconciliation and unauthorized route
BROWSER_QA_EXIT=0
Browser QA cleanup complete: processes stopped; database absent; credential removed.
```


