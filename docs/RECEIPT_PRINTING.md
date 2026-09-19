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

## Chrome PDF pagination verification

Follow-up verification on 2026-09-19 used installed Google Chrome `153.0.8010.48` in headless mode and the DevTools `Page.printToPDF` command. The browser loaded the real React preview from the loopback frontend after it fetched each receipt from the real API and owned SQLEXPRESS database. `preferCSSPageSize`, A4 portrait, print backgrounds, and disabled browser headers/footers were used. Run ID: `c19d7e4a2b8f46d590a13c7e84b2f601`.

| PDF | Pages | Bytes | SHA-256 |
|---|---:|---:|---|
| `approved-import.pdf` | 3 | 202680 | `DD6F6D5F401905CCABA631DD7F789188EDF9C4D76136005A84B7D36D9C8912A1` |
| `draft-export.pdf` | 5 | 219507 | `82697055EDDCBC930EEBCE75B138B5FC69E3D94F653FE519D4CB3E9546643240` |
| `cancelled-export.pdf` | 1 | 86059 | `7923BA0DAB05248C83A295AF9EBC32BF63BC84996FC30E3AB4BA9F033A00A524` |
| `approved-import-repeat.pdf` | 3 | 202680 | `632C803ADAF97D0CDED6F34F88295A871B7F25B0161B6F536230E68C5F85DF06` |

All pages were rendered with the existing Poppler runtime and inspected individually. Approved Import pages 1/2/3 contained rows 1–24, 25–54, and 55–75 plus summary/notes. Draft Export pages 1/2/3/4 contained rows 1–15, 16–35, 36–55, and 56–75; page 5 contained the intentional line-count, multi-line note, and fetch-time footer rather than a blank page. Cancelled Export fit on one page. Each continued table page repeated the table header. No row was split, lost, duplicated, clipped, or reordered.

The renders contained only the preview subtree: no sidebar, navigation, modal backdrop, control, URL, or browser-generated header/footer. A4 portrait dimensions, black-and-white status text, Vietnamese glyphs, long code/name wrapping, units, decimal quantities, notes, and footer separation were visually correct. Approved displayed the stored Supplier snapshot, Draft displayed current Customer data, and Cancelled without a partner displayed `Chưa ghi nhận`.

Both long receipts contained exactly 75 backend detail rows. Database evidence after four print operations remained 76 inventory transactions (one helper seed plus 75 approved import lines), zero reservations, and unchanged receipt statuses: Approved Import, Draft Export, Cancelled Export. The repeated Approved Import export retained three pages and identical rendered pages 1 and 2; page 3 differed only in the displayed fetch timestamp, as designed. PDF metadata/hash equality is not required.

The earlier in-app-browser limitation remains historical evidence only; Chrome DevTools removed the pagination blocker. QA artifacts are retained locally under the ignored Run ID artifact directory and are not committed. Physical printer output remains untested.

Status: **READY FOR OWNER REVIEW**.
