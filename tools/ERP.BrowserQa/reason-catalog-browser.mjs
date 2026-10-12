import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile, writeFile, access } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateManifest, safeEvidence, helper, captureOriginal, quiesce } from './permission-browser.mjs';

// Supplemental component-in-browser evidence; does not replace utility UI acceptance.
const here=path.dirname(fileURLToPath(import.meta.url)), repo=path.resolve(here,'../..');
const manifestPath=path.resolve(process.argv[process.argv.indexOf('--manifest')+1]);
assert(process.argv.includes('--manifest'),'Use an existing official owned run manifest');
const manifest=JSON.parse((await readFile(manifestPath,'utf8')).replace(/^\uFEFF/,''));
validateManifest(manifest,manifestPath); assert(manifest.ApiPid>0 && manifest.FrontendPid>0);
await access(path.join(manifest.ArtifactRoot,manifest.CredentialIdentifier));
const require=createRequire(import.meta.url), modulePath=process.env.ERP_KHO_PLAYWRIGHT_MODULE;
assert(modulePath,'Use installed test-only Playwright');
assert.equal(require(path.join(path.dirname(modulePath),'package.json')).version,'1.62.1');
const {chromium}=require(modulePath);
const sql=async text=>JSON.parse(await helper(manifestPath,'Sql',{sql:text}).completed);
const record={sourceHead:execFileSync('C:\\Program Files\\Git\\cmd\\git.exe',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}).trim(),runId:manifest.RunId,classification:'Supplemental mounted component-in-browser; real API; SQL fixture/postconditions',files:[],cases:[]};
for(const file of ['frontend/src/pages/ReceivingReasonCodes.tsx','frontend/e2e/mounted-reason-catalog.tsx','tools/ERP.BrowserQa/reason-catalog-browser.mjs']) record.files.push({file,sha256:createHash('sha256').update(await readFile(path.join(repo,file))).digest('hex')});
const report=path.join(manifest.ArtifactRoot,'reason-catalog-browser-'+Date.now()+'.json');
const save=()=>writeFile(report,JSON.stringify(safeEvidence(record),null,2));
const baselineSql="SELECT (SELECT COUNT(*) FROM AuditLogs WHERE EntityName<>'UserSession') audits,(SELECT COUNT(*) FROM IdempotencyRecords) claims,(SELECT SUM(Quantity) FROM InventoryStocks) stock,(SELECT SUM(ReservedQuantity) FROM InventoryStocks) reserved,(SELECT COUNT(*) FROM InventoryTransactions) ledger,(SELECT COUNT(*) FROM ImportReceipts) receipts,(SELECT COUNT(*) FROM ExportReceipts) exports,(SELECT COUNT(*) FROM StockReservations) reservations FOR JSON PATH,WITHOUT_ARRAY_WRAPPER";
const grantSql=add=>`DECLARE @role int=(SELECT Id FROM Roles WHERE RoleName='Viewer'); ${add?"INSERT RolePermissions(RoleId,PermissionId,GrantedAt) SELECT @role,Id,SYSUTCDATETIME() FROM Permissions p WHERE Code='reason_code.read' AND NOT EXISTS(SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=@role AND rp.PermissionId=p.Id)":"DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId WHERE rp.RoleId=@role AND p.Code='reason_code.read'"}; UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE Id=@role; SELECT 1 fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`;
const renameSql=prefix=>`UPDATE ReceivingReasonCodes SET Name=CASE Version WHEN 1 THEN N'${prefix}Lý do thử nghiệm phiên bản một' ELSE N'${prefix}Lý do thử nghiệm phiên bản hai' END WHERE Code='QA_CATALOG'; SELECT 1 fixtureOnly FOR JSON PATH,WITHOUT_ARRAY_WRAPPER`;
let server,browser,password; const heldResponses=[]; const before=await sql(baselineSql);
try {
  password=await helper(manifestPath,'Credential').completed;
  server=await chromium.launchServer({headless:true,host:'127.0.0.1'});
  browser=await chromium.connect(server.wsEndpoint());
  manifest.BrowserProfiles=server.process().spawnargs.filter(x=>x.startsWith('--user-data-dir=')).map(x=>x.slice('--user-data-dir='.length));
  manifest.BrowserRunner={Version:'1.62.1',Pid:process.pid,ServerPid:server.process().pid,Contexts:['reason-reader'],Ephemeral:true};
  await writeFile(manifestPath,JSON.stringify(manifest,null,2));
  manifest.BrowserRunner.Processes=JSON.parse(await helper(manifestPath,'Processes').completed);
  await writeFile(manifestPath,JSON.stringify(manifest,null,2));
  const context=await browser.newContext(),page=await context.newPage();
  const actor={pending:new Set()}; page.on('request',r=>actor.pending.add(r));
  page.on('requestfinished',r=>actor.pending.delete(r));page.on('requestfailed',r=>actor.pending.delete(r));
  await page.goto(manifest.FrontendUrl+'/e2e/mounted-reason-catalog.html#/login');
  await page.getByLabel('Tên đăng nhập',{exact:true}).fill('qa_viewer_browser');
  await page.getByLabel('Mật khẩu',{exact:true}).fill(password); password=undefined;
  await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();
  await page.getByText('Tìm thấy 30 phiên bản.',{exact:true}).waitFor();
  // A dialog correctly makes its surrounding controls inert. Refresh through the real
  // apiClient from this browser context; this is HTTP evidence, not a utility UI action.
  const refresh=()=>page.evaluate(async()=>{const {default:api}=await import('/src/services/apiClient.ts');try{await api.get('/api/permissions');return 200;}catch(error){return error.response?.status;}});
  for(const mode of ['list','detail','history']) {
    const result={mode,status:'RUNNING',startedAt:new Date().toISOString()}; record.cases.push(result); await save();
    await sql(renameSql('Cũ ')); await page.getByRole('button',{name:'Tải lại',exact:true}).click();
    await page.getByText('Cũ Lý do thử nghiệm phiên bản hai',{exact:true}).waitFor();
    const heading=await page.getByRole('heading',{name:'Lý do nhận hàng',exact:true}).elementHandle();
    const pattern=mode==='list'?/\/api\/receiving-reason-codes(?:\?|$)/:mode==='detail'?/\/api\/receiving-reason-codes\/\d+$/:/\/api\/receiving-reason-codes\/versions\/QA_CATALOG$/;
    const held=await captureOriginal(page,pattern,{deadlineMs:60000}); heldResponses.push(held);
    await page.getByRole('button',{name:mode==='list'?'Tải lại':'Chi tiết QA_CATALOG phiên bản 2',exact:true}).click();
    await held.arrived; assert((await held.original().text()).includes('Cũ Lý do thử nghiệm'),'Held original contains authorized older data');
    await sql(grantSql(false)); result.phase='revoke-refresh'; assert.equal(await refresh(),403); await save();
    await page.getByText('Quyền đọc danh mục đã bị thu hồi',{exact:true}).waitFor();
    await quiesce(actor,held.request()); assert(await heading.evaluate(node=>node.isConnected),'Catalog stayed mounted');
    assert.equal(await page.getByRole('dialog').count(),0);assert.equal(await page.locator('.reservation-table').count(),0);
    await sql(renameSql('Mới ')); await sql(grantSql(true));
    result.phase='regrant-refresh'; assert.equal(await refresh(),403); await save();
    await page.getByText('Mới Lý do thử nghiệm phiên bản hai',{exact:true}).waitFor();
    await quiesce(actor,held.request()); held.release(); await held.done; await quiesce(actor);
    assert(await heading.evaluate(node=>node.isConnected),'Original heading survives regrant/release');
    assert.equal(await page.getByText(/Cũ Lý do thử nghiệm/).count(),0);assert.equal(await page.getByRole('dialog').count(),0);
    Object.assign(result,{status:'PASS',finishedAt:new Date().toISOString(),capture:held.observation,mounted:true,oldDataAbsent:true,dialogAbsent:true});await save();
    await held.cleanup(); await heading.dispose();
  }
} catch(error) { const active=record.cases.at(-1); if(active?.status==='RUNNING')active.status='FAIL';record.failureHash=createHash('sha256').update(String(error.message)).digest('hex');record.failureLocation=String(error.stack).match(/reason-catalog-browser\.mjs:\d+:\d+/)?.[0];process.exitCode=1;console.error('FAIL: reason catalog browser assertions; sanitized evidence retained.'); }
finally {
  password=undefined;
  for(const held of heldResponses) await held.cleanup();
  // The original QA master fixture is restored; production/business databases are never targeted.
  try { await sql(renameSql(''));await sql(grantSql(true)); record.after=await sql(baselineSql);assert.deepEqual(record.after,before); }
  catch {record.postconditions='FAIL';process.exitCode=1;}
  for(const resource of [browser,server]) if(resource) {let timer;try{await Promise.race([resource.close(),new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('Bounded browser cleanup')),5000)})]);}catch{record.cleanup='UNKNOWN';process.exitCode=1;}finally{clearTimeout(timer);}}
  record.profilesRemoved=(await Promise.all((manifest.BrowserProfiles||[]).map(async file=>{try{await access(file);return false;}catch{return true;}}))).every(Boolean);
  if(!record.profilesRemoved)process.exitCode=1;
  await save(); console.log(`Reason catalog late responses: ${record.cases.filter(x=>x.status==='PASS').length}/3; profiles removed=${record.profilesRemoved}; exit=${process.exitCode||0}`);
}
