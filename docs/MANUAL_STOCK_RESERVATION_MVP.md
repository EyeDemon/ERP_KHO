# Quản lý giữ hàng Manual MVP

Trạng thái local hiện hành: **LOCAL SUCCESSOR VERIFICATION PASS**. External CI/SonarCloud của HEAD cuối được đối chiếu trong PR body; không tự suy local PASS thành external PASS. PR #32 vẫn Draft; chưa ghi owner đã chấp nhận Manual MVP. Base đã nghiệm thu vẫn `ae1f2c1c58197360a23d2d8dd2231ab600d7fa22`.

## SonarCloud và accessibility successor — 2026-10-10

Official Sonar CLI 1.9.0, EU/organization `eyedemon`, authentication qua OS Keychain. Native PR analysis xác nhận HEAD `ac407d5707d0b4c267df0130cd0b4fc03f739af3`, nhận đủ 58 issues trong một trang. Exact file/rule/line/severity/message và review decisions: [SONARCLOUD_PR32_REVIEW.md](SONARCLOUD_PR32_REVIEW.md). Không đổi SonarCloud config, quality gate/exclusions/CI, không suppress và không ghi credential/token.

Tại ac407d5, CI `37941467183` **SUCCESS**: Application 352/API 216/frontend 109; SonarCloud là check riêng, **FAIL** vì `javascript:S2871` tại runner line 100. Duplication hiện **2.8% PASS**, không dùng cảnh báo 3.1% lịch sử làm trạng thái hiện hành.

Fix tối thiểu: sort HTTP statuses bằng numeric comparator và `new Error`; giữ cell semantics cho loading table bằng native `output` bên trong `td`. Không backend/model/migration/dependency/security-configuration change; không refactor authorization pipeline hoặc tạo abstraction chỉ để giảm complexity/duplication. Các maintainability recommendations còn OPEN được ghi riêng, không gọi là Critical security defects hoặc đã đóng.

- Component precursor **1 FAIL / 8 skipped** chứng minh thiếu accessible cell trước sửa; focused successor **9/9 PASS**.
- Frontend full **110/110 PASS**, 0 failed/skipped; lint/production build/dependency audit PASS, 0 vulnerabilities. Runner failure/ownership/redaction **18/18 PASS**.
- Browser precursor `a1b88bc218e2489cb8dc75d8416d18d1`: **1 FAIL**, giữ nguyên. Assertion tìm cell theo accessible name không phù hợp Chromium: native output được expose là status nhưng không đưa tên vào cell. Static Chromium snapshot xác nhận `cell → status`; sửa locator kiểm tra đúng cell chứa status, không đổi application để khớp assertion.
- Focused browser successor `919231a872e340c09d4aee72b6de2271`: **2/2 PASS**, normal login một lần/persona, unchanged rate limiter. **Production UI** create/release/loading: native cell/status bằng tiếng Việt, double-click một request, On Hand 200/reserved 15/ledger 0/audits 2/claims 2. **Browser-origin HTTP + SQL coordination/postconditions**: reserve 201/409, release 200/409, mỗi cặp pending ít nhất 250 ms; một effect/audit/claim mỗi winner, On Hand/ledger không đổi. Không gọi concurrency HTTP là UI workflow.
- Run source association: HEAD ac407d5 + verified working-tree hashes, sau đó commit nguyên source. `StockReservations.tsx` SHA-256 `0bdebd91c830a5fdae931e444561f1c27ea06aeacf9da5a88fbe68bb279f0a6d`; Manual runner `467892cccc5f7b72fa5d6d232788d0b434aa6efb58a34f8de94208ce2a20911f`; backend filter vẫn `ce8ec1534e786eefd1e0b84bd74785a34d0b3ee4ada5697a93c6ca40f00f47fa`.
- Không lặp local Application/API SQL hoặc bảy nhóm browser đã đủ evidence: backend/permission/state/snapshot/generation logic không đổi. Retained mounted/Viewer/replay evidence giữ source association cũ; chỉ loading markup và runner assertions có successor mới trên đây.
- Cả hai fresh runs đã cleanup theo marker/process identity: exact databases absent, browser Closed=true, credential/profile/process/listener không còn; ERP_KHO ONLINE qua metadata only. Không truy cập business tables; ignored reports không stage.

Ponytail review: **Lean already. Ship.** Đây là complexity assessment, không phải independent review hoặc production release approval. Final CI/SonarCloud read-back thuộc HEAD sau push ghi trong PR body; không tạo commit tài liệu chỉ để lặp CI.

## Replay filtering acceptance — historical source-specific predecessor

## Nghiệm thu PR #32 — replay filtering successor, 2026-10-09

Notion Page 17 §35 `2026-10-08T16:48:56.271Z` và Page 30 §21 `2026-10-08T16:49:12.197Z`: **UNCHANGED** qua native fetch trước review và trước handoff. Không ghi/sửa contract. Review chỉ Manual reservation MVP; không mở lại outbound acceptance hoặc các scope deferred.

Finding **P2 — CLOSED**: `IdempotentCommandFilter` trả nguyên JSON của `StockReservation.Create` từ cached record. Manager tạo thành công rồi mất `reservation.release`, hoặc chuyển database classification sang Viewer có explicit create grant, vẫn nhận token cũ qua replay mặc dù fresh DTO phải omit token. Không phải permission escalation: mutation capability/scope vẫn được kiểm tra. Fix chỉ lọc `rowVersion` trong response replay theo database classification/grants hiện tại; stored response, reservation, audit và claim không đổi. Không thêm migration, permission, dependency hoặc engine.

| Nhóm contract | Nghiệm thu | Source/evidence và giới hạn |
| --- | --- | --- |
| Ba permission/bundles/database authority | PASS | Exact six-action registry; Staff seed read/create, unknown deny; successor Manual HTTP 10/10 |
| Manual-only public mutations; ExportReceipt read-only | PASS | Release/direct-ID/replay/implicit Manual expiry và scoped bulk expiry; accepted ExportReceipt internal lifecycle không đổi |
| On Hand/ledger boundary; reconciliation read-only | PASS | SQL assertions giữ physical quantity/ledger, chỉ reserved quantity đổi; browser postconditions riêng |
| Base-UOM/precision/legacy nullable | PASS | Snapshot ổn định sau master change, zero/negative/precision rejected, không backfill legacy từ master |
| Concurrency/no oversell/exactly once | PASS | Controlled SQL-backed overlap; retained browser reserve 201/409 và release 200/409; full API successor |
| Replay permission/all-warehouse reauthorization | PASS | Revoke capability 403/membership 404; bulk expiry rechecks mọi kho correlated audit; cached-response filtering mới được kiểm chứng riêng |
| Viewer/sensitive-response filtering | PASS | Fresh list/detail evidence giữ nguyên; successor cached create token absent, kể cả Viewer có explicit grants; không cost/value/private/idempotency metadata |
| Migration provenance/Designer/snapshot/rollback | PASS, có giới hạn | Migration không đổi; full API successor bao gồm upgrade/nullable legacy/totals và Down guard 51014; production rollback vẫn cần approved backup/data plan |
| UI tiếng Việt/accessibility/guards/mounted races | PASS trong matrix MVP | Frontend unchanged; retained production UI và component-in-browser mounted list/detail; không gọi HTTP replay là UI workflow |

Successor verification (fix chạy khi HEAD còn `d59a955…` + working-tree hashes, sau đó commit nguyên source):

- Release `ERP.slnx`: PASS, 0 warning/error; EF pending-model PASS.
- Pre-fix regression **0 PASS / 1 FAIL / 1**, TRX `6c5e5c6d-9808-4a92-8a95-099aa2d748d9`, retained tại ignored `acceptance-precursor/replay-projection.trx`; failure là property token vẫn có sau revoke.
- Focused Manual SQL/HTTP **10/10 PASS**, 0 failed/skipped, TRX `594701df-4cee-40d8-9799-0781c76d4363`, ignored `acceptance-successor/manual-focused.trx`; 9/9 owned targets cleaned.
- Full API SQL/HTTP **216/216 PASS**, 0 failed/skipped, TRX `65f5d527-1d9c-49ae-b066-834c85773d7c`, ignored `acceptance-api-successor/api.trx`; 42/42 owned targets cleaned.
- Browser runner failure/ownership/redaction regressions **18/18 PASS**. No frontend/application/model change: local Application 352/frontend 109/lint/build/audit evidence below remains source-specific and unaffected; không chạy lại local suites chỉ để tăng PASS. Successor GitHub CI được đối chiếu riêng trong PR body.
- Browser focused **1/1 PASS**, Run `02b1b36e2e2a42d987b9a2e52f1217fa`: normal UI login mỗi persona một lần; Admin grant/revoke và Manager create/replay là **browser-origin HTTP**, database role downgrade là **SQL fixture**, không gọi là UI administration workflow. Create/replay 201; administration 200; release-revoked replay token absent; regrant original token retained; Viewer classification token absent dù explicit create/release grants. Một reservation/audit/claim; On Hand 200, reserved 1, ledger 0. Stored original cached response không bị rewrite.
- Final runtime filter SHA-256: `ce8ec1534e786eefd1e0b84bd74785a34d0b3ee4ada5697a93c6ca40f00f47fa`. Browser evidence chứa runner/module hashes của bản đã chạy; sau PASS chỉ đổi equality assertion thành boolean với safe message để failure không in token, giữ nguyên assertion semantics; Node 18/18 successor. Không gán browser runtime PASS cho `d59a955…` chưa chứa fix.

Bảy nhóm composite lịch sử bên dưới được giữ với phạm vi chính xác: successor runtime chỉ thêm filtering cho **cached Create**; initial create, Release/Expire, stock, snapshots, UI và mounted entry không thay đổi. Không relabel run partial/FAIL thành full-run PASS hoặc lấy file hash filter cũ làm hash successor.

Review nội bộ toàn PR theo Ponytail/correctness/security và ui-ux-pro-max: finding P2 đã đóng, không còn finding High/Critical hoặc mandatory contract gap trong slice. Ponytail: **Lean already. Ship.** Complexity assessment không phải independent security review hay production approval.

Cleanup: precursor 1/1, focused 9/9, API 42/42 targets cleaned; metadata recheck **53 exact run targets absent** (bao gồm browser), ERP_KHO **ONLINE** qua sys.databases only. Browser Closed=true, credential/profile/process/listener thuộc run không còn. Không đọc business tables ERP_KHO; giữ main/worktrees/.npm-cache/UNKNOWN cache/policy-blocked helper. TRX/logs/manifests vẫn ignored và không stage.

Membership UI, backup/restore và các advanced scope vẫn **DEFERRED_BY_OWNER**. READY FOR OWNER ACCEPTANCE chỉ khi successor CI HEAD cuối đạt; owner acceptance không tự suy ra từ báo cáo này. PR #1/#24/#32 giữ Draft. Không merge/deploy/remote staging/capacity.

## Checkpoint triển khai trước acceptance fix — historical, superseded ở trạng thái handoff

Trạng thái: **LOCAL VERIFICATION PASS — SUCCESSOR CI PENDING IN NEW DRAFT PR**. Branch `feature/manual-reservation-mvp`, base được owner nghiệm thu `ae1f2c1c58197360a23d2d8dd2231ab600d7fa22`; kiểm chứng dưới đây gắn với working-tree source/hashes của slice, không gán evidence outbound cũ cho runtime mới. PR #1/#24 giữ Draft; owner acceptance không phải production release approval.

## Authority và phạm vi

Owner đã chốt và Notion đã được ghi/read-back trước source changes:

| Nguồn | Native trước | Native sau/read-back |
| --- | --- | --- |
| Page 17 §35 | 2026-10-04T02:21:04.902Z | 2026-10-08T16:48:56.271Z |
| Page 30 §21 | 2026-10-04T02:21:08.881Z | 2026-10-08T16:49:12.197Z |

Page 17 §34 và Page 30 §20 ExportReceipt không bị sửa; Page 18/41/84/229 đã được đọc lại. QC-before-Post, Post-only inbound boundary và outbound approve/dispatch/cancel giữ nguyên. Connector không cung cấp cờ truncation; read-back xác nhận nội dung addendum và các contract hiện hữu liên quan.

Manual là nguồn nhu cầu khai báo riêng, không đòi SalesOrder. Không phải canonical Allocation Engine. SalesOrder, Shipment, Picking/Packing/HU, FIFO/FEFO, alternate UOM và backorder ngoài phạm vi. Backup/restore: **DEFERRED_BY_OWNER — PRIORITIZE FUNCTIONAL DEVELOPMENT**; kế hoạch cũ được giữ, chưa kiểm chứng drill.

## API và permission

| Endpoint/action | Permission | Mutation boundary |
| --- | --- | --- |
| GET /api/stock-reservations | reservation.read | scoped list, filter/count trước pagination |
| GET /api/stock-reservations/{id} | reservation.read | isolated 404 ngoài kho |
| GET /api/stock-reservations/reconciliation | reservation.read | chỉ đọc, không sửa số dư |
| POST /api/stock-reservations | reservation.create | tạo Manual; giữ AVAILABLE eligible, không giảm On Hand/ledger |
| POST /api/stock-reservations/{id}/release | reservation.release | Manual only, token hiện hành, giải phóng tối đa remaining |
| POST /api/stock-reservations/expire | reservation.release | scoped Manual hết hạn only |

Admin/Manager: ba quyền; WarehouseStaff: read/create, không seed release; Viewer: read; role khác deny-by-default. Migration không xóa grant quản trị hợp lệ. Database grants và database role classification là authority; JWT Admin cũ không mở capability hoặc global scope. Warehouse intersection vẫn riêng. Viewer không nhận mutation token, cost/value, private release reason hay idempotency metadata.

Giữ hàng thuộc ExportReceipt chỉ đọc tại màn hình/API độc lập, kể cả direct-ID và replay. Internal reserve/consume/release/expiry của ExportReceipt vẫn do service nghiệp vụ đã nghiệm thu quyết định, không cần grant quản trị reservation độc lập. Manual create chỉ implicit-expire Manual cùng product/warehouse.

## Quantity, transaction và replay

Manual dùng positive Base quantity với precision 0..4 theo schema; không làm tròn. Snapshot ID/code/name/precision được lưu trong transaction tạo. Thay master không diễn giải lại reservation đã tạo; legacy nullable không backfill.

Release dùng SQL rowversion 8 bytes và eligible stock conditional-update; missing/malformed/stale token trả safe 409. Row lock trước claim bảo vệ release/replay; UnitOfWork dùng transaction hiện hành của idempotency filter, commit audit/stock/reservation/response atomically. Competing creates không oversell. Expire scope/order cố định; RowVersion + rollback chặn duplicate release.

Capability/resource checks chạy trước durable claim; failed Manual commands rollback claim/fingerprint/audit/effect. Replay thành công dùng semantics/token gốc nhưng reauthorize permission và membership hiện tại. Bulk-expire replay rechecks mọi warehouse trong correlated audit, không giới hạn ba scope columns của single-resource record. Same key/changed payload trả 409. Giữ hàng/release/expire không ghi inventory ledger hoặc thay Physical On Hand.

## Migration và rollback

EF-generated additive `20261009015806_AddManualReservationMvp`, Designer và snapshot đồng bộ. Thêm nullable Base-UOM snapshots và RowVersion; seed ba code/bundles bằng NOT EXISTS dưới permission administration lock. Không sửa migrations lịch sử, quantity/inventory/ledger history hoặc legacy snapshot.

Up/Down SQL đã được tạo và đọc đầy đủ. Down giữ catalog/grants; guard 51014 chặn mất snapshot hoặc administered permission provenance/audit. Down chỉ phù hợp fixture/quiesced application tương thích permission-backed, không phải business reversal hoặc cho phép quay lại runtime role-backed. Sau dữ liệu quản trị/snapshot cần backup và approved data plan; không blind downgrade production.

## Frontend và accessibility

`/stock-reservations` dùng effective permission set cho menu/route/actions. List/detail/create/partial release/scoped expiry/reconciliation đã nối API; optional master loads chỉ khi mở create và đủ product.read/warehouse.read. Reader không bị dependency denial phá list.

Synchronous submit guard; idempotency keys; giữ input khi lỗi; không tự replay 403/409. Generation/abort guards và permission change xóa list/dialog/master/stale state. Dialog reuse AccessibleDialog, focus containment/Escape/return; loading/empty/error/status/source/title/labels tiếng Việt, không dùng raw permission code làm nhãn. Responsive table scroll giữ dữ liệu; browser matrix kiểm chứng riêng.

## Historical automated evidence — checkpoint d59a955, 2026-10-09

- Release build: PASS, 0 warning/error. EF pending-model: PASS.
- Focused Manual SQL-backed HTTP: **9/9 PASS**, `TestResults/ManualReservation/focused-null-final/manual-focused.trx`; metadata/bootstrap focused **3/3 PASS**. Focused TRX Run `50e77ada-89af-48f4-ae02-002b4fb2e4ed`; eight marker-owned targets were cleaned.
- Full Application SQL: **352/352 PASS**, Run `437f196fe06a47ebbbf47145ab9c2e74`; official cleanup xác nhận database absent.
- Full API SQL/HTTP: **215/215 PASS**, 0 failed/skipped, TRX Run `943ed7d6-abc6-48fb-9f0f-c2dc5ba7460c`, `TestResults/ManualReservation/api-successor/api.trx`; 41/41 owned targets cleaned; precursor **213 PASS / 2 FAIL / 215** do hardcoded catalog-count và role-only metadata assertions cũ. Assertions được sửa theo exact registry/canonical action; không coi precursor là closure.
- Frontend: **109/109 PASS** (20 files); lint/production build/dependency audit PASS, 0 vulnerabilities.
- Browser runner error/ownership/redaction regressions: **18/18 PASS**. Không thay đổi dependencies hoặc production bundle bằng test entry.

Precursor focused discovery thiếu configured API harness env và JSON substring assertion trùng synthetic product label đã được giữ lịch sử; successor 9/9 dùng official SQL ownership harness và JSON property assertions. Evidence cũ App/API/frontend outbound là historical, không thay fresh slice verification.

## Historical browser composite và source association — trước acceptance replay fix

| Nhóm | Evidence PASS | Phân loại và assertions |
| --- | --- | --- |
| Create/partial release/double-click | `4981da386eec475aad2375bc41ed8b45` | Production UI: Admin tạo 20, Manager giải phóng 5; một request/effect cho double-click; On Hand 200 và ledger 0 không đổi; reserved 15, hai audit và hai claims |
| Viewer/Staff/stale JWT/errors | `4981da386eec475aad2375bc41ed8b45` | Browser-origin HTTP + UI: Viewer list/detail technical tokens/private/cost absent; 401/403/404/409 an toàn; stale Admin JWT dùng QAReader database role không khôi phục grants; Staff create 0.0001 được phép, release/expire 403 |
| Replay/revoke grant và membership | `7bec06aae38545d2bc7868bf10130909` | Browser-origin commands: release 200/200 exactly once, changed fingerprint/stale token 409; revoke grant 403/revoke membership 404 trước cached success; regrant/replay 200 không thêm audit/claim/effect |
| Controlled reserve/release races | `7bec06aae38545d2bc7868bf10130909` | Hai browser HTTP requests pending đồng thời ít nhất 250 ms trong SQL-held row/location lock; reserve 201/409, release 200/409; một winner/effect/audit/claim; không oversell, On Hand/ledger không đổi |
| Base snapshot/precision/eligibility | `7bec06aae38545d2bc7868bf10130909` | Browser HTTP + SQL master fixture: snapshot MR-EA/precision 4 không đổi khi master đổi; release 0.125 đúng snapshot; zero/negative/precision 400, ineligible stock 409, zero failed effects |
| ExportReceipt read-only/scoped Manual expiry | `7bec06aae38545d2bc7868bf10130909` | Browser HTTP: Export hold release/replay 409; manager/old-Admin-JWT reader foreign ID 404; expire chỉ một Manual của kho được phép, foreign Manual và Export hold còn Active; replay và Viewer reconciliation không đổi số dư/ledger |
| Mounted late list/detail + accessibility | `4981da386eec475aad2375bc41ed8b45` | Component-in-browser test entry: real apiClient 403 refresh, exact h1 còn connected xuyên revoke/regrant; old authorized list/detail không phục hồi mã cũ/dialog; 375px keyboard focus containment/Escape, title/status/labels tiếng Việt |

Bảy nhóm mandatory đã PASS qua composite successor evidence, không gọi một run partial là full-run PASS. Run `7bec…` tổng 5 PASS/2 FAIL: chỉ bốn nhóm backend-only ở bảng được dùng làm closure; service/controller/filter/stock repository hashes trùng source checkpoint d59a955 trước acceptance fix; không phải filter hash successor. Fix tiếp theo chỉ explicit labels/css/test ở frontend và runner wait, không thay backend/migration. Run `4981…` là selection UI/Viewer/mounted **3/3 PASS**, gắn với frontend cuối. Không chạy lại SQL suites chỉ vì thay label/test typing. File hashes nằm trong ignored browser evidence, không stage full responses/logs/credentials.

Precursor failures giữ riêng: `9e40d866d3df4b66b64886f30d58cb85` 4 PASS/3 FAIL; `7bec…` hai UI/Viewer failures; `c1c6832ecf574546a32f511e773f5474` startup loopback unavailable trước login; `60f6c45afc6b420881885a14e35aebe3` 0 PASS/2 FAIL; diagnostic `f977cf0a9ac84ec499e93b561653c08f` xác nhận label text nuốt option text. Không dùng pre-fix UI evidence cho closure.

Findings đã đóng:
- Explicit htmlFor/id cho Kho/Sản phẩm: browser thấy tên truy cập đúng, không gồm option text. Focused StrictMode component tests **8/8**, successor browser UI/Viewer/mounted **3/3**.
- Runner quiesce chỉ loại đúng held original request; không coi controlled hold là unrelated request. Capture failure propagation/ownership/redaction **18/18 PASS**.
- Full frontend **109/109 PASS**; precursor production build type-check phản đối `exact` trong Testing Library ByRoleOptions. Bỏ option không hỗ trợ trong test (string-name matching vẫn exact); focused 8/8, lint/build/audit successor PASS. Runtime không đổi vì sửa type của test.

Review nội bộ correctness/security đã kiểm tra sáu action, database authority, warehouse scoping/replay, Manual-only public mutations và implicit expiry, ExportReceipt compatibility, RowVersion/conditional stock rollback, Viewer filtering, migration provenance/Down guard và frontend generations/accessibility. Không còn High/Critical finding trong slice; không gọi review này là independent review. Ponytail whole-diff: **Lean already. Ship.** Đây chỉ là complexity assessment, không production approval. Không thêm dependency, permission code ngoài ba code canonical, engine hoặc abstraction speculative.

Skills thực tế đã đọc/áp dụng: Ponytail/Ponytail Review (reuse và whole-diff complexity), careful (exact ownership và protected paths), plan-eng-review (boundary/dependencies/acceptance), review (correctness/security), QA/QA-only (phân lớp evidence), ui-ux-pro-max SKILL.md (form labels, keyboard/focus, targets/loading/errors/generation guards theo design system hiện có). Drive router dùng cho metadata freshness.

**Historical pre-push checkpoint, superseded by current PR #32 handoff:** Còn trước handoff READY: exact staged/history scans, commit/push branch riêng, Draft PR và successor CI HEAD cuối. CI sẽ ghi trong PR body, không tạo commit tài liệu nối tiếp chỉ để lặp CI.

Drive: root **140 ảnh + 5 folders = 145 entries**; children 16/24/56/10/176, không folder con mới. Connector list limit 1000, kết quả từng folder dưới limit và không có continuation cursor. Artifact `143XzjlXxbtxR_EbQ7RcIjUqMdWnC4ECd` metadata-only **NOT REVIEWED**; illustrative reference, không security authority.

Cleanup chỉ exact Run ID/marker/process identity; giữ cache UNKNOWN/policy-blocked helper và .npm-cache. Không truy cập business tables ERP_KHO. Cả sáu browser manifests Closed=true, owned profiles/credentials/processes/listeners/databases còn lại = 0; API 41/41 targets cleaned; Application run cleaned. ERP_KHO chỉ đọc ONLINE qua sys.databases, không business tables. Accepted main/inbound/outbound/test/checkpoint worktrees và protected paths giữ nguyên. Source HEAD/CI cuối sẽ ghi trong Draft PR; tài liệu này ghi local evidence trước push.

REMOTE STAGING NOT AUTHORIZED · CAPACITY EXECUTION NOT AUTHORIZED · PRODUCTION NO-GO
