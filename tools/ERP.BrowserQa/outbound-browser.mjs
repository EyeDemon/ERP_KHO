import assert from 'node:assert/strict';
import { randomUUID, createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

// Reuses the official runner's authenticated pages, credential/SQL helper, evidence redaction and cleanup.
export async function runOutboundCases({ run, actors, fixture: f, outboundFixture: out, sql, grant,
  fetchFromBrowser: request, expectStatus: status, manifest, helper, manifestPath, captureOriginal, quiesce }) {
  const { admin, manager, viewer, reader } = actors;
  const sourceHashes = {};
  for (const file of ['ERP.Application/Services/ExportReceiptService.cs', 'ERP.Api/Infrastructure/IdempotentCommandFilter.cs',
    'ERP.Infrastructure/Repositories/InventoryStockRepository.cs', 'frontend/src/pages/ExportReceipts.tsx',
    'tools/ERP.BrowserQa/outbound-browser.mjs', 'tools/ERP.BrowserQa/outbound-fixtures.sql'])
    sourceHashes[file] = createHash('sha256').update(await readFile(new URL('../../' + file, import.meta.url))).digest('hex');
  const state = () => sql(`SELECT
    (SELECT SUM(Quantity) FROM InventoryStocks WHERE ProductId=@product) AS onHand,
    (SELECT SUM(ReservedQuantity) FROM InventoryStocks WHERE ProductId=@product) AS reserved,
    (SELECT COUNT(*) FROM InventoryTransactions WHERE ProductId=@product) AS ledger,
    (SELECT SUM(Quantity) FROM InventoryTransactions WHERE ProductId=@product) AS exported,
    (SELECT COUNT(*) FROM AuditLogs WHERE EntityName='ExportReceipt') AS audits,
    (SELECT COUNT(*) FROM IdempotencyRecords WHERE CommandScope LIKE 'ExportReceipt.%') AS claims
    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { product: out.product });
  const read = async (id, actor = manager) => { const r = await request(actor, `/api/exportreceipts/${id}`); status(r, 200); return r.data; };
  const create = async (quantity, code = 'OUT-' + randomUUID().slice(0, 8)) => {
    const r = await request(admin, '/api/exportreceipts', 'POST', { code, warehouseId: out.warehouse,
      note: 'QA_PRIVATE_OUT', details: [{ productId: out.product, quantity, unitPrice: 7, note: 'QA_PRIVATE_OUT' }] });
    status(r, 201); return r.data.id;
  };
  const command = (id, action, actor, version, key = randomUUID()) => request(actor,
    `/api/exportreceipts/${id}/${action}`, 'POST', { rowVersion: version }, key);
  let dispatched;
  await run('Outbound UI reserve dispatch', 'UI workflow + SQL postconditions', async () => {
    await admin.page.goto(manifest.FrontendUrl + '/export-receipts');
    await admin.page.getByLabel('Mã phiếu', { exact: true }).fill('OUT-UI');
    await admin.page.getByLabel('Kho', { exact: true }).selectOption(String(out.warehouse));
    await admin.page.getByRole('button', { name: '+ Thêm dòng', exact: true }).click();
    await admin.page.getByLabel('Sản phẩm dòng 1').selectOption(String(out.product));
    await admin.page.getByLabel('Số lượng dòng 1').fill('25');
    await admin.page.getByLabel('Đơn giá dòng 1').fill('7');
    const created = admin.page.waitForResponse(r => r.url().endsWith('/api/exportreceipts') && r.request().method() === 'POST');
    await admin.page.getByRole('button', { name: 'Tạo Phiếu Xuất', exact: true }).click();
    const result = await created; assert.equal(result.status(), 201); dispatched = (await result.json()).id;
    await manager.page.goto(manifest.FrontendUrl + '/export-receipts');
    const row = manager.page.getByRole('row').filter({ hasText: 'OUT-UI' });
    let requests = 0;
    const observe = r => { if (r.url().endsWith(`/${dispatched}/approve-and-reserve`) && r.method() === 'POST') requests++; };
    manager.page.on('request', observe);
    manager.page.once('dialog', dialog => dialog.accept());
    const held = await captureOriginal(manager.page, `**/api/exportreceipts/${dispatched}/approve-and-reserve`);
    try {
      await row.getByRole('button', { name: 'Duyệt và giữ hàng', exact: true }).dblclick();
      await held.arrived;
      assert(await row.getByRole('button', { name: 'Duyệt và giữ hàng', exact: true }).isDisabled());
      assert.equal(requests, 1);
      assert.equal((await state()).onHand, 100); assert.equal((await state()).reserved, 25); assert.equal((await state()).ledger, 0);
      held.release(); await held.done; await quiesce(manager);
    } finally { await held.cleanup(); manager.page.off('request', observe); }
    await row.getByText('Đã duyệt và giữ hàng', { exact: true }).waitFor();
    assert.equal(await row.getByRole('button', { name: 'Xác nhận xuất kho' }).count(), 0, 'Checker cannot dispatch their own approval');
    await reader.page.reload();
    const dispatchRow = reader.page.getByRole('row').filter({ hasText: 'OUT-UI' });
    reader.page.once('dialog', dialog => dialog.accept());
    const moved = reader.page.waitForResponse(r => r.url().endsWith(`/${dispatched}/dispatch`) && r.request().method() === 'POST');
    await dispatchRow.getByRole('button', { name: 'Xác nhận xuất kho', exact: true }).click();
    assert.equal((await moved).status(), 200); await dispatchRow.getByText('Đã xuất kho', { exact: true }).waitFor();
    const after = await state(); assert.equal(after.onHand, 75); assert.equal(after.reserved, 0); assert.equal(after.exported, 25);
    const ledger = await sql(`SELECT COUNT(*) AS rows,COUNT(LocationId) AS locations FROM InventoryTransactions WHERE ReferenceType='ExportReceipt' AND ReferenceId=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { id: dispatched });
    assert.equal(ledger.rows, ledger.locations);
    return { sourceHashes, actor: 'Admin maker, Manager checker, scoped reader dispatcher', statuses: [201, 200, 200], reserveRequestCount: requests, postconditions: after, assertions: ['Vietnamese labels/states', 'reserve leaves physical stock/ledger unchanged', 'double-click one request', 'checker differs from dispatcher', 'location-traceable ledger'] };
  });
  await run('Outbound replay filtering permissions and scope', 'Browser HTTP + SQL postconditions', async () => {
    const id = await create(10), original = (await read(id)).rowVersion, key = randomUUID();
    status(await command(id, 'approve-and-reserve', admin, original), 403);
    status(await command(id, 'approve-and-reserve', reader, original), 403);
    status(await command(id, 'approve-and-reserve', manager, original, key), 200);
    const held = await state(); status(await command(id, 'approve-and-reserve', manager, original, key), 200);
    assert.deepEqual(await state(), held);
    const mismatch = await command(id, 'approve-and-reserve', manager, 'changed', key); status(mismatch, 409);
    assert(JSON.stringify(mismatch.data).includes('Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.'));
    status(await grant(f.managerRole, 'export_receipt.approve', false), 200);
    status(await command(id, 'approve-and-reserve', manager, original, key), 403); assert.deepEqual(await state(), held);
    status(await grant(f.managerRole, 'export_receipt.approve', true), 200);
    const version = (await read(id)).rowVersion;
    status(await command(id, 'cancel', manager, version), 200);
    const target = await create(5); const fresh = (await read(target)).rowVersion;
    const dispatchKey = randomUUID(); status(await command(target, 'approve-and-dispatch', manager, fresh, dispatchKey), 200);
    let membership = await request(admin, `/api/users/${f.manager}/warehouse-access`); status(membership, 200);
    status(await request(admin, `/api/users/${f.manager}/warehouse-access/${f.warehouse}`, 'DELETE', { rowVersion: membership.data.rowVersion }), 204);
    const revoked = await state(); status(await command(target, 'approve-and-dispatch', manager, fresh, dispatchKey), 404); assert.deepEqual(await state(), revoked);
    membership = await request(admin, `/api/users/${f.manager}/warehouse-access`);
    status(await request(admin, `/api/users/${f.manager}/warehouse-access`, 'POST', { warehouseId: f.warehouse, rowVersion: membership.data.rowVersion }), 204);
    for (const path of ['/api/exportreceipts', `/api/exportreceipts/${target}`, `/api/approvals/ExportReceipt/${target}/history`]) {
      const r = await request(viewer, path); status(r, 200);
      const text = JSON.stringify(r.data);
      assert(!/"(?:unitPrice|rowVersion|cost|value|fingerprint|idempotencyKey|stackTrace|exceptionType)"\s*:/.test(text));
      assert(!/QA_PRIVATE_OUT/.test(text));
    }
    const denied = await request(reader, `/api/exportreceipts/${target}/cancel`, 'POST', { rowVersion: fresh }); status(denied, 403);
    const notFound = await request(viewer, '/api/exportreceipts/2147483647'); status(notFound, 404);
    assert(JSON.stringify(denied.data).includes('Bạn không có quyền thực hiện thao tác này.'));
    assert(JSON.stringify(notFound.data).includes('Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.'));
    for (const response of [denied, notFound, mismatch]) assert(!/SqlException|DbUpdateException|Microsoft\.EntityFrameworkCore|stackTrace|provider|database|QA_PRIVATE_OUT|OUT-UI/i.test(JSON.stringify(response.data)));
    const unauthorized = await viewer.page.evaluate(async url => (await fetch(url)).status, manifest.ApiUrl + '/api/exportreceipts'); assert.equal(unauthorized, 401);
    return { actor: 'same authenticated pages, actual grant/membership API mutations', statuses: [200, 401, 403, 404, 409, 204], postconditions: await state(), assertions: ['replay exactly once', 'fingerprint conflict', 'permission/membership revoke prevents cached success', 'Viewer raw properties/private content absent', 'maker/approve capability independent', 'cancel releases without physical effect'] };
  });
  await run('Outbound concurrent reserve and dispatch cancel', 'Browser HTTP + SQL coordination/postconditions', async () => {
    const available = (await state()).onHand, quantity = available - 1;
    const a = await create(quantity), b = await create(quantity);
    const va = (await read(a)).rowVersion, vb = (await read(b)).rowVersion;
    async function race(mode, target, commands) {
      const lock = helper(manifestPath, mode, { id: target });
      await Promise.race([new Promise(resolve => lock.child.stdout.once('data', text => { assert(text.includes('LOCK_READY')); resolve(); })), lock.completed.then(() => { throw new Error('Lock not observed'); })]);
      let completed = 0;
      const startedAt = new Date().toISOString();
      const pending = commands.map(command => command().then(r => { completed++; return r; }));
      await new Promise(resolve => setTimeout(resolve, 250)); assert.equal(completed, 0, 'Both requests pending while fixture owns lock');
      await lock.completed;
      const responses = await Promise.all(pending); assert.deepEqual(responses.map(r => r.status).sort(), [200, 409]);
      return { responses, startedAt, finishedAt: new Date().toISOString(), controlledPendingMs: 250 };
    }
    const reserve = await race('HoldExportLocations', f.warehouse, [() => command(a, 'approve-and-reserve', manager, va), () => command(b, 'approve-and-reserve', manager, vb)]);
    assert.equal((await state()).reserved, quantity);
    const winner = reserve.responses[0].status === 200 ? a : b, version = (await read(winner)).rowVersion;
    const final = await race('HoldExportReceipt', winner, [() => command(winner, 'dispatch', reader, version), () => command(winner, 'cancel', manager, version)]);
    const after = await state(); assert.equal(after.reserved, 0); assert([available, available - quantity].includes(after.onHand));
    const duplicate = await create(0.0001), draft = (await read(duplicate)).rowVersion;
    status(await command(duplicate, 'approve-and-reserve', manager, draft), 200);
    const approved = (await read(duplicate)).rowVersion, beforeDuplicate = await state();
    const dispatch = await race('HoldExportReceipt', duplicate, [() => command(duplicate, 'dispatch', reader, approved), () => command(duplicate, 'dispatch', reader, approved)]);
    const duplicateAfter = await state();
    assert.equal(duplicateAfter.audits - beforeDuplicate.audits, 1);
    assert.equal(duplicateAfter.claims - beforeDuplicate.claims, 1);
    assert(Math.abs(beforeDuplicate.onHand - duplicateAfter.onHand - 0.0001) < 1e-8);
    return { actor: 'Manager reserves/cancels, independent reader dispatches', reserveTiming: { startedAt: reserve.startedAt, finishedAt: reserve.finishedAt, pendingMs: 250 }, finalTiming: { startedAt: final.startedAt, finishedAt: final.finishedAt, pendingMs: 250 }, dispatchTiming: { startedAt: dispatch.startedAt, finishedAt: dispatch.finishedAt, pendingMs: 250 }, statuses: reserve.responses.concat(final.responses, dispatch.responses).map(r => r.status), postconditions: after, assertions: ['real overlapping requests', 'no oversell', 'dispatch/cancel only one effect', 'concurrent dispatch one effect/audit/claim'] };
  });
  await run('Outbound Approval Center independent grants nonzero count and isolation', 'Browser HTTP + SQL fixture/postconditions', async () => {
    const id = await create(0.0001);
    const before = await state();
    const queue = await request(reader, '/api/approvals/queue?pageSize=1'); status(queue, 200);
    const exportCount = await sql(`SELECT COUNT(*) AS count FROM ExportReceipts WHERE WarehouseId=@warehouse AND Status=0 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { warehouse: f.warehouse });
    assert(exportCount.count > 0); assert.equal(queue.data.items.length, 1);
    status(await grant(f.readerRole, 'export_receipt.read', false), 200);
    const filtered = await request(reader, '/api/approvals/queue?pageSize=1'); status(filtered, 200);
    assert.equal(queue.data.totalRecords - filtered.data.totalRecords, exportCount.count);
    assert(filtered.data.items.every(x => x.documentType !== 'ExportReceipt'));
    status(await request(reader, `/api/approvals/ExportReceipt/${id}`), 403);
    status(await request(reader, `/api/approvals/ExportReceipt/${id}/history`), 403);
    status(await grant(f.readerRole, 'export_receipt.read', true), 200);
    status(await grant(f.readerRole, 'export_receipt.cancel', true), 200);
    status(await request(reader, `/api/approvals/ExportReceipt/${id}/reject`, 'POST', { reason: 'Không phù hợp' }), 403);
    status(await grant(f.readerRole, 'export_receipt.cancel', false), 200);
    status(await request(admin, `/api/approvals/ExportReceipt/${id}/reject`, 'POST', { reason: 'Không phù hợp' }), 403);
    status(await grant(f.managerRole, 'export_receipt.cancel', false), 200);
    status(await request(manager, `/api/approvals/ExportReceipt/${id}/reject`, 'POST', { reason: 'Không phù hợp' }), 403);
    status(await grant(f.managerRole, 'export_receipt.cancel', true), 200);
    const foreign = await sql(`INSERT ExportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt) VALUES('OUT-FOREIGN',@warehouse,0,@actor,SYSUTCDATETIME()); SELECT CAST(SCOPE_IDENTITY() AS int) AS id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { warehouse: f.foreignWarehouse, actor: f.admin });
    status(await request(manager, `/api/approvals/ExportReceipt/${foreign.id}`), 404);
    const key = randomUUID();
    status(await request(manager, `/api/approvals/ExportReceipt/${id}/reject`, 'POST', { reason: 'Không phù hợp' }, key), 200);
    status(await request(manager, `/api/approvals/ExportReceipt/${id}/reject`, 'POST', { reason: 'Không phù hợp' }, key), 200);
    const audit = await sql(`SELECT COUNT(*) AS count FROM AuditLogs WHERE EntityName='ExportReceipt' AND EntityId=@id AND Action='ApprovalRejected' AND Result='Success' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { id });
    assert.equal(audit.count, 1);
    const after = await state(); assert.equal(after.onHand, before.onHand); assert.equal(after.ledger, before.ledger);
    return { actor: 'reader independent grants, maker Admin, scoped Manager', statuses: [200, 403, 404], counts: { beforeFilter: queue.data.totalRecords, afterFilter: filtered.data.totalRecords, excluded: exportCount.count, pageSize: 1, rejectionAudit: audit.count }, assertions: ['mixed count/page filters before pagination', 'cancel does not imply reject', 'reject requires cancel as well', 'maker denied', 'isolated foreign ID', 'successful replay exactly once; no physical effect'] };
  });
  await run('Outbound Base UOM snapshot and invalid quantity', 'Browser HTTP + SQL master-only fixture and postconditions', async () => {
    const id = await create(0.0001), version = (await read(id)).rowVersion;
    status(await command(id, 'approve-and-reserve', manager, version), 200);
    await sql(`UPDATE Units SET DecimalPlaces=0,Name=N'Đơn vị mới' WHERE Id=@id; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { id: out.unit });
    const detail = await read(id); assert.equal(detail.details[0].baseUomPrecisionSnapshot, 4); assert.equal(detail.details[0].baseUomNameSnapshot, 'Cái');
    const before = await state(); status(await command(id, 'dispatch', reader, detail.rowVersion), 200);
    const after = await state(); assert(Math.abs(before.onHand - after.onHand - 0.0001) < 1e-8);
    for (const quantity of [0, -1, 0.00001]) {
      const unchanged = await state();
      status(await request(admin, '/api/exportreceipts', 'POST', { code: 'BAD-' + randomUUID(), warehouseId: out.warehouse, details: [{ productId: out.product, quantity, unitPrice: 1 }] }), 400);
      assert.deepEqual(await state(), unchanged);
    }
    await sql(`UPDATE Units SET DecimalPlaces=4,Name=N'Cái' WHERE Id=@id; SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { id: out.unit });
    return { actor: 'Admin maker, Manager checker, reader dispatcher', statuses: [201, 200, 400], postedBaseQuantity: 0.0001, snapshotPrecision: 4, assertions: ['snapshot unaffected by current master', 'exact base quantity; no rounding', 'invalid commands zero stock/ledger/audit/claim effect'] };
  });
  await run('Outbound ineligible stock exclusion', 'Browser HTTP + SQL fixture/postconditions', async () => {
    const before = await state();
    const denied = await request(admin, '/api/exportreceipts', 'POST', { code: 'OUT-NO-PICK', warehouseId: out.warehouse, details: [{ productId: out.ineligible, quantity: 1, unitPrice: 0 }] });
    status(denied, 400); assert.deepEqual(await state(), before);
    const legacy = await sql(`INSERT ExportReceipts(Code,WarehouseId,Status,CreatedBy,CreatedAt) VALUES('OUT-LEGACY-INELIGIBLE',@warehouse,0,@actor,SYSUTCDATETIME());
      DECLARE @id int=SCOPE_IDENTITY(); INSERT ExportReceiptDetails(ExportReceiptId,ProductId,Quantity,UnitPrice) VALUES(@id,@product,1,0);
      SELECT @id AS id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { warehouse: out.warehouse, actor: f.admin, product: out.ineligible });
    status(await command(legacy.id, 'approve-and-reserve', manager, (await read(legacy.id)).rowVersion), 409);
    assert.deepEqual(await state(), before);
    const stock = await sql(`SELECT SUM(Quantity) AS onHand,SUM(ReservedQuantity) AS reserved,(SELECT COUNT(*) FROM InventoryTransactions WHERE ProductId=@product) AS ledger FROM InventoryStocks WHERE ProductId=@product FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { product: out.ineligible });
    assert.deepEqual(stock, { onHand: 600, reserved: 0, ledger: 0 });
    return { actor: 'Admin create, Manager reserve; QA-only legacy line setup', statuses: [400, 409], postconditions: stock, assertions: ['RECEIVING excluded even when pickable', 'damaged/rejected/inactive/blocked/non-pickable excluded', 'no success audit/claim/reservation or physical effect'] };
  });
  await run('Outbound mounted late list detail print', 'Component in browser + real identity refresh + SQL fixture-only display changes', async () => {
    await create(0.0001); // Give a focused successor a real authorized receipt response.
    await reader.page.reload(); await quiesce(reader); // Settle the normal in-memory-token refresh before capture.
    const refresh = async expected => {
      const identity = reader.page.waitForResponse(r => /\/api\/auth\/me$/i.test(r.url()));
      await reader.page.getByRole('button', { name: 'Xác minh quyền truy cập', exact: true }).click(); await identity;
      await reader.page.getByRole('status').filter({ hasText: expected }).waitFor();
    };
    const delays = [];
    for (const mode of ['list', 'detail', 'print']) {
      const held = await captureOriginal(reader.page, mode === 'list' ? '**/api/exportreceipts' : /\/api\/exportreceipts\/\d+$/);
      let heading;
      try {
        if (mode === 'list') {
          // A real grant change refreshes the mounted component without an unauthenticated reload request.
          status(await grant(f.readerRole, 'export_receipt.dispatch', false), 200);
          await refresh('Có quyền đọc phiếu xuất');
        } else await reader.page.getByRole('button', { name: mode === 'detail' ? 'Chi tiết' : 'Xem bản in', exact: true }).first().click();
        await held.arrived;
        heading = await reader.page.getByRole('heading', { name: 'Quản Lý Phiếu Xuất Kho', exact: true }).elementHandle();
        const oldData = await held.original().json(); const old = JSON.stringify(oldData); assert(old.includes('OUT-'));
        const oldCodes = (Array.isArray(oldData) ? oldData : [oldData]).map(receipt => receipt.code);
        await sql(`UPDATE ExportReceipts SET Code=CONCAT('OUT-FRESH-',@mode,'-',Id); SELECT 1 AS fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`, { mode });
        status(await grant(f.readerRole, 'export_receipt.read', false), 200); await refresh('Quyền đọc phiếu xuất đã bị thu hồi');
        await quiesce(reader, held.request()); assert(await heading.evaluate(node => node.isConnected));
        assert(!/OUT-/.test(await reader.page.locator('body').innerText()));
        status(await grant(f.readerRole, 'export_receipt.read', true), 200); await refresh('Có quyền đọc phiếu xuất');
        await quiesce(reader, held.request()); await reader.page.getByText(`OUT-FRESH-${mode}-`, { exact: false }).first().waitFor();
        held.release(); await held.done; await quiesce(reader);
        assert(await heading.evaluate(node => node.isConnected)); assert.equal(await reader.page.getByRole('dialog').count(), 0);
        assert.equal(await reader.page.getByRole('heading', { name: /^Chi Tiết Phiếu Xuất:/ }).count(), 0);
        const visible = await reader.page.locator('body').innerText();
        assert(visible.includes(`OUT-FRESH-${mode}-`));
        for (const code of oldCodes) assert(!visible.includes(code), 'Old authorized receipt identifier cannot return');
        delays.push({ mode, ...held.observation, mounted: true, originalHadOldData: true });
      } finally { await held.cleanup(); await heading?.dispose(); }
      if (mode === 'list') {
        status(await grant(f.readerRole, 'export_receipt.dispatch', true), 200);
        await refresh('Có quyền đọc phiếu xuất'); await quiesce(reader);
      }
    }
    return { actor: 'same reader page stays mounted through revoke/regrant/release, Admin grant API', delays, assertions: ['late responses cannot restore old data/detail/print', 'real 403 refresh; no fake permission event'] };
  });
}
