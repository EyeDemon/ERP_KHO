import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { readFile, writeFile, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID, createHash } from 'node:crypto';
import { runOutboundCases } from './outbound-browser.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const require = createRequire(import.meta.url);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const key = () => randomUUID();
const conflict = 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.';

export function validateManifest(manifest, filename) {
  assert.match(manifest.RunId, /^[a-f0-9]{32}$/);
  assert.equal(manifest.Database, `ERP_KHO_BrowserQA_${manifest.RunId}`);
  assert.equal(manifest.MarkerType, 'LocalBrowserFullStackQA');
  const expected = path.join(repo, 'TestResults', 'BrowserQA', manifest.RunId);
  assert.equal(path.resolve(manifest.ArtifactRoot), expected);
  assert.equal(path.resolve(filename), path.join(expected, 'manifest.json'));
  for (const url of [manifest.ApiUrl, manifest.FrontendUrl]) {
    const parsed = new URL(url);
    assert.equal(parsed.protocol, 'http:'); assert.equal(parsed.hostname, '127.0.0.1');
  }
}

export function safeEvidence(value) {
  const text = JSON.stringify(value);
  assert(!/"(?:password|token|cookie|authorization|connectionString|rowVersion|body|headers)"\s*:/i.test(text), 'Secret-bearing evidence field');
  assert(!/Bearer\s|-----BEGIN.*PRIVATE KEY|eyJ[A-Za-z0-9_-]{20,}\./.test(text), 'Secret-bearing evidence value');
  return value;
}

const deniedKeys = new Set(['unitprice', 'cost', 'value', 'rowversion', 'passwordhash', 'refreshtokenhash', 'idempotencykey', 'fingerprint', 'stacktrace', 'exceptiontype']);
export function assertFiltered(value) {
  if (!value || typeof value !== 'object') return;
  for (const [name, child] of Object.entries(value)) {
    assert(!deniedKeys.has(name.toLowerCase()) || (['unitprice','cost','value'].includes(name.toLowerCase()) && child === null), `Viewer sensitive property must be absent or cost null: ${name}`);
    assertFiltered(child);
  }
  assert(!JSON.stringify(value).includes('QA_PRIVATE_EXCEPTION'), 'Viewer private content absent');
}

function helper(manifestPath, mode, request) {
  const child = spawn('pwsh.exe', ['-NoProfile', '-File', path.join(here, 'Invoke-BrowserQaFixture.ps1'), '-ManifestPath', manifestPath, '-Mode', mode], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  let stdout = '';
  // Only the safe stage/number/type/line diagnostic is surfaced; discard provider messages.
  let diagnostic = '';
  child.stderr.setEncoding('utf8'); child.stderr.on('data', text => { diagnostic += text; });
  child.stdout.setEncoding('utf8'); child.stdout.on('data', text => { stdout += text; });
  child.stdin.end(request ? JSON.stringify(request) : '');
  const completed = new Promise((resolve, reject) => {
    child.once('error', () => reject(new Error('QA helper unavailable')));
    child.once('exit', code => code === 0 ? resolve(stdout.trim()) : reject(new Error(diagnostic.match(/stage=[\w-]+; sqlNumber=\d+; kind=\w+; line=\d+/)?.[0] || 'Owned QA helper failed')));
  });
  return { child, completed };
}

async function main(manifestPath) {
  const manifest = JSON.parse((await readFile(manifestPath, 'utf8')).replace(/^\uFEFF/, ''));
  validateManifest(manifest, manifestPath);
  assert(manifest.ApiPid > 0 && manifest.FrontendPid > 0, 'Wait for the official startup helper to complete before running the browser');
  await access(path.join(manifest.ArtifactRoot, manifest.CredentialIdentifier));
  const modulePath = process.env.ERP_KHO_PLAYWRIGHT_MODULE;
  assert(modulePath, 'ERP_KHO_PLAYWRIGHT_MODULE must select the installed test tooling');
  const playwright = require(modulePath);
  const version = require(path.join(path.dirname(modulePath), 'package.json')).version;
  assert.equal(version, '1.62.1', 'Review the runner before changing Playwright version');
  manifest.SourceHead = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repo, encoding: 'utf8' }).trim();
  const selected = process.argv.includes('--case') ? new RegExp(process.argv[process.argv.indexOf('--case') + 1], 'i') : null;
  const mounted = selected?.test('Mounted receipt list/detail/print late responses');
  const outbound = selected?.test('Outbound UI reserve dispatch') || selected?.test('Outbound mounted late list detail print') || selected?.test('Outbound Approval Center independent grants nonzero count and isolation');
  const evidence = { sourceHead: manifest.SourceHead, runnerSha256: createHash('sha256').update(await readFile(fileURLToPath(import.meta.url))).digest('hex'), fixtureSha256: createHash('sha256').update(await readFile(path.join(here, 'permission-fixtures.sql'))).digest('hex'), runId: manifest.RunId, runnerVersion: version, selection: selected?.source || 'all', logins: [], cases: [], requests: [] };
  const reportPath = path.join(manifest.ArtifactRoot, 'permission-browser-evidence.json');
  const save = () => writeFile(reportPath, JSON.stringify(safeEvidence(evidence), null, 2));
  const sql = async (text, parameters = {}) => JSON.parse(await helper(manifestPath, 'Sql', { sql: text, parameters }).completed);
  let password;
  let server;
  let browser;
  const actors = {};
  let currentCase = 'startup';
  const run = async (name, classification, action) => {
    if (selected && !selected.test(name)) return;
    for (const actor of Object.values(actors)) {
      if (actor.windowRequests > 80 && Date.now() - actor.windowStart < 60000) {
        console.log('Waiting for the unchanged API rate-limit window before the next independent case.');
        await delay(61000 - (Date.now() - actor.windowStart));
      }
    }
    currentCase = name;
    const started = new Date().toISOString();
    try {
      const result = await action();
      evidence.cases.push(safeEvidence({ name, classification, status: 'PASS', started, finished: new Date().toISOString(), ...result }));
      console.log(`PASS: ${name}`);
    } catch (error) {
      evidence.cases.push({ name, classification, status: 'FAIL', started, finished: new Date().toISOString(), assertionLocation: String(error.stack).split('\n').find(x => /(?:permission|outbound)-browser\.mjs:/.test(x))?.match(/(?:permission|outbound)-browser\.mjs:\d+:\d+/)?.[0], diagnosticHash: createHash('sha256').update(String(error.message)).digest('hex').slice(0, 12), ...(error.pendingPaths ? { pendingPaths: error.pendingPaths } : {}) });
      process.exitCode = 1; console.error(`FAIL: ${name}; assertions stopped for this case; independent cases continue.`);
    }
    await save();
  };
  const fetchFromBrowser = async (actor, endpoint, method = 'GET', payload, idempotency = key()) => {
    const result = await actor.page.evaluate(async ({ url, bearer, method, payload, idempotency }) => {
      const started = performance.now();
      const response = await fetch(url, { method, credentials: 'include', headers: { Authorization: `Bearer ${bearer}`, 'Content-Type': 'application/json', 'Idempotency-Key': idempotency }, ...(payload === undefined ? {} : { body: JSON.stringify(payload) }) });
      const text = await response.text();
      let data; try { data = JSON.parse(text); } catch { data = null; }
      return { status: response.status, started, ended: performance.now(), data };
    }, { url: manifest.ApiUrl + endpoint, bearer: actor.bearer, method, payload, idempotency });
    evidence.requests.push({ case: currentCase, actor: Object.keys(actors).find(name => actors[name].page === actor.page), endpoint, method, status: result.status, started: result.started, ended: result.ended });
    return result;
  };
  const expectStatus = (response, status) => assert.equal(response.status, status, 'HTTP contract status');
  const roleSnapshot = async roleId => {
    const response = await fetchFromBrowser(actors.admin, '/api/permissions/roles'); expectStatus(response, 200);
    const role = response.data.find(r => r.id === roleId); assert(role, 'Fixture role exists'); return role;
  };
  const grant = async (roleId, code, add, actor = actors.admin, snapshot, idem = key()) => {
    const role = snapshot || await roleSnapshot(roleId);
    return fetchFromBrowser(actor, `/api/permissions/roles/${roleId}/grants${add ? '' : '/' + code}`, add ? 'POST' : 'DELETE', add ? { permissionCode: code, rowVersion: role.rowVersion } : { rowVersion: role.rowVersion }, idem);
  };
  const counts = () => sql(`SELECT (SELECT COUNT(*) FROM AuditLogs) AS audits,(SELECT COUNT(*) FROM IdempotencyRecords) AS claims,(SELECT COUNT(*) FROM InventoryLocationMovements) AS movements,(SELECT COUNT(*) FROM InventoryTransactions) AS ledger,(SELECT SUM(Quantity) FROM InventoryStocks) AS stock FOR JSON PATH, WITHOUT_ARRAY_WRAPPER`);
  try {
    currentCase = 'startup credential';
    password = await helper(manifestPath, 'Credential').completed;
    currentCase = 'startup fixture';
    const fixture = await sql(await readFile(path.join(here, 'permission-fixtures.sql'), 'utf8'));
    const outboundFixture = outbound ? await sql(await readFile(path.join(here, 'outbound-fixtures.sql'), 'utf8')) : null;
    currentCase = 'startup Chromium';
    server = await playwright.chromium.launchServer({ headless: true, host: '127.0.0.1' });
    browser = await playwright.chromium.connect(server.wsEndpoint());
    manifest.BrowserProfiles = server.process().spawnargs.filter(arg => arg.startsWith('--user-data-dir=')).map(arg => arg.slice('--user-data-dir='.length));
    manifest.BrowserRunner = { Version: version, Pid: process.pid, ServerPid: server.process().pid, Contexts: ['admin', 'manager', 'viewer', 'reader'], Ephemeral: true };
    await writeFile(manifestPath, JSON.stringify(manifest, null, 2));
    manifest.BrowserRunner.Processes = JSON.parse(await helper(manifestPath, 'Processes').completed);
    await writeFile(manifestPath, JSON.stringify(manifest, null, 2));
    // The official helper has made two setup logins. Respect the unchanged IP bucket before persona logins.
    console.log('Waiting for the normal login rate-limit window; no retry/configuration bypass.');
    await delay(61000);
    for (const persona of ['admin', 'manager', 'viewer', 'reader']) {
      currentCase = `normal first login: ${persona}`;
      const context = await browser.newContext(); const page = await context.newPage();
      const actor = { page, context, bearer: undefined, pending: new Set(), windowStart: Date.now(), windowRequests: 0 }; actors[persona] = actor;
      page.on('request', request => {
        actor.pending.add(request);
        if (request.url().startsWith(manifest.ApiUrl)) {
          if (Date.now() - actor.windowStart >= 60000) { actor.windowStart = Date.now(); actor.windowRequests = 0; }
          actor.windowRequests++;
        }
      });
      page.on('requestfinished', request => actor.pending.delete(request));
      page.on('requestfailed', request => actor.pending.delete(request));
      page.on('response', async response => {
        if (/\/api\/Auth\/(login|refresh)$/i.test(response.url()) && response.status() === 200) {
          try { actor.bearer = (await response.json()).token; } catch { /* Closing a context invalidates pending reads. */ }
        }
      });
      page.setDefaultTimeout(10000);
      if (mounted && persona === 'reader') actor.initialList = await captureOriginal(page, '**/api/importreceipts');
      await page.goto(manifest.FrontendUrl + (outbound && persona === 'reader' ? '/e2e/mounted-exports.html#/login' : mounted && persona === 'reader' ? '/e2e/mounted-receipts.html#/login' : '/login'));
      assert.equal(await page.title(), 'Quản lý kho ERP');
      assert.equal(await page.getByLabel('Tên đăng nhập').getAttribute('autocomplete'), 'username');
      assert.equal(await page.getByLabel('Mật khẩu').getAttribute('autocomplete'), 'current-password');
      await page.getByLabel('Tên đăng nhập').fill(`qa_${persona}_browser`);
      await page.getByLabel('Mật khẩu').fill(password);
      const login = page.waitForResponse(r => /\/api\/Auth\/login$/i.test(r.url()));
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      const response = await login; assert.equal(response.status(), 200, 'First persona login must succeed; never retry a blocked identity');
      evidence.logins.push({ persona, status: response.status(), count: 1, assertions: ['Vietnamese labels/title', 'username/current-password autocomplete'] });
      console.log(`Normal UI login: ${persona}; HTTP ${response.status()}; one attempt.`);
      actor.bearer = (await response.json()).token;
      await page.waitForURL(url => !url.pathname.endsWith('/login') && !url.hash.endsWith('/login'));
    }
    password = undefined;
    const admin = actors.admin, manager = actors.manager, viewer = actors.viewer, reader = actors.reader;
    if (outbound) await runOutboundCases({ run, actors, fixture, outboundFixture, sql, grant, fetchFromBrowser, expectStatus,
      manifest, helper, manifestPath, captureOriginal, quiesce });
    await run('Conditional QC partial/final/approval/replay', 'UI workflow + browser HTTP + SQL postconditions', async () => {
      const qc = await sql(`
        SET NOCOUNT ON; BEGIN TRAN;
        INSERT ImportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt) VALUES('QA-QC-PARTIAL',@warehouse,7,@admin,SYSUTCDATETIME());
        DECLARE @partial int=SCOPE_IDENTITY();
        INSERT ImportReceiptDetails(ImportReceiptId,ProductId,Quantity,ExpectedQuantity,ReceivedQuantity,AcceptedQuantity,BaseExpectedQuantity,BaseReceivedQuantity,BaseAcceptedQuantity,UnitPrice,OperationUnitId,OperationUnitCodeSnapshot,OperationUnitDecimalPlaces,BaseUnitId,BaseUnitCodeSnapshot,BaseUnitDecimalPlaces,ConversionFactor,ConversionVersion,RequiresQc,QcState)
          SELECT TOP 2 @partial,p.Id,5,5,5,0,5,5,0,0,u.Id,u.Code,u.DecimalPlaces,u.Id,u.Code,u.DecimalPlaces,1,1,1,1 FROM Products p JOIN Units u ON u.Id=p.UnitId ORDER BY p.Id;
        INSERT ImportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt) VALUES('QA-QC-APPROVAL',@warehouse,8,@admin,SYSUTCDATETIME());
        DECLARE @approval int=SCOPE_IDENTITY();
        INSERT ImportReceiptDetails(ImportReceiptId,ProductId,Quantity,ExpectedQuantity,ReceivedQuantity,AcceptedQuantity,BaseExpectedQuantity,BaseReceivedQuantity,BaseAcceptedQuantity,UnitPrice,OperationUnitId,OperationUnitCodeSnapshot,OperationUnitDecimalPlaces,BaseUnitId,BaseUnitCodeSnapshot,BaseUnitDecimalPlaces,ConversionFactor,ConversionVersion,RequiresQc,QcState)
          SELECT TOP 1 @approval,p.Id,5,5,5,5,5,5,5,0,u.Id,u.Code,u.DecimalPlaces,u.Id,u.Code,u.DecimalPlaces,1,1,1,2 FROM Products p JOIN Units u ON u.Id=p.UnitId ORDER BY p.Id;
        COMMIT; SELECT @partial AS partial,@approval AS approval FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { warehouse: fixture.warehouse, admin: fixture.admin });
      const state = () => sql(`SELECT
        (SELECT Status FROM ImportReceipts WHERE Id=@receipt) AS receiptState,
        (SELECT COUNT(*) FROM ImportReceiptDetails WHERE ImportReceiptId=@receipt AND QcState=2) AS completedLines,
        (SELECT COUNT(*) FROM AuditLogs WHERE EntityId=@receipt AND Action='ImportReceipt.QcDispositionRecorded' AND Result='Success') AS qcAudits,
        (SELECT COUNT(*) FROM AuditLogs WHERE EntityId=@approval AND Action='ImportReceipt.Approved' AND Result='Success') AS approvalAudits,
        (SELECT COUNT(*) FROM IdempotencyRecords WHERE CommandScope IN ('ImportReceipt.QcDisposition','ImportReceipt.Approve')) AS commandClaims,
        (SELECT SUM(Quantity) FROM InventoryStocks) AS stock,
        (SELECT COUNT(*) FROM InventoryTransactions) AS ledger FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { receipt: qc.partial, approval: qc.approval });
      const before = await state();
      expectStatus(await grant(fixture.managerRole, 'quality_inspection.complete', false), 200);
      await manager.page.goto(manifest.FrontendUrl + '/import-receipts');
      const partialRow = manager.page.getByRole('row').filter({ hasText: 'QA-QC-PARTIAL' });
      await partialRow.getByRole('button', { name: 'Chi tiết', exact: true }).click();
      const detail = await fetchFromBrowser(manager, `/api/importreceipts/${qc.partial}`); expectStatus(detail, 200);
      assert.equal(detail.data.details.length, 2);
      const [first, second] = detail.data.details;
      const body = lines => ({ lines: lines.map(line => ({ lineId: line.id, acceptedQuantity: 5, damagedQuantity: 0, rejectedQuantity: 0, reasonCode: '', note: '' })) });
      await manager.page.getByLabel(`Kiểm tra chất lượng: chấp nhận ${first.productCode}`, { exact: true }).fill('5');
      await manager.page.getByLabel(`Ghi kết quả kiểm tra ${second.productCode}`, { exact: true }).uncheck();
      const response = manager.page.waitForResponse(r => r.url().endsWith(`/${qc.partial}/qc-disposition`) && r.request().method() === 'POST');
      await manager.page.getByRole('button', { name: 'Ghi nhận kết quả kiểm tra chất lượng', exact: true }).click();
      const partialResponse = await response; assert.equal(partialResponse.status(), 200);
      const partialKey = partialResponse.request().headers()['idempotency-key']; assert(partialKey);
      await quiesce(manager);
      let effects = await state(); assert.equal(effects.receiptState, 7); assert.equal(effects.completedLines, 1); assert.equal(effects.qcAudits, 1);
      await manager.page.getByLabel(`Kiểm tra chất lượng: chấp nhận ${second.productCode}`, { exact: true }).fill('5');
      let uiRequests = 0;
      const observe = request => { if (request.method() === 'POST' && request.url().endsWith(`/${qc.partial}/qc-disposition`)) uiRequests++; };
      manager.page.on('request', observe);
      await manager.page.getByRole('button', { name: 'Ghi nhận kết quả kiểm tra chất lượng', exact: true }).click();
      await manager.page.getByText('Bạn không có quyền hoàn tất kiểm tra chất lượng. Hãy ghi kết quả từng phần hoặc liên hệ người có quyền.', { exact: true }).waitFor();
      assert.equal(uiRequests, 0, 'UI does not submit an unauthorized completing command');
      manager.page.off('request', observe);
      expectStatus(await fetchFromBrowser(manager, `/api/importreceipts/${qc.partial}/qc-disposition`, 'POST', body([second])), 403);
      assert.deepEqual(await state(), effects, 'Denied final QC leaves no line/state/audit/claim/stock/ledger effect');
      expectStatus(await grant(fixture.managerRole, 'quality_inspection.complete', true), 200);
      await manager.page.reload();
      await manager.page.getByRole('row').filter({ hasText: 'QA-QC-PARTIAL' }).getByRole('button', { name: 'Chi tiết', exact: true }).click();
      await manager.page.getByLabel(`Kiểm tra chất lượng: chấp nhận ${second.productCode}`, { exact: true }).fill('5');
      const finalResponsePromise = manager.page.waitForResponse(r => r.url().endsWith(`/${qc.partial}/qc-disposition`) && r.request().method() === 'POST');
      await manager.page.getByRole('button', { name: 'Ghi nhận kết quả kiểm tra chất lượng', exact: true }).click();
      const finalResponse = await finalResponsePromise; assert.equal(finalResponse.status(), 200);
      const finalKey = finalResponse.request().headers()['idempotency-key']; assert(finalKey);
      const finalBody = finalResponse.request().postDataJSON();
      effects = await state(); assert.equal(effects.receiptState, 8); assert.equal(effects.completedLines, 2); assert.equal(effects.qcAudits, 2);
      expectStatus(await fetchFromBrowser(manager, `/api/importreceipts/${qc.partial}/qc-disposition`, 'POST', finalBody, finalKey), 200);
      expectStatus(await fetchFromBrowser(manager, `/api/importreceipts/${qc.partial}/qc-disposition`, 'POST', body([first]), finalKey), 409);
      expectStatus(await grant(fixture.managerRole, 'quality_inspection.complete', false), 200);
      const denied = await fetchFromBrowser(manager, `/api/importreceipts/${qc.partial}/qc-disposition`, 'POST', finalBody, finalKey); expectStatus(denied, 403);
      assert.equal(denied.data.message, 'Bạn không có quyền thực hiện thao tác này.');
      expectStatus(await fetchFromBrowser(manager, `/api/importreceipts/${qc.partial}/qc-disposition`, 'POST', partialResponse.request().postDataJSON(), partialKey), 200);
      assert.deepEqual(await state(), effects, 'Original partial replay stays partial after later completion; no duplicate effect');
      expectStatus(await grant(fixture.managerRole, 'quality_disposition.approve', false), 200);
      await manager.page.goto(manifest.FrontendUrl + '/approvals');
      const approvalRow = manager.page.getByRole('row').filter({ hasText: 'QA-QC-APPROVAL' });
      await approvalRow.waitFor(); assert.equal(await approvalRow.getByTitle('Duyệt', { exact: true }).count(), 0);
      const queue = await fetchFromBrowser(manager, '/api/approvals/queue?documentType=ImportReceipt'); expectStatus(queue, 200);
      const entry = queue.data.items.find(item => item.documentId === qc.approval); assert(entry); assert.equal(entry.pendingState, 'QcCompleted'); assert.equal(entry.canApprove, false);
      const approvalPath = `/api/importreceipts/${qc.approval}/approve`, approvalKey = key();
      expectStatus(await fetchFromBrowser(manager, approvalPath, 'POST', undefined, approvalKey), 403);
      expectStatus(await grant(fixture.managerRole, 'quality_disposition.approve', true), 200);
      expectStatus(await grant(fixture.managerRole, 'quality_inspection.execute', false), 200);
      await manager.page.reload();
      await manager.page.getByRole('row').filter({ hasText: 'QA-QC-APPROVAL' }).getByTitle('Duyệt', { exact: true }).waitFor();
      expectStatus(await fetchFromBrowser(manager, approvalPath, 'POST', undefined, approvalKey), 200);
      expectStatus(await fetchFromBrowser(manager, approvalPath, 'POST', undefined, approvalKey), 200);
      expectStatus(await grant(fixture.managerRole, 'quality_disposition.approve', false), 200);
      expectStatus(await fetchFromBrowser(manager, approvalPath, 'POST', undefined, approvalKey), 403);
      effects = await state(); assert.equal(effects.qcAudits, 2); assert.equal(effects.approvalAudits, 1); assert.equal(effects.commandClaims, before.commandClaims + 3);
      assert.equal(effects.stock, before.stock); assert.equal(effects.ledger, before.ledger);
      expectStatus(await grant(fixture.managerRole, 'quality_inspection.execute', true), 200);
      expectStatus(await grant(fixture.managerRole, 'quality_inspection.complete', true), 200);
      expectStatus(await grant(fixture.managerRole, 'quality_disposition.approve', true), 200);
      return { actor: 'Manager same authenticated session; Admin grant/revoke from browser context', statuses: [200, 403, 200, 200, 409, 403, 200, 403, 200, 200, 403], finalDeniedUiRequests: uiRequests, baseline: before, postconditions: effects, assertions: ['execute-only partial UI succeeds', 'final UI safely blocked without complete; raw HTTP also 403 with zero effects', 'regrant enables final QC on reload', 'original partial/final replay rights remain distinct', 'QC approval needs disposition.approve; execute not required', 'Approval Center state-aware action visibility', 'two QC audits, one approval audit, three additional claims; inventory/ledger unchanged'] };
    });
    await run('Viewer raw list/detail/history/destinations and safe errors', 'Browser HTTP + SQL postconditions', async () => {
      let task = await fetchFromBrowser(manager, `/api/putaway-tasks/${fixture.task}`); expectStatus(task, 200);
      let response = await fetchFromBrowser(manager, `/api/putaway-tasks/${fixture.task}/assign`, 'POST', { rowVersion: task.data.rowVersion, assignedUserId: fixture.manager }); expectStatus(response, 200); task = response;
      response = await fetchFromBrowser(manager, `/api/putaway-tasks/${fixture.task}/start`, 'POST', { rowVersion: task.data.rowVersion }); expectStatus(response, 200); task = response;
      response = await fetchFromBrowser(manager, `/api/putaway-tasks/${fixture.task}/move`, 'POST', { rowVersion: task.data.rowVersion, itemId: task.data.items[0].id, destinationLocationId: fixture.storage, quantity: 1, unitCode: 'EA' }); expectStatus(response, 200); task = response;
      response = await fetchFromBrowser(manager, `/api/putaway-tasks/${fixture.task}/exception`, 'POST', { rowVersion: task.data.rowVersion, reason: 'QA_PRIVATE_EXCEPTION' }); expectStatus(response, 200);
      const before = await counts();
      const paths = ['/api/importreceipts', `/api/importreceipts/${fixture.posted}`, '/api/putaway-tasks', `/api/putaway-tasks/${fixture.task}`, `/api/putaway-tasks/${fixture.task}/items/${task.data.items[0].id}/destinations`, `/api/approvals/ImportReceipt/${fixture.pending}/history`, `/api/approvals/ImportReceipt/${fixture.pending}`];
      for (const endpoint of paths) { const r = await fetchFromBrowser(viewer, endpoint); expectStatus(r, 200); assertFiltered(r.data); }
      const detail = await fetchFromBrowser(viewer, `/api/putaway-tasks/${fixture.task}`);
      assert.equal(detail.data.exceptionReason, null); assert.equal(detail.data.movements.length, 1);
      const bad = await fetchFromBrowser(viewer, '/api/permissions'); expectStatus(bad, 403);
      assert.equal(bad.data.message, 'Bạn không có quyền thực hiện thao tác này.');
      const missing = await fetchFromBrowser(viewer, '/api/putaway-tasks/2147483647'); expectStatus(missing, 404);
      assert(missing.data.message.startsWith('Không tìm thấy'), 'Safe Vietnamese missing-resource response');
      const unauth = await fetchFromBrowser({ ...viewer, bearer: '' }, '/api/auth/me'); expectStatus(unauth, 401);
      const stale = await fetchFromBrowser(admin, `/api/users/${fixture.reader}/security/unlock`, 'POST', { rowVersion: 'AA==' }); expectStatus(stale, 409); assert.equal(stale.data.message, conflict);
      for (const r of [bad, missing, unauth, stale]) assert(!/SqlException|Microsoft\.EntityFramework|provider|stacktrace|ERP_KHO_BrowserQA/i.test(JSON.stringify(r.data)), 'Safe raw error payload');
      assert.deepEqual(await counts(), before, 'Read/denial requests have no durable effect');
      return { actor: 'Viewer / canonical read bundle', statuses: [200, 403, 404, 401, 409], assertions: ['mutation/secret properties absent recursively', 'receipt unitPrice null; no cost value', 'exceptionReason null; private marker absent', 'one movement history item', 'safe errors'], postconditions: before };
    });
    await run('Approval Center nonzero mixed count/pagination/read/reject/SoD/isolation', 'Browser HTTP + SQL postconditions', async () => {
      const m = await fetchFromBrowser(manager, '/api/approvals/queue?pageSize=5'); expectStatus(m, 200); assert.equal(m.data.totalRecords, 13);
      const r = await fetchFromBrowser(reader, '/api/approvals/queue?pageSize=5'); expectStatus(r, 200); assert.equal(r.data.totalRecords, 12); assert.equal(r.data.items.length, 5); assert(r.data.items.every(x => x.documentType === 'ImportReceipt' && x.warehouseId === fixture.warehouse));
      const second = await fetchFromBrowser(reader, '/api/approvals/queue?pageSize=5&pageIndex=2'); expectStatus(second, 200); assert(second.data.items.every(x => !r.data.items.some(y => y.documentId === x.documentId)));
      expectStatus(await fetchFromBrowser(reader, `/api/approvals/ImportReceipt/${fixture.foreign}`), 404);
      expectStatus(await fetchFromBrowser(reader, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }), 403);
      expectStatus(await fetchFromBrowser(admin, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }), 403);
      expectStatus(await grant(fixture.managerRole, 'approval.reject', false), 200);
      const deniedBefore = await counts();
      expectStatus(await fetchFromBrowser(manager, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }), 403);
      assert.deepEqual(await counts(), deniedBefore);
      expectStatus(await grant(fixture.managerRole, 'approval.reject', true), 200);
      expectStatus(await grant(fixture.readerRole, 'approval.reject', true), 200);
      expectStatus(await grant(fixture.readerRole, 'receipt.read', false), 200);
      const empty = await fetchFromBrowser(reader, '/api/approvals/queue?pageSize=5'); expectStatus(empty, 403);
      expectStatus(await fetchFromBrowser(reader, `/api/approvals/ImportReceipt/${fixture.pending}`), 403);
      expectStatus(await fetchFromBrowser(reader, `/api/approvals/ImportReceipt/${fixture.pending}/history`), 403);
      expectStatus(await grant(fixture.readerRole, 'receipt.read', true), 200);
      expectStatus(await grant(fixture.readerRole, 'approval.reject', false), 200);
      const idem = key(), before = await counts();
      const first = await fetchFromBrowser(manager, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }, idem); expectStatus(first, 200);
      expectStatus(await fetchFromBrowser(manager, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }, idem), 200);
      expectStatus(await fetchFromBrowser(manager, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Lý do khác' }, idem), 409);
      const after = await counts(); assert.equal(after.audits - before.audits, 1); assert.equal(after.claims - before.claims, 1); assert.equal(after.stock, before.stock); assert.equal(after.ledger, before.ledger);
      expectStatus(await grant(fixture.managerRole, 'approval.reject', false), 200);
      const revoked = await counts(); expectStatus(await fetchFromBrowser(manager, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }, idem), 403); assert.deepEqual(await counts(), revoked);
      expectStatus(await grant(fixture.managerRole, 'approval.reject', true), 200);
      const membership = await fetchFromBrowser(admin, `/api/users/${fixture.manager}/warehouse-access`); expectStatus(membership, 200);
      expectStatus(await fetchFromBrowser(admin, `/api/users/${fixture.manager}/warehouse-access/${fixture.warehouse}`, 'DELETE', { rowVersion: membership.data.rowVersion }), 204);
      const scoped = await counts(); expectStatus(await fetchFromBrowser(manager, `/api/approvals/ImportReceipt/${fixture.pending}/reject`, 'POST', { reason: 'Không phù hợp' }, idem), 404); assert.deepEqual(await counts(), scoped);
      const noMembership = await fetchFromBrowser(admin, `/api/users/${fixture.manager}/warehouse-access`);
      expectStatus(await fetchFromBrowser(admin, `/api/users/${fixture.manager}/warehouse-access`, 'POST', { warehouseId: fixture.warehouse, rowVersion: noMembership.data.rowVersion }), 204);
      return { actor: 'Manager / receipt.read + independent approval.reject; receipt-only reader; maker Admin', statuses: [200, 403, 404, 409, 204], counts: { managerQueue: 13, readerQueue: 12, pageSize: 5, rejectionAudit: 1, rejectionClaim: 1 }, assertions: ['filters precede count/pagination', 'maker blocked', 'revoked permission/membership replay denied with no new durable effects'] };
    });
    await run('Administration concurrency/last-admin and replay', 'Browser HTTP; SQL lock coordination/postconditions', async () => {
      const snapshot = await roleSnapshot(fixture.staffRole), before = await counts();
      const lock = helper(manifestPath, 'HoldLock'); await new Promise(resolve => lock.child.stdout.once('data', resolve));
      const idemA = key(), idemB = key();
      const a = grant(fixture.staffRole, 'warehouse.manage', true, admin, snapshot, idemA);
      const b = grant(fixture.staffRole, 'product_category.read', true, admin, snapshot, idemB);
      let settled = 0; a.then(() => settled++); b.then(() => settled++); await delay(150); assert.equal(settled, 0, 'Both HTTP requests overlap while SQL lock held');
      const results = await Promise.all([a, b]); await lock.completed; assert.deepEqual(results.map(x => x.status).sort(), [200, 409]);
      const winner = results[0].status === 200 ? 0 : 1; const code = winner === 0 ? 'warehouse.manage' : 'product_category.read'; const idem = winner === 0 ? idemA : idemB;
      expectStatus(await grant(fixture.staffRole, code, true, admin, snapshot, idem), 200);
      expectStatus(await grant(fixture.staffRole, code === 'warehouse.manage' ? 'product_category.read' : 'warehouse.manage', true, admin, snapshot, idem), 409);
      const after = await counts(); assert.equal(after.audits - before.audits, 1); assert.equal(after.claims - before.claims, 1);
      expectStatus(await grant(fixture.staffRole, code, false), 200);
      // A fully granted Manager is setup, not a browser grant workflow; it must never satisfy canonical Admin protection.
      await sql(`INSERT RolePermissions(RoleId,PermissionId,GrantedAt) SELECT @manager,p.Id,SYSUTCDATETIME() FROM Permissions p WHERE p.Code IN ('permission.read','permission.assign','role.read','role.manage','user.read','user.manage','user_warehouse.read','user_warehouse.manage') AND NOT EXISTS(SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=@manager AND rp.PermissionId=p.Id); UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE Id=@manager; UPDATE Users SET LockoutEnd=DATEADD(hour,1,SYSUTCDATETIME()),SecurityRevision=SecurityRevision+1 WHERE Username='qa_locked_admin'; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { manager: fixture.managerRole });
      const adminSnapshot = await roleSnapshot(fixture.adminRole), protectedBefore = await counts();
      const hold = helper(manifestPath, 'HoldLock'); await new Promise(resolve => hold.child.stdout.once('data', resolve));
      const last = await Promise.all([grant(fixture.adminRole, 'user.manage', false, admin, adminSnapshot), grant(fixture.adminRole, 'permission.assign', false, admin, adminSnapshot)]); await hold.completed;
      assert.deepEqual(last.map(x => x.status), [409, 409]); assert.deepEqual(await counts(), protectedBefore, 'Both unsafe revokes rejected without partial audit/claim');
      const valid = await sql(`SELECT COUNT(*) AS valid FROM Users u JOIN Roles r ON u.RoleId=r.Id WHERE u.IsActive=1 AND (u.LockoutEnd IS NULL OR u.LockoutEnd<=SYSUTCDATETIME()) AND LOWER(LTRIM(RTRIM(r.RoleName)))='admin' AND 8=(SELECT COUNT(*) FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId WHERE rp.RoleId=r.Id AND p.Code IN ('permission.read','permission.assign','role.read','role.manage','user.read','user.manage','user_warehouse.read','user_warehouse.manage')) FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`); assert.equal(valid.valid, 1);
      await sql(`UPDATE Users SET IsActive=0,LockoutEnd=NULL,SecurityRevision=SecurityRevision+1 WHERE Username='qa_locked_admin'; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`);
      expectStatus(await grant(fixture.adminRole, 'user.manage', false, admin, adminSnapshot), 409); assert.deepEqual(await counts(), protectedBefore);
      await sql(`UPDATE Users SET IsActive=1,LockoutEnd=DATEADD(hour,1,SYSUTCDATETIME()),SecurityRevision=SecurityRevision+1 WHERE Username='qa_locked_admin'; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`);
      await sql(`DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId WHERE rp.RoleId=@manager AND p.Code IN ('permission.read','permission.assign','role.read','role.manage','user.read','user.manage','user_warehouse.read','user_warehouse.manage'); UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE Id=@manager; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { manager: fixture.managerRole });
      return { actor: 'Admin / explicit administration grants', statuses: results.map(x => x.status), timing: results.map(x => ({ started: x.started, ended: x.ended })), overlapHeldMs: 150, effects: { audit: 1, claim: 1 }, lastAdminStatuses: [409, 409], inactiveAdminStatus: 409, canonicalAdministrators: 1, assertions: ['Manager with all eight grants excluded', 'locked/inactive canonical Admin excluded', 'unsafe concurrent revokes both fail closed'] };
    });
    await run('UI grant/revoke double-submit and next-request/reload/token renewal', 'UI workflow + browser HTTP + SQL postconditions', async () => {
      await admin.page.goto(manifest.FrontendUrl + '/permissions');
      const button = admin.page.getByRole('button', { name: 'Cấp Xem sản phẩm cho Vai trò khác', exact: true });
      await button.waitFor(); admin.page.on('dialog', dialog => dialog.accept());
      let requests = 0; const listener = request => { if (request.method() === 'POST' && request.url().endsWith(`/api/permissions/roles/${fixture.readerRole}/grants`)) requests++; };
      admin.page.on('request', listener);
      let release; const gate = new Promise(resolve => { release = resolve; }); let observed;
      const arrived = new Promise(resolve => { observed = resolve; });
      await admin.page.route(`**/api/permissions/roles/${fixture.readerRole}/grants`, async route => { observed(); await gate; await route.continue(); }, { times: 1 });
      const before = await counts();
      await button.dblclick(); await arrived;
      assert.equal(await admin.page.getByRole('button', { name: 'Cấp Xem sản phẩm cho Vai trò khác', exact: true }).isDisabled(), true);
      assert.equal(requests, 1); release();
      await admin.page.getByText('Đã cập nhật quyền truy cập.', { exact: true }).waitFor(); admin.page.off('request', listener);
      const after = await counts(); assert.equal(after.audits - before.audits, 1); assert.equal(after.claims - before.claims, 1);
      const identity = await fetchFromBrowser(reader, '/api/auth/me'); expectStatus(identity, 200); assert(identity.data.permissions.includes('product.read'));
      await reader.page.reload(); await reader.page.getByRole('link', { name: 'Sản phẩm', exact: true }).waitFor();
      let revokeRequests = 0;
      const revokeListener = request => { if (request.method() === 'DELETE' && request.url().endsWith(`/api/permissions/roles/${fixture.readerRole}/grants/product.read`)) revokeRequests++; };
      admin.page.on('request', revokeListener);
      let releaseRevoke, observedRevoke;
      const revokeGate = new Promise(resolve => { releaseRevoke = resolve; }), revokeArrived = new Promise(resolve => { observedRevoke = resolve; });
      await admin.page.route(`**/api/permissions/roles/${fixture.readerRole}/grants/product.read`, async route => { observedRevoke(); await revokeGate; await route.continue(); }, { times: 1 });
      const revokeBefore = await counts();
      const revokeButton = admin.page.getByRole('button', { name: 'Thu hồi Xem sản phẩm cho Vai trò khác', exact: true });
      await revokeButton.dblclick(); await revokeArrived; assert.equal(await revokeButton.isDisabled(), true); assert.equal(revokeRequests, 1);
      releaseRevoke(); await admin.page.getByRole('button', { name: 'Cấp Xem sản phẩm cho Vai trò khác', exact: true }).waitFor(); admin.page.off('request', revokeListener);
      const revokeAfter = await counts(); assert.equal(revokeAfter.audits - revokeBefore.audits, 1); assert.equal(revokeAfter.claims - revokeBefore.claims, 1);
      expectStatus(await fetchFromBrowser(reader, '/api/products'), 403);
      expectStatus(await grant(fixture.readerRole, 'product.read', true), 200);
      const oldBearer = reader.bearer;
      const response = await reader.page.evaluate(async api => { const r = await fetch(api + '/api/Auth/refresh', { method: 'POST', credentials: 'include' }); return { status: r.status, data: await r.json() }; }, manifest.ApiUrl); expectStatus(response, 200); reader.bearer = response.data.token; assert(reader.bearer !== oldBearer, 'Refresh rotates access credentials');
      expectStatus(await grant(fixture.readerRole, 'product.read', false), 200);
      expectStatus(await fetchFromBrowser(reader, '/api/products'), 403);
      await reader.page.reload(); await reader.page.waitForTimeout(300); assert.equal(await reader.page.getByRole('link', { name: 'Sản phẩm', exact: true }).count(), 0);
      return { actor: 'Admin grant/revoke buttons -> receipt-only reader same session', statuses: [200, 200, 403], grantRequests: requests, revokeRequests, grantAudit: 1, grantClaim: 1, revokeAudit: 1, revokeClaim: 1, assertions: ['loading disabled synchronously', 'grant/revoke next-request', 'reload menus', 'refresh cookie rotation'] };
    });
    await run('Stale Admin JWT does not restore database grants/global warehouse scope', 'Browser HTTP; SQL role fixture only', async () => {
      await sql(`UPDATE Users SET RoleId=@role,SecurityRevision=SecurityRevision+1 WHERE Id=@actor; UPDATE Users SET LockoutEnd=NULL,SecurityRevision=SecurityRevision+1 WHERE Username='qa_locked_admin'; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { role: fixture.managerRole, actor: fixture.admin });
      try {
        expectStatus(await fetchFromBrowser(admin, '/api/permissions'), 403);
        const warehouses = await fetchFromBrowser(admin, '/api/warehouses'); expectStatus(warehouses, 200); assert.equal(warehouses.data.length, 0);
        expectStatus(await fetchFromBrowser(admin, `/api/importreceipts/${fixture.foreign}`), 404);
      } finally { await sql(`UPDATE Users SET RoleId=@role,SecurityRevision=SecurityRevision+1 WHERE Id=@actor; UPDATE Users SET LockoutEnd=DATEADD(hour,1,SYSUTCDATETIME()),SecurityRevision=SecurityRevision+1 WHERE Username='qa_locked_admin'; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { role: fixture.adminRole, actor: fixture.admin }); }
      return { actor: 'Original Admin JWT / database Manager without membership', statuses: [403, 200, 404], scopedWarehouses: 0, assertions: ['role claim cannot recover grants or global warehouse access'] };
    });
    await run('Membership aggregate concurrency/empty set/replay and revoked capability', 'Browser HTTP; SQL lock coordination/postconditions; no membership UI', async () => {
      const endpoint = `/api/users/${fixture.reader}/warehouse-access`;
      const initial = await fetchFromBrowser(admin, endpoint); expectStatus(initial, 200);
      const before = await counts(), idemA = key(), idemB = key();
      const hold = helper(manifestPath, 'HoldLock'); await new Promise(resolve => hold.child.stdout.once('data', resolve));
      const a = fetchFromBrowser(admin, endpoint, 'POST', { warehouseId: fixture.foreignWarehouse, rowVersion: initial.data.rowVersion }, idemA);
      const b = fetchFromBrowser(admin, `${endpoint}/${fixture.warehouse}`, 'DELETE', { rowVersion: initial.data.rowVersion }, idemB);
      let settled = 0; a.then(() => settled++); b.then(() => settled++); await delay(150); assert.equal(settled, 0);
      const responses = await Promise.all([a, b]); await hold.completed; assert.deepEqual(responses.map(r => r.status).sort(), [204, 409]);
      const firstWon = responses[0].status === 204;
      expectStatus(await fetchFromBrowser(admin, firstWon ? endpoint : `${endpoint}/${fixture.warehouse}`, firstWon ? 'POST' : 'DELETE', firstWon ? { warehouseId: fixture.foreignWarehouse, rowVersion: initial.data.rowVersion } : { rowVersion: initial.data.rowVersion }, firstWon ? idemA : idemB), 204);
      expectStatus(await fetchFromBrowser(admin, firstWon ? endpoint : `${endpoint}/${fixture.foreignWarehouse}`, firstWon ? 'POST' : 'DELETE', firstWon ? { warehouseId: fixture.warehouse, rowVersion: initial.data.rowVersion } : { rowVersion: initial.data.rowVersion }, firstWon ? idemA : idemB), 409);
      const after = await counts(); assert.equal(after.audits - before.audits, 1); assert.equal(after.claims - before.claims, 1);
      expectStatus(await fetchFromBrowser(admin, endpoint, 'POST', { warehouseId: fixture.foreignWarehouse, rowVersion: initial.data.rowVersion }), 409);
      let current = await fetchFromBrowser(admin, endpoint);
      for (const membership of current.data.memberships) {
        expectStatus(await fetchFromBrowser(admin, `${endpoint}/${membership.warehouseId}`, 'DELETE', { rowVersion: current.data.rowVersion }), 204);
        current = await fetchFromBrowser(admin, endpoint);
      }
      assert.equal(current.data.memberships.length, 0); assert.equal(Buffer.from(current.data.rowVersion, 'base64').length, 8);
      expectStatus(await fetchFromBrowser(admin, endpoint, 'POST', { warehouseId: fixture.warehouse, rowVersion: current.data.rowVersion }), 204);
      expectStatus(await grant(fixture.managerRole, 'user_warehouse.read', true), 200);
      expectStatus(await grant(fixture.managerRole, 'user_warehouse.manage', true), 200);
      current = await fetchFromBrowser(manager, endpoint); expectStatus(current, 200);
      const replayKey = key(), command = { rowVersion: current.data.rowVersion };
      expectStatus(await fetchFromBrowser(manager, `${endpoint}/${fixture.warehouse}`, 'DELETE', command, replayKey), 204);
      expectStatus(await grant(fixture.managerRole, 'user_warehouse.manage', false), 200);
      const revoked = await counts(); expectStatus(await fetchFromBrowser(manager, `${endpoint}/${fixture.warehouse}`, 'DELETE', command, replayKey), 403); assert.deepEqual(await counts(), revoked);
      current = await fetchFromBrowser(admin, endpoint);
      expectStatus(await fetchFromBrowser(admin, endpoint, 'POST', { warehouseId: fixture.warehouse, rowVersion: current.data.rowVersion }), 204);
      expectStatus(await grant(fixture.managerRole, 'user_warehouse.read', false), 200);
      return { actor: 'Admin then scoped Manager with explicit membership grants', statuses: responses.map(r => r.status), timing: responses.map(r => ({ started: r.started, ended: r.ended })), effects: { concurrentAudit: 1, concurrentClaim: 1 }, assertions: ['same aggregate token one winner', 'stale token 409', 'empty-set eight-byte token', 'replay exactly once; mismatch 409', 'revoked capability replay 403 without durable effect'], membershipUi: 'DEFERRED_BY_OWNER' };
    });
    await run('Granular Category/Barcode/Warehouse/Location capabilities', 'Browser HTTP + UI observation; grants via browser administration HTTP', async () => {
      const products = await fetchFromBrowser(admin, '/api/products'); expectStatus(products, 200); const product = products.data.find(p => p.code === 'BARCODE-TARGET'); assert(product);
      expectStatus(await grant(fixture.readerRole, 'product.read', true), 200);
      expectStatus(await grant(fixture.readerRole, 'product.update', true), 200);
      const before = await counts();
      expectStatus(await fetchFromBrowser(reader, '/api/product-categories', 'POST', { code: 'QA-CATEGORY', name: 'Danh mục kiểm thử' }), 403);
      expectStatus(await fetchFromBrowser(reader, `/api/products/${product.id}/barcodes`, 'POST', { value: 'QA-NEW-BARCODE' }), 403);
      expectStatus(await fetchFromBrowser(reader, '/api/product-categories'), 403); assert.deepEqual(await counts(), before);
      const barcodes = await fetchFromBrowser(reader, `/api/products/${product.id}/barcodes`); expectStatus(barcodes, 200);
      assert(barcodes.data.length > 0); expectStatus(await fetchFromBrowser(reader, `/api/product-barcodes/lookup?value=${encodeURIComponent(barcodes.data[0].value)}`), 200);
      await reader.page.goto(manifest.FrontendUrl + '/products'); await reader.page.getByRole('button', { name: 'Sửa', exact: true }).first().click();
      assert.equal(await reader.page.getByLabel('Danh mục', { exact: true }).isDisabled(), true);
      assert.equal(await reader.page.getByRole('button', { name: 'Thêm mã vạch', exact: true }).count(), 0);
      assert.equal(await reader.page.getByRole('button', { name: 'Thêm danh mục', exact: true }).count(), 0);
      expectStatus(await grant(fixture.readerRole, 'product.update', false), 200);
      expectStatus(await grant(fixture.readerRole, 'product_barcode.manage', true), 200);
      expectStatus(await fetchFromBrowser(reader, `/api/products/${product.id}`, 'PUT', { name: 'Không được sửa', unitId: product.unitId, isActive: true }), 403);
      const barcode = await fetchFromBrowser(reader, `/api/products/${product.id}/barcodes`, 'POST', { value: 'QA-NEW-BARCODE' }); expectStatus(barcode, 201);
      expectStatus(await fetchFromBrowser(reader, `/api/products/${product.id}/barcodes/${barcode.data.id}`, 'DELETE'), 204);
      expectStatus(await grant(fixture.readerRole, 'product_barcode.manage', false), 200);
      expectStatus(await grant(fixture.readerRole, 'warehouse.read', true), 200);
      expectStatus(await grant(fixture.readerRole, 'location.read', true), 200);
      expectStatus(await grant(fixture.readerRole, 'location.manage', true), 200);
      const scoped = await fetchFromBrowser(reader, '/api/warehouses'); expectStatus(scoped, 200); assert.equal(scoped.data.length, 1); assert.equal(scoped.data[0].id, fixture.warehouse);
      expectStatus(await fetchFromBrowser(reader, `/api/warehouses/${fixture.foreignWarehouse}`), 404);
      expectStatus(await fetchFromBrowser(reader, `/api/warehouses/${fixture.warehouse}`, 'PUT', { name: 'Không được sửa', isActive: true }), 403);
      await reader.page.goto(manifest.FrontendUrl + '/warehouses'); await reader.page.getByRole('heading', { name: /kho/i }).first().waitFor();
      assert.equal(await reader.page.getByRole('button', { name: 'Sửa', exact: true }).count(), 0);
      expectStatus(await grant(fixture.readerRole, 'location.manage', false), 200);
      expectStatus(await grant(fixture.readerRole, 'warehouse.manage', true), 200);
      expectStatus(await fetchFromBrowser(reader, '/api/putaway-tasks/locations', 'POST', { warehouseId: fixture.warehouse, code: 'QA-NO-ALIAS', name: 'Không được tạo', locationType: 'STORAGE' }), 403);
      expectStatus(await fetchFromBrowser(reader, `/api/putaway-tasks/locations?warehouseId=${fixture.warehouse}`), 200);
      for (const code of ['product.read', 'warehouse.read', 'warehouse.manage', 'location.read']) expectStatus(await grant(fixture.readerRole, code, false), 200);
      return { actor: 'Receipt-only role with independently added/removed capabilities', assertions: ['product.update not Category/Barcode manage', 'Barcode manage not Product update', 'product.read exact barcode lookup', 'Category read independent', 'Location manage not Warehouse manage', 'Warehouse manage not Location manage', 'one scoped warehouse; foreign 404', 'UI category/barcode/warehouse controls guarded independently'], statuses: [200, 201, 204, 403, 404] };
    });
    await run('Vietnamese nonempty history/title/accessibility and safe route denial', 'UI workflow; normal authenticated browser session', async () => {
      await manager.page.goto(manifest.FrontendUrl + '/approvals');
      const trigger = manager.page.getByRole('row').filter({ hasText: 'QA-PENDING-02' }).getByRole('button', { name: 'Xem chi tiết', exact: true }); await trigger.click();
      const dialog = manager.page.getByRole('dialog'); await dialog.getByRole('heading', { name: 'Lịch sử phê duyệt', exact: true }).waitFor();
      assert.match(await dialog.innerText(), /Nháp → Đã nhận hàng/);
      assert(!/ImportReceipt|\.Received|RowVersion|AVAILABLE|InProgress/.test(await dialog.innerText()));
      const close = dialog.getByRole('button', { name: 'Đóng', exact: true }); assert.equal(await close.evaluate(button => button === document.activeElement), true);
      await close.click(); assert.equal(await trigger.evaluate(button => button === document.activeElement), true);
      assert.equal(await manager.page.title(), 'Quản lý kho ERP');
      await manager.page.goto(manifest.FrontendUrl + '/permissions');
      await manager.page.getByText('Bạn không có quyền thực hiện thao tác này.', { exact: true }).waitFor();
      assert.equal(await manager.page.getByRole('link', { name: 'Quản trị quyền truy cập', exact: true }).count(), 0);
      return { actor: 'Manager / no permission administration', assertions: ['nonempty Vietnamese history/state mapping', 'dialog close/return focus', 'Vietnamese document title', 'safe direct-route denial; no administration menu'] };
    });
    await run('Operational warehouse/assigned operator/maker rules remain independent', 'Browser HTTP; database role fixture only; SQL postconditions', async () => {
      const before = await counts();
      const successBefore = await sql(`SELECT COUNT(*) AS count FROM AuditLogs WHERE Result='Success' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`);
      expectStatus(await fetchFromBrowser(admin, `/api/importreceipts/${manifest.RetryReceiptId}/post`, 'POST'), 403);
      expectStatus(await fetchFromBrowser(admin, `/api/importreceipts/${fixture.checkerPending}/approve`, 'POST'), 403);
      const denied = await counts(); for (const field of ['claims','stock','movements','ledger']) assert.equal(denied[field], before[field]);
      assert.deepEqual(await sql(`SELECT COUNT(*) AS count FROM AuditLogs WHERE Result='Success' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`), successBefore);
      expectStatus(await fetchFromBrowser(manager, `/api/importreceipts/${manifest.RetryReceiptId}/post`, 'POST'), 200);
      const tasks = await fetchFromBrowser(manager, '/api/putaway-tasks'); expectStatus(tasks, 200);
      let task = tasks.data.find(t => t.receiptId === manifest.RetryReceiptId); assert(task);
      const detail = await fetchFromBrowser(manager, `/api/putaway-tasks/${task.id}`); expectStatus(detail, 200); task = detail.data;
      task = (await fetchFromBrowser(manager, `/api/putaway-tasks/${task.id}/assign`, 'POST', { assignedUserId: fixture.manager, rowVersion: task.rowVersion })).data;
      await sql(`UPDATE Users SET RoleId=@role,SecurityRevision=SecurityRevision+1 WHERE Id=@user; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { role: fixture.staffRole, user: fixture.reader });
      try {
        const identity = await fetchFromBrowser(reader, '/api/auth/me'); expectStatus(identity, 200);
        assert(identity.data.permissions.includes('putaway.execute')); assert(!identity.data.permissions.includes('putaway.assign'));
        expectStatus(await fetchFromBrowser(reader, `/api/putaway-tasks/${task.id}/start`, 'POST', { rowVersion: task.rowVersion }), 403);
        expectStatus(await fetchFromBrowser(reader, `/api/putaway-tasks/${task.id}/assign`, 'POST', { rowVersion: task.rowVersion, assignedUserId: fixture.reader }), 403);
        const assigned = await fetchFromBrowser(manager, `/api/putaway-tasks/${task.id}/assign`, 'POST', { assignedUserId: fixture.reader, rowVersion: task.rowVersion }); expectStatus(assigned, 200); task = assigned.data;
        const idem = key(), command = { rowVersion: task.rowVersion };
        expectStatus(await fetchFromBrowser(reader, `/api/putaway-tasks/${task.id}/start`, 'POST', command, idem), 200);
        let access = await fetchFromBrowser(admin, `/api/users/${fixture.reader}/warehouse-access`);
        expectStatus(await fetchFromBrowser(admin, `/api/users/${fixture.reader}/warehouse-access/${fixture.warehouse}`, 'DELETE', { rowVersion: access.data.rowVersion }), 204);
        const revoked = await counts(); expectStatus(await fetchFromBrowser(reader, `/api/putaway-tasks/${task.id}/start`, 'POST', command, idem), 404); assert.deepEqual(await counts(), revoked);
        access = await fetchFromBrowser(admin, `/api/users/${fixture.reader}/warehouse-access`);
        expectStatus(await fetchFromBrowser(admin, `/api/users/${fixture.reader}/warehouse-access`, 'POST', { warehouseId: fixture.warehouse, rowVersion: access.data.rowVersion }), 204);
        const replayBefore = await counts(); expectStatus(await fetchFromBrowser(reader, `/api/putaway-tasks/${task.id}/start`, 'POST', command, idem), 200); assert.deepEqual(await counts(), replayBefore);
      } finally { await sql(`UPDATE Users SET RoleId=@role,SecurityRevision=SecurityRevision+1 WHERE Id=@user; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { role: fixture.readerRole, user: fixture.reader }); }
      return { actor: 'Maker Admin; Manager; same reader session with database WarehouseStaff fixture', statuses: [403, 200, 404, 204], postconditions: await counts(), assertions: ['explicit grants do not bypass maker/checker/poster', 'execute grant does not bypass assignment', 'missing assign grant denied', 'membership-revoked start replay 404', 'legitimate terminal replay 200 without new effect'], setup: 'role fixture is SQL setup; operations are browser HTTP' };
    });
    await run('Receipt-only reader and unmounted stale auth/list/detail/print responses', 'UI workflow; runner-only original response delay', async () => {
      const delays = [];
      await reader.page.goto(manifest.FrontendUrl + '/import-receipts');
      await reader.page.getByRole('button', { name: 'Chi tiết', exact: true }).first().waitFor();
      const optional = []; const observe = request => { if (/discrepanc|reason|business-partners/.test(request.url())) optional.push(new URL(request.url()).pathname); };
      reader.page.on('request', observe);
      await reader.page.getByRole('button', { name: 'Chi tiết', exact: true }).first().click();
      await reader.page.waitForTimeout(300); assert.equal(optional.length, 0); reader.page.off('request', observe);
      await reader.page.goto(manifest.FrontendUrl + '/'); await quiesce(reader);
      const authResponse = await captureOriginal(reader.page, '**/api/auth/me');
      await reader.page.getByRole('link', { name: 'Phiếu nhập kho', exact: true }).click(); await authResponse.arrived;
      expectStatus(await grant(fixture.readerRole, 'receipt.read', false), 200);
      await reader.page.getByRole('link', { name: 'Tổng quan', exact: true }).click(); await reader.page.waitForTimeout(200);
      await quiesce(reader, authResponse.request());
      assert.equal(await reader.page.getByRole('link', { name: 'Phiếu nhập kho', exact: true }).count(), 0);
      expectStatus(await grant(fixture.readerRole, 'receipt.read', true), 200);
      authResponse.release(); await authResponse.done; await reader.page.waitForTimeout(200);
      delays.push(authResponse.observation);
      assert.equal(await reader.page.getByRole('link', { name: 'Phiếu nhập kho', exact: true }).count(), 0, 'Older identity cannot restore revoked UI');
      await reader.page.reload(); await quiesce(reader);
      for (const mode of ['list', 'detail', 'print']) {
        if (mode !== 'list') { await reader.page.goto(manifest.FrontendUrl + '/import-receipts'); await reader.page.getByRole('button', { name: 'Chi tiết', exact: true }).first().waitFor(); }
        const held = await captureOriginal(reader.page, mode === 'list' ? '**/api/importreceipts' : /\/api\/importreceipts\/\d+$/);
        if (mode === 'list') await reader.page.getByRole('link', { name: 'Phiếu nhập kho', exact: true }).click();
        else await reader.page.getByRole('button', { name: mode === 'detail' ? 'Chi tiết' : 'Xem bản in', exact: true }).first().click();
        await held.arrived;
        expectStatus(await grant(fixture.readerRole, 'receipt.read', false), 200);
        await reader.page.getByRole('link', { name: 'Tổng quan', exact: true }).click(); await reader.page.waitForTimeout(200);
        await quiesce(reader, held.request());
        expectStatus(await grant(fixture.readerRole, 'receipt.read', true), 200);
        held.release(); await held.done; await reader.page.waitForTimeout(200);
        delays.push(held.observation);
        assert(!/QA-(PENDING|SEED|RETRY|CONCURRENT|UI)-/.test(await reader.page.locator('body').innerText()), 'Late receipt cannot restore sensitive state after unmount/revocation');
        assert.equal(await reader.page.getByRole('dialog').count(), 0, 'Late detail/print cannot reopen dialog');
        await reader.page.reload(); await quiesce(reader);
      }
      return { actor: 'Receipt-only reader / no optional dependency grants', delayedCases: ['auth/me', 'list', 'detail', 'print'], delays, optionalRequests: 0, postconditions: await counts(), assertions: ['original authorized response held in memory', 'revoke then regrant', 'no stale menu/data/dialog restoration after navigation/unmount'] };
    });
    if (mounted) await run('Mounted receipt list/detail/print late responses', 'Component-in-browser; real application 403 refresh; SQL fixture/postconditions', async () => {
      const delays = [];
      const heading = await reader.page.getByRole('heading', { name: 'Quản Lý Nhập Kho', exact: true }).elementHandle();
      assert(heading, 'Receipt component is mounted');
      const refresh = async expected => {
        const identity = reader.page.waitForResponse(r => /\/api\/auth\/me$/i.test(r.url()) && r.status() === 200);
        await reader.page.getByRole('button', { name: 'Xác minh quyền truy cập', exact: true }).click();
        await identity;
        await reader.page.getByRole('status').filter({ hasText: expected }).waitFor();
      };
      const connected = () => heading.evaluate(node => node.isConnected);
      for (const mode of ['list', 'detail', 'print']) {
        let held = mode === 'list' ? reader.initialList : await captureOriginal(reader.page, /\/api\/importreceipts\/\d+$/);
        try {
          if (mode !== 'list') await reader.page.getByRole('button', { name: mode === 'detail' ? 'Chi tiết' : 'Xem bản in', exact: true }).first().click();
          await held.arrived;
          const originalJson = JSON.stringify(await held.original().json());
          const previous = mode === 'list' ? /QA-(PENDING|SEED|RETRY|CONCURRENT|UI)-/ : new RegExp(`QA-FRESH-${mode === 'detail' ? 'list' : 'detail'}-`);
          assert(previous.test(originalJson), 'Held authorized response actually contains old fixture identifiers');
          const before = await counts();
          // Change only QA display identifiers so a late old list cannot masquerade as the newer authorized list.
          await sql(`UPDATE ImportReceipts SET Code=CONCAT('QA-FRESH-',@mode,'-',Id); SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { mode });
          expectStatus(await grant(fixture.readerRole, 'receipt.read', false), 200);
          await refresh('Quyền đọc phiếu đã bị thu hồi');
          await quiesce(reader, held.request());
          assert(await connected(), 'Same original heading stays connected after revocation; no component unmount');
          assert.equal(await reader.page.getByRole('dialog').count(), 0);
          assert(!/QA-(PENDING|SEED|RETRY|CONCURRENT|UI|FRESH)-/.test(await reader.page.locator('body').innerText()), 'Revocation clears existing receipt data');
          expectStatus(await grant(fixture.readerRole, 'receipt.read', true), 200);
          await refresh('Có quyền đọc phiếu');
          await quiesce(reader, held.request());
          await reader.page.getByText(`QA-FRESH-${mode}-`, { exact: false }).first().waitFor();
          assert(await connected(), 'Same component stays mounted through regrant');
          held.release(); await held.done; await quiesce(reader);
          assert(await connected(), 'Same component is mounted after original response release');
          assert.equal(await reader.page.getByRole('dialog').count(), 0, 'Late detail/print cannot reopen either dialog');
          const text = await reader.page.locator('body').innerText();
          assert(text.includes(`QA-FRESH-${mode}-`), 'Current authorized list remains');
          assert(!/QA-(PENDING|SEED|RETRY|CONCURRENT|UI)-/.test(text));
          if (mode !== 'list') assert(!text.includes(`QA-FRESH-${mode === 'detail' ? 'list' : 'detail'}-`), 'Held older response cannot overwrite current display identifiers');
          const after = await counts();
          assert.equal(after.stock, before.stock); assert.equal(after.ledger, before.ledger); assert.equal(after.movements, before.movements);
          assert.equal(after.audits - before.audits, 2); assert.equal(after.claims - before.claims, 2);
          delays.push({ mode, ...held.observation, mountedChecks: 3, originalHeadingConnected: true, originalHadPreviousIdentifier: true, deniedCatalogStatus: 403, refreshedIdentityStatus: 200, grantStatuses: [200, 200], auditDelta: 2, claimDelta: 2 });
        } finally { await held.cleanup(); }
      }
      await heading.dispose();
      return { actor: 'Receipt-only reader, same UI login; Admin mutations from browser context', delayedCases: ['list', 'detail', 'print'], delays, assertions: ['unchanged production receipt component stays mounted', 'real 403 interceptor refreshes database permissions', 'revocation clears data/dialog', 'regrant fresh response wins; old release cannot restore data/dialog'], postconditions: await counts() };
    });
    if (selected?.test('Capture failure cleanup probe')) await run('Capture failure cleanup probe', 'Intentional negative browser case; caller must FAIL', async () => {
      const held = await captureOriginal(reader.page, '**/api/permissions');
      try {
        const request = fetchFromBrowser(reader, '/api/permissions'); request.catch(() => {});
        await held.arrived; await request; // Actual HTTP 403 must reject arrived, never report success.
        held.release(); await held.done;
      } finally { await held.cleanup(); }
    });
    assert(evidence.cases.length > 0, 'At least one selected browser case must run');
    evidence.status = evidence.cases.every(x => x.status === 'PASS') ? 'PASS_FOR_IMPLEMENTED_CASES' : 'FAILED'; await save();
  } catch (error) {
    evidence.status = 'FAILED'; evidence.cases.push({ name: currentCase, status: 'FAIL', assertionLocation: String(error.stack).split('\n').find(x => /(?:permission|outbound)-browser\.mjs:/.test(x))?.match(/(?:permission|outbound)-browser\.mjs:\d+:\d+/)?.[0], diagnosticHash: createHash('sha256').update(String(error.message)).digest('hex').slice(0, 12) });
    await save(); console.error(`FAIL: ${currentCase}; ${String(error.message).match(/stage=[\w-]+; sqlNumber=\d+; kind=\w+; line=\d+/)?.[0] || 'inspect the runnable assertion locally'} (no sensitive response logged).`); process.exitCode = 1;
  } finally {
    password = undefined;
    let closed = true;
    for (const actor of Object.values(actors)) actor.bearer = undefined;
    for (const resource of [...Object.values(actors).map(actor => actor.context), browser, server].filter(Boolean)) {
      let timer;
      try { await Promise.race([resource.close(), new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Browser cleanup deadline exceeded')), 5000); })]); }
      catch { closed = false; process.exitCode = 1; }
      finally { clearTimeout(timer); }
    }
    if (!closed) { evidence.status = 'FAILED'; evidence.cleanupFailed = true; await save(); }
    const remainingProfiles = [];
    for (const profile of manifest.BrowserProfiles) if (await access(profile).then(() => true, () => false)) remainingProfiles.push(profile);
    manifest.BrowserRunner = { ...(manifest.BrowserRunner || {}), Closed: closed, RemainingProfiles: remainingProfiles };
    await writeFile(manifestPath, JSON.stringify(manifest, null, 2));
  }
}

export async function captureOriginal(page, pattern, { arrivalMs = 10000, deadlineMs = 30000, cleanupMs = 1000 } = {}) {
  const arrival = Promise.withResolvers(), completion = Promise.withResolvers(), gate = Promise.withResolvers();
  // Preserve the rejected promises for callers, without an unhandled rejection if a UI action fails first.
  arrival.promise.catch(() => {}); completion.promise.catch(() => {});
  let route, request, original, failed = false, finished = false, cleaning;
  const observation = {};
  const release = () => { observation.releasedAt ??= new Date().toISOString(); gate.resolve(); };
  const fail = error => {
    failed = true; clearTimeout(arrivalTimer); clearTimeout(deadline);
    arrival.reject(error); completion.reject(error); gate.resolve();
  };
  const bounded = operation => {
    let timer;
    return Promise.race([Promise.resolve().then(operation), new Promise(resolve => { timer = setTimeout(() => resolve(false), cleanupMs); })])
      .then(() => {}, () => {}).finally(() => clearTimeout(timer));
  };
  const cleanup = () => cleaning ??= Promise.all([
    bounded(() => page.unroute(pattern, handler)),
    ...(route && !finished ? [bounded(() => route.abort())] : []),
  ]);
  const arrivalTimer = setTimeout(() => { fail(new Error('Expected original response was not observed')); void cleanup(); }, arrivalMs);
  const deadline = setTimeout(() => { fail(new Error('Original response capture deadline exceeded')); void cleanup(); }, deadlineMs);
  const handler = async current => {
    route = current;
    try {
      request = route.request(); observation.path = new URL(request.url()).pathname; observation.startedAt = new Date().toISOString();
      const response = original = await route.fetch({ timeout: arrivalMs });
      if (failed) return;
      observation.status = response.status(); observation.receivedAt = new Date().toISOString();
      assert.equal(response.status(), 200, 'Only delay an original authorized success response');
      clearTimeout(arrivalTimer); arrival.resolve();
      await gate.promise;
      if (failed) return;
      await route.fulfill({ response });
      if (failed) return;
      finished = true; clearTimeout(deadline); completion.resolve();
    } catch (error) { fail(error); }
    finally { await cleanup(); }
  };
  try { await Promise.race([page.route(pattern, handler, { times: 1 }), completion.promise]); }
  catch (error) { fail(error); await cleanup(); throw error; }
  return { arrived: arrival.promise, release, done: completion.promise, request: () => request, original: () => original, observation,
    cleanup: () => { if (!finished && !failed) fail(new Error('Original response capture cancelled')); return cleanup(); } };
}

async function quiesce(actor, held) {
  // Do not mistake a legitimate newer 403-triggered identity refresh for the held old response.
  for (let attempt = 0; attempt < 100; attempt++) {
    if ([...actor.pending].every(request => request === held)) { await delay(100); if ([...actor.pending].every(request => request === held)) return; }
    await delay(100);
  }
  const error = new Error('Unrelated requests must settle before regrant/releasing the held original response');
  error.pendingPaths = [...actor.pending].map(request => ({ path: new URL(request.url()).pathname, held: request === held }));
  throw error;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const manifestPath = process.argv[process.argv.indexOf('--manifest') + 1];
  assert(process.argv.includes('--manifest') && manifestPath, 'Use --manifest for an existing exact owned Browser QA run');
  await main(path.resolve(manifestPath));
}
