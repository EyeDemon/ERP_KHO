# Permission browser runner

Test-only Playwright Library **1.62.1**, reused from the installed workspace runtime, with its installed PowerShell 7 (`pwsh.exe`) for the existing DPAPI/helper contract. No production dependency, bundle hook, browser security override, rate-limit override or second SQL/credential harness. Set `ERP_KHO_PLAYWRIGHT_MODULE` to the installed module's `index.js`; the runner rejects another version. Node's built-in `node:test` checks runner ownership/redaction guards without starting QA resources.

Read API documentation before running: [Page](https://playwright.dev/docs/api/class-page), [network observation/routing](https://playwright.dev/docs/network), [BrowserContext](https://playwright.dev/docs/api/class-browsercontext). `page.evaluate(fetch)` below means a real HTTP request from the authenticated browser page, not a user UI workflow. Routing may delay the original response in memory; it must not change grants, responses, production endpoints or security configuration.

Prepare/review the runner first. Then start one fresh run with `Start-BrowserQa.ps1`: configured local Integrated Security, exact marker, pre-permission migration, legitimate synthetic bootstrap Admin, additive migrations and loopback processes. Pass that run's manifest to the runner. It reuses `BrowserQaConnection.ps1`, the DPAPI credential and the exact ownership marker for fixture setup/postcondition SQL. Never target ERP_KHO business tables.

```powershell
# Set ERP_KHO_PLAYWRIGHT_MODULE to the installed module path, without copying it into source.
node --test tools/ERP.BrowserQa/permission-browser.test.mjs
node tools/ERP.BrowserQa/permission-browser.mjs --manifest TestResults/BrowserQA/<RunId>/manifest.json
# A focused successor may select named cases on a fresh owned run:
node tools/ERP.BrowserQa/permission-browser.mjs --manifest TestResults/BrowserQA/<RunId>/manifest.json --case 'Membership|Granular'
# Always run the existing guarded cleanup, including after runner failure:
./tools/ERP.BrowserQa/Stop-BrowserQa.ps1 -ManifestPath TestResults/BrowserQA/<RunId>/manifest.json
```

Wait until the startup helper exits successfully; the runner requires the completed PID/credential manifest. The Chromium server binds to loopback. The runner closes its in-memory browser contexts/server in `finally`, records their process identity and checks that its temporary profiles were removed. No storageState, HAR, video, trace, screenshot, password, bearer, cookie or full response body is written. Its ignored JSON evidence contains source/runner/fixture hashes, case names/classification, statuses, timings, assertions and scalar database counts only. A failed case remains failed; prior cases are not silently retried. Membership UI stays **DEFERRED_BY_OWNER**.

| Case | Capability and planned evidence | Classification |
| --- | --- | --- |
| Viewer | Real authenticated fetch, JSON property absence/null assertions; task/list/history/destinations and safe errors | Browser HTTP + SQL postconditions |
| Permission administration | Normal login, grant/revoke button, synchronous disabled/loading, network count, audit/claim delta | UI workflow |
| Replay/fingerprint/revocation | Same page/user/key; 200/200, changed payload 409, revoked permission/membership denial; no new audit/effect/claim | Browser HTTP + SQL postconditions |
| Aggregate/last-admin | Hold the existing SQL transaction lock in fixture helper; two page fetches observed pending; release, winner/conflict and canonical Admin invariant | Browser HTTP; SQL coordination/postconditions |
| Delayed identity/receipt | Capture original server response via route.fetch, revoke/regrant, release older response; menus/list/detail/print must stay current | UI + runner-only response coordination |
| Approval Center | Nonzero inbound A/B and outbound fixture; count/page/type assertions; independent read/reject, maker/isolation/replay | Browser HTTP + UI observation |
| Refresh/stale JWT | Same login, permission changes via administration HTTP; reload/refresh-cookie rotation; database role downgrade and old JWT denial | Browser HTTP + UI; SQL role fixture only |
| Membership aggregate | Empty-set token, held same-token grant/revoke race, replay/stale/fingerprint and revoked capability | Browser HTTP; SQL coordination/postconditions; API-only owner scope |
| Master capability separation | Product/Category/Barcode and Warehouse/Location grants independently toggled; raw denied/allowed actions and UI controls | Browser HTTP + UI observation |
| Operational guards | Maker/checker/poster, scoped assigned WarehouseStaff, membership-revoked terminal replay | Browser HTTP; SQL role fixture only |
| Vietnamese history/accessibility | Nonempty inbound history, translated states, title, dialog focus/return and route-denial copy | UI workflow |

SQL fixture grants/role changes are labelled setup, never called browser administration. A registry/component/API PASS does not replace an unrun browser case. The runner reports partial/failure and production NO-GO unless every mandatory matrix case actually passes.

Controlled delay captures only an original HTTP 200 response. Before regrant, unrelated identity/refresh requests must settle; otherwise a legitimate newer request would invalidate the assertion about the held older response. Request paths/status/timestamps are recorded without bodies or credentials. The runner respects the existing login/API rate-limit windows and never retries blocked logins. Selected runs are not represented as full-matrix results.
