# Quản lý giữ hàng Manual MVP

## Tiện ích browser successor — 2026-10-11

**LOCAL UTILITY SUCCESSOR PASS.** Source: `f9bc0cd827af6b623692f5489cd61e3cc8d2fa62` cùng các hash working-tree đã kiểm chứng trước commit; external CI/SonarCloud của commit successor được ghi trong Draft PR #32, không dùng kết quả f9bc0cd để chứng nhận layout mới. Chưa có owner acceptance hoặc production approval.

Tool: **Codex In-app Browser / cua_repl**; frontend `http://127.0.0.1:4175`, API loopback `5265`. Đây là tương tác UI thật bằng tiện ích, không đổi tên evidence runner/HTTP thành utility. Owner tự nhập credential; tiện ích chỉ bấm đăng nhập, không đọc password field/clipboard hoặc ghi plaintext credential. Official bootstrap/marker và production security configuration giữ nguyên.

| Case | Source-associated utility evidence |
| --- | --- |
| Staff read/create, không release/expire | Run `9f6b291b43bc4751ba288b5dee596b13`: đăng nhập thành công sau handoff bằng executable PowerShell đã xác minh; tạo 5 tại Kho A, controls release/expire absent. Hash MainLayout/App.css/StockReservations.css trùng successor; không backend/permission change |
| Viewer list/detail/reconciliation | Run `c45b0797d53047c1897f66a0c546a61b`: nonempty Manual fixture; không mutation controls/cost/value/private/technical fields trên UI; đối chiếu chỉ đọc. Không gọi đây là raw HTTP filtering proof; giữ runner evidence riêng |
| Mobile và keyboard | 375/768 px: create/detail/release containment, Tab/Shift+Tab, Escape trả focus về trigger và outer scrollX 0. Boundary 720/721 và desktop 1280: detail/return focus không tràn document. Bảng giữ hàng vẫn scroll nội bộ và action truy cập được |
| Shared-layout smoke | Viewer list Sản phẩm/Phiếu nhập/Phiếu xuất/Cất hàng tại 375/768/1280; Admin Quản trị quyền tại cùng các widths. Document width bằng client width, kể cả vertical scrollbar; không dùng login responsive thay shared-layout smoke. Đây là list/route smoke, không tuyên bố toàn bộ form/bản in của các module đã review lại |
| Admin release và ExportReceipt read-only | Run c45b…: giải phóng Manual 1 từ 5 còn 4, loading/disabled và success tiếng Việt; ExportReceipt hold chỉ có Chi tiết dù Admin có release grant. Export hold là guarded SQL fixture, không gọi setup này là UI approve/reserve workflow |

Double-click utility được quan sát, SQL chứng minh một effect/audit/claim; **network count NOT OBSERVED BY UTILITY**. Network-count/raw-response/replay/concurrency evidence runner trước đó giữ nguyên phạm vi. SQL hậu điều kiện: Staff create Physical On Hand 200/reserved 5/ledger 0/audit 1; Viewer read không thêm effect/audit/claim; Admin partial release Physical On Hand 200/reserved 4/released 1/ledger 0/audit 1/claim 1 trước setup Export hold riêng. Không đọc business tables ERP_KHO.

Findings layout được sửa tối thiểu: shared flex minWidth:0/mobile sidebar và table containment; smoke phát hiện thêm bảng Sản phẩm (document 368/client 360), Phiếu nhập (605/360), Phiếu xuất (485/360). Native overflow region có tên tiếng Việt và keyboard focus sửa đúng bảng; successor document=client và ArrowRight chỉ scroll vùng bảng. Không che body/root overflow, bỏ cột hoặc thay authorization/hooks/generation guards. Pure layout regression được kiểm chứng bằng utility trước/sau; không thêm test mirror implementation.

Final frontend source: **110/110 tests**, 20 files, 0 failed/skipped; lint/production build/dependency audit PASS, 0 vulnerabilities. Không backend/model/migration change, không lặp local SQL suites. Diagnostic helper tests **13/13 PASS** bằng command doubles/resource-free, source không đổi; không coi là full startup/host termination proof. Review nội bộ UI/UX Pro Max, React checklist, careful/QA và Ponytail giữ native/reused patterns, không supervisor/dependency hoặc production configuration change.

Lifecycle: startup thành công không tự cleanup; các fresh runs từng healthy sau helper return. Run 9f6b… sau đó được quan sát cả hai process ABSENT, không có exit code/cleanup marker giải thích; termination cause vẫn **UNKNOWN**. Không quy thành production defect hoặc kết luận host stop_process đã gây termination. Recovery input lịch sử `rejected: blocked by policy` và error-document URL policy là hai trạng thái riêng; không retry input bị chặn bằng launcher/driver/transport khác. Fresh official Start được phép và utility tab mới truy cập thành công.

Cleanup: run 9f6b… và c45b… qua official Stop/verified marker, exact databases absent, credentials removed, owned API/frontend processes/listeners 0. Tab c45b… provider `browser-use:1ac3d7be-100c-4bf1-ac89-7b5f239c9070` đã đóng và viewport reset; tab lỗi cũ không rõ ownership giữ **UNKNOWN**, không đóng. Windows clipboard UNKNOWN giữ nguyên. Main/other worktrees/cache UNKNOWN/policy-blocked helper/.npm-cache bảo toàn; ERP_KHO ONLINE chỉ qua metadata. Ignored evidence có scalar assertions/hash, không public logs/credentials/response bodies.

Notion native recheck: Page 17 §35 `2026-10-08T16:48:56.271Z`, Page 30 §21 `2026-10-08T16:49:12.197Z` **UNCHANGED**; không sửa contract. Drive artifact vẫn metadata-only **NOT REVIEWED**; không tuyên bố đã visual review mới. Sáu Critical complexity recommendations còn OPEN là maintainability findings; không suppress hoặc gọi là security defects. Backup/restore và membership UI vẫn owner-deferred. PR #32 giữ Draft; REMOTE STAGING NOT AUTHORIZED · CAPACITY EXECUTION NOT AUTHORIZED · PRODUCTION NO-GO.

## Historical utility checkpoint 2026-10-10 — superseded by successor above

Yêu cầu tương tác bằng **Codex In-app Browser / cua_repl** bổ sung vẫn **PARTIAL**; không thay evidence runner hoặc CI/SonarCloud đã đạt. Source chạy UI: `f9bc0cd827af6b623692f5489cd61e3cc8d2fa62`, Run `ed3b43f298e38ddd2b57a7a3ea53bdac`, frontend `http://127.0.0.1:4175`, API loopback `5265`, official bootstrap và exact ownership marker. Admin đăng nhập do owner hỗ trợ; Manager nhập mật khẩu do owner hỗ trợ, tiện ích bấm đăng nhập. Không lưu/in plaintext credential hoặc đọc password field để vượt redaction.

| Case utility UI | Actual / phạm vi evidence |
| --- | --- |
| Admin tạo/detail/giải phóng từng phần | PASS: tạo 20, giải phóng 2; nguồn Manual, snapshot Cái giữ hàng; ExportReceipt hold chỉ có Chi tiết |
| Manager tạo/detail/giải phóng từng phần | PASS: tạo 10, giải phóng 3; chỉ Kho A trong selector/list |
| Scoped expiry và reconciliation | PASS: Manager xử lý đúng một Manual A hết hạn; Manual B và ExportReceipt hold còn active, released 0; đối chiếu chỉ đọc, không thêm effect |
| Validation/loading/error/stale | PASS: zero, precision và thiếu lý do có thông báo tiếng Việt; disabled/Đang xử lý được quan sát; SQL fixture đổi rowversion khi form mở rồi UI submit bị stale conflict, zero extra effect. Transient load/permission errors hiển thị thông báo an toàn, reload phục hồi; không gán lỗi môi trường này thành defect production |
| Double-click | PARTIAL theo capability: thao tác dblclick tạo/release/expire qua utility, SQL chứng minh một effect/audit/claim mỗi command; utility không cung cấp network count, không tuyên bố chỉ một HTTP request. Runner network-count PASS giữ phạm vi riêng |
| Keyboard/dialog/tiếng Việt | PASS trên Admin/Manager: Tab/Shift+Tab Kho/Sản phẩm, focus containment, Escape đóng và trả focus về trigger; labels/status/error/empty tiếng Việt |
| Staff và Viewer đăng nhập/thao tác utility | BLOCKED, chưa chạy. Runner Staff/Viewer PASS không thay lượt utility |
| Mobile 375×812 | FAIL precursor: document rộng 476 px, focus về trigger kéo trang ngang 116 px. Draft fix ở shared MainLayout + App.css + StockReservations.css; chưa browser successor, không ghi đã đóng |

SQL hậu điều kiện của đúng marker-owned database trước cleanup: product MR-QA Physical On Hand **200**, reserved **31**, ledger **0**, StockReservation audit **5**, claims **5**. Admin/Manager còn giữ 18/7; Manual A expired/released 1; foreign Manual B giữ 1; ExportReceipt hold giữ 5. SQL chỉ fixture và hậu điều kiện, không gọi là thao tác UI. Không quan sát raw HTTP status qua utility; status/replay/filtering trước đó thuộc runner evidence riêng.

Bản sửa mobile chưa commit: 110/110 frontend tests, lint/production build/audit PASS (0 vulnerabilities), diff check PASS. Không backend/model/migration change; không lặp Application/API SQL suites. Cần successor browser tại desktop/mobile trước commit/push; CI/SonarCloud hiện hành vẫn thuộc f9bc0cd. Sáu Critical complexity maintainability recommendations vẫn OPEN; không suppress hoặc tự coi là security defects.

Blocker: utility từ chối bind tab theo URL policy; owner mở trực tiếp thấy ERR_CONNECTION_REFUSED, API/frontend run đã kết thúc. Thao tác phục hồi hai process trên database hiện có bị automatic execution review từ chối `blocked by policy`; không retry bằng script/transport khác. Staff/Viewer và mobile successor vẫn BLOCKED. Không tạo run lặp để thay bằng runner PASS.

Official Stop-BrowserQa đã cleanup exact run: database absent, credential removed, process/listener 0. Tab utility chưa xác nhận đóng vì URL-policy blocker, giữ trạng thái UNKNOWN thay vì Browser Closed=true. Protected main/worktrees/.npm-cache/UNKNOWN cache/policy-blocked helper không bị tác động. Notion 17 §35 `2026-10-08T16:48:56.271Z`, 30 §21 `2026-10-08T16:49:12.197Z` UNCHANGED qua native fetch trước sửa; không viết contract. Drive metadata-only vẫn NOT REVIEWED.

**Chưa READY FOR OWNER ACCEPTANCE cho yêu cầu utility bổ sung; PR #32 giữ Draft, chưa có owner acceptance.**

### Review mobile và blocker — tiếp tục 2026-10-10, chưa browser successor

Đã đọc AGENTS.md và đầy đủ bản cài `ui-ux-pro-max/SKILL.md`; áp dụng Quick Reference accessibility/interactions/responsive cho web. Focused search `horizontal scroll responsive table --domain ux` xác nhận hai guidance phù hợp Web: nội dung vừa viewport, bảng rộng dùng wrapper `overflow-x:auto`. Không áp dụng ví dụ `overflow-x:hidden` lên document vì sẽ che nội dung/focus. Không cài Python; dùng runtime Python đã bundle sẵn để đọc dataset skill.

Static review ba file source (hash không đổi so với bản sửa draft đã test):
- `MainLayout.tsx`: content flex child có `minWidth:0`; class mới chỉ dùng bố cục, không đổi permission refresh/Outlet/route/action logic. Sidebar không shrink ở desktop; dưới 720 px chuyển lên trên nội dung và links wrap. Đây là thay đổi shared layout, chưa kết luận mọi route đã responsive.
- `App.css`: selectors chỉ `.main-layout/.main-sidebar`; không thêm body/root overflow hidden/clip, transform, fixed overlay hoặc rule ảnh hưởng focus. Desktop giữ sidebar 250 px; cần kiểm tra vùng 720/721/768 px vì trước đây sidebar được phép shrink.
- `StockReservations.css`: giữ table `min-width:980px` trong `.reservation-table{overflow-x:auto}`; wrapper mobile `max-width:100%` theo content parent, tools wrap. Không bỏ cột hoặc ẩn action để vừa màn hình.
- `AccessibleDialog` portal vào body, giữ containment/Escape/returnFocus; không sửa component này. Print overlay có CSS độc lập, không đổi bản in. Source strings/validation/accessibility vẫn tiếng Việt.

Smoke cần chạy khi QA và utility được phép: `/stock-reservations` ở 375×812 và 768×1024, thêm boundary 720/721 và desktop 1280; create/detail/release form/dialog, Tab/Shift+Tab/Escape/return focus phải có document scrollWidth không vượt clientWidth (dung sai 1 px), scrollX không tăng; bảng vẫn cuộn nội bộ và truy cập cột action được. `/warehouses` có wrapper cuộn table, `/approvals` có wrapper và breakpoint filter riêng; `/products` dùng table trực tiếp nên ưu tiên smoke dữ liệu dài để phát hiện tác động shared layout. Theo yêu cầu owner mới nhất, smoke còn phải bao gồm đăng nhập, phiếu nhập, phiếu xuất, Cất hàng và quản trị quyền với persona có quyền tương ứng; đăng nhập nằm ngoài MainLayout nên là kiểm tra không bị ảnh hưởng bởi App.css. Không sửa các route khác hoặc khai smoke PASS khi chưa chạy.

Phân loại chính xác các blocker (không retry operation bị từ chối):

| Nhóm | Operation / evidence | Kết luận |
| --- | --- | --- |
| Browser URL policy | Tool `mcp__cua_repl.js`, input `let qaTab = await cua.getTab({url:'http://127.0.0.1:4175/stock-reservations'}, {browser:'iab'});` bị từ chối, nguyên văn: `Browser Use rejected this action due to browser security policy. Reason: The browser URL policy blocks this action. Browser use cannot visit the requested page. The requested URL protocol is not allowed. Allowed protocols: "http:", "https:".` | Inventory read-only mới xác nhận tab run ID 1 là error document `data:text/html`; không phải protocol HTTP của API bị cấm. Không bind/navigate/close lại tab này bằng đường khác |
| Môi trường/process | PIDs cũ 9220/13672 không còn, ports 4175/5265 không LISTEN; owner báo ERR_CONNECTION_REFUSED. Inventory có thêm tab ID 2 tại HTTP URL nhưng cũng title `This site can't be reached`, ownership không xác định | Không có QA environment đang chạy; chưa xác định nguyên nhân process kết thúc. Không coi là lỗi ứng dụng hoặc bằng chứng lỗi authorization |
| Execution review | Tool `functions.exec` → `tools.exec_command`, workdir Manual MVP, một PowerShell compound command kiểm marker/absent PIDs, đọc connection qua official helper, định nghĩa QA env, hai `Start-Process`, cập nhật manifest và loop/finally cleanup. Toàn input bị chặn ở `CreateProcess`: `rejected: blocked by policy` | Không suy rằng review đã chỉ ra một subcommand cụ thể hay authorization bug. Không retry bằng sửa script/launcher/driver/transport, không tạo QA run mới. Hai lệnh launch được trích dưới đây; toàn input nằm trong tool record của hội thoại, không khai đã xuất thành artifact riêng và không thực thi lại |

Các lệnh launch đã nằm trong compound input bị từ chối (trích nguyên input, **documentation only**, không phải lệnh yêu cầu owner chạy):
```powershell
$qaApi=Start-Process dotnet -ArgumentList @("$qaRoot/ERP.Api/bin/Release/net10.0/ERP.Api.dll",'--contentRoot',"$qaRoot/ERP.Api",'--urls','http://127.0.0.1:5265') -WorkingDirectory $qaManifest.ArtifactRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $qaManifest.ArtifactRoot 'api.restart.stdout.log') -RedirectStandardError (Join-Path $qaManifest.ArtifactRoot 'api.restart.stderr.log') -PassThru
$qaFrontend=Start-Process node -ArgumentList @("$qaRoot/frontend/node_modules/vite/bin/vite.js",'--host','127.0.0.1','--port','4175','--strictPort') -WorkingDirectory "$qaRoot/frontend" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $qaManifest.ArtifactRoot 'frontend.restart.stdout.log') -RedirectStandardError (Join-Path $qaManifest.ArtifactRoot 'frontend.restart.stderr.log') -PassThru
```

Không chạy thêm tests/suites/browser khi source không đổi trong lượt review này. 110/110/lint/build/audit draft evidence giữ source hashes cũ; CI/SonarCloud PASS chỉ thuộc f9bc0cd. Staff read/create/no release-expire, Viewer read-only/no sensitive UI, mobile successor và shared-route smoke còn BLOCKED. Tab run 1 vẫn UNKNOWN cho cleanup; tab 2 không rõ ownership được giữ nguyên. Không commit/push bốn file draft trước gate browser cần thiết.

Checkpoint source f9bc0cd trước yêu cầu utility bổ sung: **LOCAL SUCCESSOR VERIFICATION PASS**. External CI/SonarCloud của HEAD này được đối chiếu trong PR body; không tự suy local PASS thành external PASS. PR #32 vẫn Draft; chưa ghi owner đã chấp nhận Manual MVP. Base đã nghiệm thu vẫn `ae1f2c1c58197360a23d2d8dd2231ab600d7fa22`.

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
