import assert from 'node:assert/strict';
import { randomUUID, createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

// Test-only cases reuse the official authenticated browser, owned SQL helper and redacted evidence.
export async function runManualReservationCases({run,actors,fixture:f,manualFixture:m,sql,grant,fetchFromBrowser:request,expectStatus:status,manifest,helper,manifestPath,captureOriginal,quiesce,assertFiltered}) {
  const {admin,manager,viewer,reader}=actors;
  const hashes={};
  for(const file of ['ERP.Infrastructure/Services/StockReservationService.cs','ERP.Api/Controllers/StockReservationsController.cs','ERP.Api/Infrastructure/IdempotentCommandFilter.cs','ERP.Infrastructure/Repositories/InventoryStockRepository.cs','frontend/src/pages/StockReservations.tsx','frontend/e2e/mounted-reservations.tsx','tools/ERP.BrowserQa/manual-reservation-fixtures.sql','tools/ERP.BrowserQa/manual-reservation-browser.mjs'])
    hashes[file]=createHash('sha256').update(await readFile(new URL('../../'+file,import.meta.url))).digest('hex');
  const state=()=>sql(`SELECT
    (SELECT SUM(Quantity) FROM InventoryStocks WHERE ProductId=@product) AS onHand,
    (SELECT SUM(ReservedQuantity) FROM InventoryStocks WHERE ProductId=@product) AS reserved,
    (SELECT COUNT(*) FROM InventoryTransactions WHERE ProductId=@product) AS ledger,
    (SELECT COUNT(*) FROM AuditLogs WHERE EntityName='StockReservation') AS audits,
    (SELECT COUNT(*) FROM IdempotencyRecords WHERE CommandScope LIKE 'StockReservation.%') AS claims
    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`,{product:m.product});
  const read=async(id,actor=manager)=>{const r=await request(actor,'/api/stock-reservations/'+id);status(r,200);return r.data;};
  const create=async(quantity,actor=manager,warehouse=m.warehouse,product=m.product)=>{const r=await request(actor,'/api/stock-reservations','POST',{warehouseId:warehouse,productId:product,quantity});status(r,201);return r.data;};
  const release=(id,actor,payload,key=randomUUID())=>request(actor,'/api/stock-reservations/'+id+'/release','POST',payload,key);
  const refreshReader=async(text,held)=>{await reader.page.getByRole('button',{name:'Xác minh quyền truy cập',exact:true}).click();await reader.page.getByText(text,{exact:true}).waitFor();await quiesce(reader,held);};
  // This fixture change creates a stale Admin JWT, not a mock JWT or a browser administration workflow.
  await sql('UPDATE Users SET RoleId=@role WHERE Id=@user; SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER',{role:f.readerRole,user:f.reader});
  await refreshReader('Có quyền đọc giữ hàng');
  let uiId;
  await run('Manual reservation UI create release','UI workflow + network count + SQL postconditions',async()=>{
    const before=await state();await admin.page.goto(manifest.FrontendUrl+'/stock-reservations');
    await admin.page.getByRole('button',{name:'Tạo giữ hàng',exact:true}).click();
    await admin.page.getByLabel('Kho',{exact:true}).selectOption(String(m.warehouse));await admin.page.getByLabel('Sản phẩm',{exact:true}).selectOption(String(m.product));await admin.page.getByLabel('Số lượng',{exact:true}).fill('20');
    const created=admin.page.waitForResponse(r=>r.url().endsWith('/api/stock-reservations')&&r.request().method()==='POST');
    await admin.page.getByRole('button',{name:'Xác nhận tạo',exact:true}).click();const response=await created;assert.equal(response.status(),201);const item=await response.json();uiId=item.id;
    assert.equal((await state()).reserved-before.reserved,20);assert.equal((await state()).onHand,before.onHand);assert.equal((await state()).ledger,before.ledger);
    await manager.page.goto(manifest.FrontendUrl+'/stock-reservations');const row=manager.page.getByRole('row').filter({hasText:item.reservationCode});
    await row.getByRole('button',{name:'Giải phóng '+item.reservationCode,exact:true}).click();await manager.page.getByLabel('Số lượng',{exact:true}).fill('5');await manager.page.getByLabel('Lý do',{exact:true}).fill('Không còn nhu cầu một phần');
    let requests=0;const observe=r=>{if(r.url().endsWith('/'+uiId+'/release')&&r.method()==='POST')requests++;};manager.page.on('request',observe);
    const held=await captureOriginal(manager.page,'**/api/stock-reservations/'+uiId+'/release');
    try {await manager.page.getByRole('button',{name:'Xác nhận giải phóng',exact:true}).dblclick();await held.arrived;assert(await manager.page.getByRole('button',{name:'Đang xử lý...',exact:true}).isDisabled());assert.equal(requests,1);held.release();await held.done;await quiesce(manager);}
    finally {await held.cleanup();manager.page.off('request',observe);}
    const after=await state();assert.equal(after.reserved-before.reserved,15);assert.equal(after.onHand,before.onHand);assert.equal(after.ledger,before.ledger);assert.equal(after.audits-before.audits,2);assert.equal(after.claims-before.claims,2);
    assert.equal(await manager.page.title(),'Giữ hàng — ERP KHO');
    return {hashes,actor:'Admin creates, Manager releases through UI',statuses:[201,200],requestCount:requests,postconditions:after,assertions:['reservation does not reduce physical stock or create ledger','partial release exact quantity','double-click one request/effect','Vietnamese form and loading']};
  });
  await run('Manual reservation Viewer stale JWT and safe errors','Browser-origin HTTP + UI observation + SQL postconditions',async()=>{
    const before=await state();const list=await request(viewer,'/api/stock-reservations');status(list,200);assertFiltered(list.data);assertFiltered(await read(uiId,viewer));
    status(await request(reader,'/api/stock-reservations','POST',{warehouseId:m.warehouse,productId:m.product,quantity:1}),403);
    status(await request(reader,'/api/stock-reservations/expire','POST',{}),403);
    status(await request({...viewer,bearer:'invalid'},'/api/stock-reservations'),401);
    status(await request(manager,'/api/stock-reservations/999999'),404);
    const malformed=await release(uiId,manager,{rowVersion:'broken',reason:'Kiểm thử'});status(malformed,409);assert(JSON.stringify(malformed.data).includes('Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.'));
    for(const value of [malformed.data])assert(!/SqlException|EntityFramework|stackTrace|database|deadlock/i.test(JSON.stringify(value)));
    assert.equal(await reader.page.getByRole('button',{name:'Tạo giữ hàng',exact:true}).count(),0);assert.equal(await viewer.page.goto(manifest.FrontendUrl+'/stock-reservations').then(()=>viewer.page.getByRole('button',{name:'Xử lý hết hạn',exact:true}).count()),0);
    assert.deepEqual(await state(),before);
    await sql('UPDATE Users SET RoleId=@role WHERE Id=@user; SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER',{role:f.staffRole,user:f.reader});
    await refreshReader('Có quyền đọc giữ hàng');await reader.page.getByRole('button',{name:'Tạo giữ hàng',exact:true}).waitFor();
    const staffItem=await create(0.0001,reader);
    status(await release(staffItem.id,reader,{reason:'Không được giải phóng'}),403);
    assert.equal(await reader.page.getByRole('button',{name:'Xử lý hết hạn',exact:true}).count(),0);
    await sql('UPDATE Users SET RoleId=@role WHERE Id=@user; SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER',{role:f.readerRole,user:f.reader});
    await refreshReader('Có quyền đọc giữ hàng');
    return {hashes,actor:'Viewer; old Admin JWT with database QAReader/Staff fixtures',statuses:[200,403,401,404,409,201,403],postconditions:await state(),assertions:['Viewer property filtering','stale Admin JWT does not restore capability','safe Vietnamese errors','denial no effect/audit/claim','Staff read/create without default release'],setup:'database role changes are SQL fixture setup, not browser administration'};
  });
  await run('Manual reservation replay revoked capability and membership','Browser-origin HTTP administration and replay + SQL postconditions',async()=>{
    const item=await create(10),key=randomUUID(),payload={quantity:2,reason:'Giải phóng một phần',rowVersion:item.rowVersion};
    status(await release(item.id,manager,payload,key),200);const before=await state();status(await release(item.id,manager,payload,key),200);assert.deepEqual(await state(),before);
    status(await release(item.id,manager,{...payload,quantity:3},key),409);status(await release(item.id,manager,payload),409);
    status(await grant(f.managerRole,'reservation.release',false),200);status(await release(item.id,manager,payload,key),403);assert.deepEqual(await state(),before);
    status(await grant(f.managerRole,'reservation.release',true),200);
    let access=await request(admin,'/api/users/'+f.manager+'/warehouse-access');status(access,200);
    status(await request(admin,'/api/users/'+f.manager+'/warehouse-access/'+m.warehouse,'DELETE',{rowVersion:access.data.rowVersion}),204);
    status(await release(item.id,manager,payload,key),404);assert.deepEqual(await state(),before);
    access=await request(admin,'/api/users/'+f.manager+'/warehouse-access');status(access,200);
    status(await request(admin,'/api/users/'+f.manager+'/warehouse-access','POST',{warehouseId:m.warehouse,rowVersion:access.data.rowVersion}),204);
    status(await release(item.id,manager,payload,key),200);assert.deepEqual(await state(),before);
    return {hashes,actor:'Manager same session; Admin grant/membership commands from browser',statuses:[200,200,409,403,404,200],postconditions:before,assertions:['successful terminal/partial replay exactly once','fingerprint mismatch and stale token','replay reauthorizes current permission and membership','no cached success on denial']};
  });
  await run('Manual reservation controlled reserve release concurrency','Browser-origin HTTP + owned SQL coordination/postconditions',async()=>{
    async function race(mode,id,operations,expected){const lock=helper(manifestPath,mode,{id});await Promise.race([new Promise(resolve=>lock.child.stdout.once('data',text=>{assert(text.includes('LOCK_READY'));resolve();})),lock.completed.then(()=>{throw Error('Lock not observed');})]);let completed=0;const startedAt=new Date().toISOString();const pending=operations.map(op=>op().then(r=>{completed++;return r;}));await new Promise(resolve=>setTimeout(resolve,250));assert.equal(completed,0);await lock.completed;const responses=await Promise.all(pending);assert.deepEqual(responses.map(r=>r.status).sort(),expected);return {responses,startedAt,finishedAt:new Date().toISOString(),controlledPendingMs:250};}
    const before=await state();const available=100-before.reserved,quantity=Math.floor(available)-1;assert(quantity>1);
    const payload={warehouseId:m.warehouse,productId:m.product,quantity};
    const reserved=await race('HoldExportLocations',m.warehouse,[()=>request(manager,'/api/stock-reservations','POST',payload),()=>request(manager,'/api/stock-reservations','POST',payload)],[201,409]);
    const item=reserved.responses.find(r=>r.status===201).data;const afterReserve=await state();assert.equal(afterReserve.reserved-before.reserved,quantity);assert.equal(afterReserve.audits-before.audits,1);assert.equal(afterReserve.claims-before.claims,1);
    const released=await race('HoldReservation',item.id,[()=>release(item.id,manager,{quantity:1,reason:'Giải phóng',rowVersion:item.rowVersion}),()=>release(item.id,manager,{quantity:1,reason:'Giải phóng',rowVersion:item.rowVersion})],[200,409]);
    const after=await state();assert.equal(afterReserve.reserved-after.reserved,1);assert.equal(after.audits-afterReserve.audits,1);assert.equal(after.claims-afterReserve.claims,1);assert.equal(after.onHand,before.onHand);assert.equal(after.ledger,before.ledger);
    const latest=await read(item.id);status(await release(item.id,manager,{reason:'Kết thúc fixture cạnh tranh',rowVersion:latest.rowVersion}),200);
    return {hashes,actor:'Manager, distinct keys and same aggregate token',statuses:reserved.responses.concat(released.responses).map(x=>x.status),reserveTiming:{startedAt:reserved.startedAt,finishedAt:reserved.finishedAt,pendingMs:250},releaseTiming:{startedAt:released.startedAt,finishedAt:released.finishedAt,pendingMs:250},postconditions:after,assertions:['real overlap','no oversell','one release effect/audit/claim','safe conflict and no physical/ledger change'],setup:'SQL only holds existing rows; requests originate in browser'};
  });
  await run('Manual reservation Base UOM precision and eligibility','Browser-origin HTTP + owned SQL fixture/postconditions',async()=>{
    const item=await create(2.5);assert.equal(item.baseUomCodeSnapshot,'MR-EA');assert.equal(item.baseUomPrecisionSnapshot,4);
    await sql("UPDATE Units SET Code='MR-CHANGED',Name=N'Đơn vị đã đổi',DecimalPlaces=0 WHERE Id=@unit; SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",{unit:m.unit});
    const before=await state();for(const quantity of [0,-1,0.5,1.00001])status(await request(manager,'/api/stock-reservations','POST',{productId:m.product,warehouseId:m.warehouse,quantity}),400);
    status(await request(manager,'/api/stock-reservations','POST',{productId:m.ineligible,warehouseId:m.warehouse,quantity:1}),409);assert.deepEqual(await state(),before);
    status(await release(item.id,manager,{quantity:0.125,reason:'Giải phóng theo đơn vị đã lưu',rowVersion:item.rowVersion}),200);const saved=await read(item.id);assert.equal(saved.baseUomCodeSnapshot,'MR-EA');assert.equal(saved.remainingQuantity,2.375);
    await sql("UPDATE Units SET Code='MR-EA',Name=N'Cái giữ hàng',DecimalPlaces=4 WHERE Id=@unit; SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",{unit:m.unit});
    return {hashes,actor:'Manager',statuses:[201,400,409,200],postconditions:await state(),assertions:['snapshot remains independent of current master','exact Base precision no rounding','receiving/nonavailable/blocked/inactive/nonpickable excluded','failed commands no audit/claim/effect']};
  });
  await run('Manual reservation ExportReceipt read-only and scoped expiry','Browser-origin HTTP + owned SQL fixture/postconditions',async()=>{
    const exported=await request(admin,'/api/exportreceipts','POST',{code:'MR-EXPORT',warehouseId:m.warehouse,details:[{productId:m.product,quantity:5,unitPrice:7}]});status(exported,201);
    status(await request(manager,'/api/exportreceipts/'+exported.data.id+'/approve-and-reserve','POST',{rowVersion:exported.data.rowVersion}),200);
    const ref=await sql("SELECT Id AS id FROM StockReservations WHERE SourceType='ExportReceipt' AND SourceId=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",{id:exported.data.id});
    const before=await state();const key=randomUUID();status(await release(ref.id,manager,{reason:'Không được xử lý độc lập',rowVersion:(await read(ref.id)).rowVersion},key),409);status(await release(ref.id,manager,{reason:'Không được xử lý độc lập'},key),409);assert.deepEqual(await state(),before);
    const own=await create(1),foreign=await create(1,admin,m.foreignWarehouse);
    await sql("UPDATE StockReservations SET CreatedAt=DATEADD(hour,-2,SYSUTCDATETIME()),ExpiresAt=DATEADD(hour,-1,SYSUTCDATETIME()) WHERE Id IN (@own,@foreign,@export); SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",{own:own.id,foreign:foreign.id,export:ref.id});
    status(await request(manager,'/api/stock-reservations/'+foreign.id),404);
    status(await request(reader,'/api/stock-reservations/'+foreign.id),404);
    const expiring=await state(),expiryKey=randomUUID();const expired=await request(manager,'/api/stock-reservations/expire','POST',{},expiryKey);status(expired,200);assert.equal(expired.data.expired,1);
    const after=await state();assert.equal(expiring.reserved-after.reserved,1);assert.equal(after.onHand,expiring.onHand);assert.equal(after.ledger,expiring.ledger);status(await request(manager,'/api/stock-reservations/expire','POST',{},expiryKey),200);assert.deepEqual(await state(),after);
    const untouched=await sql("SELECT COUNT(*) AS active FROM StockReservations WHERE Id IN (@foreign,@export) AND Status=0 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER",{foreign:foreign.id,export:ref.id});assert.equal(untouched.active,2);
    const reconciliation=await request(viewer,'/api/stock-reservations/reconciliation');status(reconciliation,200);assertFiltered(reconciliation.data);assert.deepEqual(await state(),after);
    return {hashes,actor:'Manager scoped expiry; Admin foreign fixture; Viewer reconciliation',statuses:[201,200,409,404,200],postconditions:after,assertions:['ExportReceipt holds cannot be independently released including replay','bulk expiry Manual own warehouse only','foreign/ExportReceipt remain active','reconciliation readonly'],setup:'expiry clock updates are SQL fixture setup, not browser expiry administration'};
  });
  await run('Manual reservation mounted late list detail and Vietnamese accessibility','Component-in-browser UI + runner response coordination + SQL fixture',async()=>{
    await reader.page.reload();await refreshReader('Có quyền đọc giữ hàng');const heading=reader.page.getByRole('heading',{name:'Giữ hàng',exact:true});const original=await heading.elementHandle();const timings=[];
    for(const mode of ['list','detail']){
      const item=await create(0.0001),pattern=mode==='list'?'**/api/stock-reservations?*':'**/api/stock-reservations/'+item.id;
      await reader.page.getByRole('button',{name:'Tải lại',exact:true}).click();await quiesce(reader);
      const held=await captureOriginal(reader.page,pattern);
      try{
        if(mode==='list')await reader.page.getByRole('button',{name:'Tải lại',exact:true}).click();else await reader.page.getByRole('button',{name:'Chi tiết '+item.reservationCode,exact:true}).click();
        await held.arrived;await quiesce(reader,held.request());
        status(await grant(f.readerRole,'reservation.read',false),200);await refreshReader('Quyền đọc giữ hàng đã bị thu hồi',held.request());assert(await original.evaluate(node=>node.isConnected));
        const newCode='MR-FRESH-'+mode;await sql('UPDATE StockReservations SET ReservationCode=@code WHERE Id=@id; SELECT 1 AS changed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER',{code:newCode,id:item.id});
        status(await grant(f.readerRole,'reservation.read',true),200);await refreshReader('Có quyền đọc giữ hàng',held.request());await reader.page.getByText(newCode,{exact:true}).waitFor();
        held.release();await held.done;await quiesce(reader);assert(await original.evaluate(node=>node.isConnected));assert.equal(await reader.page.getByText(item.reservationCode,{exact:true}).count(),0);assert.equal(await reader.page.getByRole('dialog').count(),0);timings.push({mode,...held.observation});
      }finally{await held.cleanup();}
    }
    await reader.page.setViewportSize({width:375,height:812});await reader.page.getByRole('button',{name:'Chi tiết MR-FRESH-detail',exact:true}).click();const dialog=reader.page.getByRole('dialog');await dialog.waitFor();
    await reader.page.getByRole('button',{name:'Đóng',exact:true}).focus();await reader.page.keyboard.press('Tab');assert(await dialog.evaluate(node=>node.contains(document.activeElement)));await reader.page.keyboard.press('Escape');assert.equal(await dialog.count(),0);
    assert.equal(await reader.page.title(),'Giữ hàng — ERP KHO');const text=await reader.page.locator('body').innerText();assert(!/reservation\.(?:read|create|release)|\b(?:Active|PartiallyConsumed|Consumed|Released|Expired|Cancelled|Manual|ExportReceipt)\b/.test(text));
    return {hashes,actor:'Reader same session with stale Admin JWT/database QAReader grants',timings,assertions:['exact heading stays connected during revoke/regrant','old authorized list/detail cannot restore data/dialog','actual 403 interceptor refreshes context without replay','Vietnamese title/status/accessibility','375px keyboard focus containment/Escape'],scope:'Mounted real component in test-only entry; not a production route-unmount substitute'};
  });
}
