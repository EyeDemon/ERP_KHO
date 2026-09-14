# Receipt printing

## Scope and data source

Import and export receipt lists expose **Xem bản in**. The read-only preview performs a fresh `GET /api/importreceipts/{id}` or `GET /api/exportreceipts/{id}` and never prints unsaved form state or cached list data. Refreshing or reopening performs another GET. Loading clears the previous preview; an error leaves no stale printable receipt and disables printing because the dialog is absent. The request carries authentication in the normal API header, never in the URL.

No print-specific endpoint, server PDF, print marker, status transition, inventory mutation, signature, legal statement, price, amount, barcode, or QR code is introduced. Users invoke the browser print dialog and may choose Save as PDF when their browser provides it.

## Content and presentation

The A4 portrait stylesheet prints title, document code/date/status, warehouse, partner, creator/approver/dispatcher data when stored, line number, product code/name, base unit and quantity, line count, notes, and data-fetch time. Draft and Cancelled use explicit black-and-white-safe banners: `BẢN NHÁP — CHƯA DUYỆT` and `ĐÃ HỦY`. Other labels preserve workflow names; Approved is not presented as dispatched.

Draft partner fields come from current partner data. Non-Draft fields come from receipt snapshots. A missing historical snapshot or partner displays `Chưa ghi nhận`; the preview does not fall back to current partner master data. Product, Unit and Warehouse do not have equivalent receipt snapshots, so historical printouts show their current master-data values. This sprint does not claim byte-for-byte historical reconstruction.

CSS hides controls during print, repeats table headers where the browser supports `table-header-group`, avoids splitting rows where supported, permits wrapping long Vietnamese text, and does not use fixed content height or promise browser-controlled page headers, footers, or page counts. Escape closes the dialog and focus returns to its launch button. Fetch timestamps use the browser timezone and say so explicitly.

## Authorization

Printing inherits detail-read authorization and warehouse scope from existing receipt GET endpoints. No role is added. A direct ID outside the user's warehouse scope remains unavailable through the same repository-filtered detail query. A failed or expired request cannot retain the previously opened printable data. Viewing or printing is read-only and does not change workflow, stock, reservation, ledger, approval, or audit state.

## Verification record

Candidate base revision: `7f704102584b5a0a846e49eb72d8b749dc7a0593`; verification was performed on the working-tree receipt-printing candidate on 2026-09-14.

| Evidence | Result |
|---|---|
| Backend Release build | PASS, 0 warnings/errors |
| Application owned SQL harness | PASS 324/324; Run ID `1845d35711bc4b6aa95f43613f890570`; exact database cleaned |
| API suite with isolated SQL configuration | PASS 150/150 |
| Frontend component tests | PASS 49/49, including Draft/Cancelled/null/HTML-as-text/print/Escape/loading/error stale-data tests |
| Frontend lint | PASS |
| Browser UI, Run ID `6e9f7a21c4d84bbfa18d26a3510e9c72` | PASS for Approved import and Draft export through loopback frontend → real API → owned SQLEXPRESS; displayed backend code, status, warehouse, partner-null text, actors, unit, quantity, note and fetch time; Escape restored launch-button focus |
| Direct initial test invocation without SQL harness variables | FAILED 275/324 Application and 146/150 API because repository fail-closed guards require owned SQL configuration; corrected harness/configured runs above PASS |
| Browser print/PDF pagination | BLOCKED: Codex in-app browser invoked `window.print()` but exposed no controllable print-dialog or PDF artifact. Two-page first/middle/last page, repeated header and clipping evidence are therefore NOT RUN |
| Long multi-line browser receipt | NOT RUN because the required PDF export surface was unavailable; component/CSS checks do not replace PDF pagination evidence |

Self-review found and fixed a print-media isolation defect before commit: the initial stylesheet hid preview controls but could still include the application shell. The final rule hides all page descendants and reveals only the preview subtree for print.

The synthetic browser target used loopback ports only and an exact ownership marker. No Docker, tunnel, real ERP_KHO database, physical printer, or real credentials/data were used. Runtime artifacts and protected credentials remain excluded from Git.

Status: **TESTING INCOMPLETE** until a supported browser can export a real PDF and the first, middle and last pages of a multi-page receipt are inspected for repeated table header, wrapping, clipping and row breaks.
