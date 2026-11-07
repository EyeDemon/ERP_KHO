import { test } from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateManifest, safeEvidence, assertFiltered } from './permission-browser.mjs';

test('ownership rejects foreign database, path and remote URLs', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const id = '0123456789abcdef0123456789abcdef';
  const folder = path.join(root, 'TestResults', 'BrowserQA', id);
  const filename = path.join(folder, 'manifest.json');
  const m = { RunId: id, Database: `ERP_KHO_BrowserQA_${id}`, MarkerType: 'LocalBrowserFullStackQA', ArtifactRoot: folder, ApiUrl: 'http://127.0.0.1:5265', FrontendUrl: 'http://127.0.0.1:4175' };
  validateManifest(m, filename);
  for (const change of [{ Database: 'ERP_KHO' }, { RunId: '../' }, { ArtifactRoot: root }, { ApiUrl: 'https://example.com' }, { MarkerType: 'Other' }]) assert.throws(() => validateManifest({ ...m, ...change }, filename));
});
test('evidence cannot persist credentials, raw bodies, headers or tokens', () => {
  assert.deepEqual(safeEvidence({ statuses: [200, 409], audits: 1 }), { statuses: [200, 409], audits: 1 });
  for (const field of ['password', 'token', 'cookie', 'authorization', 'connectionString', 'rowVersion', 'body', 'headers']) assert.throws(() => safeEvidence({ [field]: 'dummy' }));
  assert.throws(() => safeEvidence({ diagnostic: 'Bearer dummy' }));
});
test('Viewer recursive filtering rejects empty/null technical properties', () => {
  assertFiltered({ items: [{ id: 1 }], exceptionReason: null });
  for (const value of ['', null, 'dummy']) assert.throws(() => assertFiltered({ items: [{ rowVersion: value }] }));
  assert.throws(() => assertFiltered({ note: 'QA_PRIVATE_EXCEPTION' }));
});
import { captureOriginal } from './permission-browser.mjs';
import { spawnSync } from 'node:child_process';

function fakePage(kind) {
  let handler;
  const calls = { aborted: 0, unrouted: 0, fulfilled: 0 };
  const response = { status: () => kind === 'status' ? 403 : 200 };
  const route = {
    request: () => ({ url: () => 'http://127.0.0.1/api/importreceipts' }),
    fetch: async () => { if (kind === 'fetch') throw new Error('fetch failed'); if (kind === 'timeout-fetch') return new Promise(() => {}); return response; },
    fulfill: async () => { calls.fulfilled++; if (kind === 'fulfill') throw new Error('fulfill failed'); if (kind === 'timeout-fulfill') return new Promise(() => {}); },
    abort: async () => { calls.aborted++; if (kind === 'cleanup-hang') return new Promise(() => {}); },
  };
  const page = {
    route: async (_, callback) => { handler = callback; },
    unroute: async () => { calls.unrouted++; if (kind === 'cleanup-hang') return new Promise(() => {}); },
  };
  return { page, calls, start: () => handler(route) };
}

for (const kind of ['fetch', 'status', 'fulfill', 'timeout-fetch', 'timeout-fulfill', 'timeout-arrival', 'timeout-hold', 'cleanup-hang']) {
  test(`capture ${kind} rejects caller and cleanup is bounded`, { timeout: 2000 }, async () => {
    const fake = fakePage(kind);
    const held = await captureOriginal(fake.page, '**/api/importreceipts', { arrivalMs: 40, deadlineMs: 80, cleanupMs: 15 });
    if (kind !== 'timeout-arrival') void fake.start();
    if (['fulfill', 'timeout-fulfill'].includes(kind)) { await held.arrived; held.release(); }
    if (['timeout-hold', 'cleanup-hang'].includes(kind)) await held.arrived;
    const message = kind === 'fetch' ? /fetch failed/ : kind === 'status' ? /original authorized success/ : kind === 'fulfill' ? /fulfill failed/ : /not observed|deadline exceeded/;
    await assert.rejects(held.done, message);
    if (['fetch', 'status', 'timeout-fetch', 'timeout-arrival'].includes(kind)) await assert.rejects(held.arrived, message);
    const started = Date.now(); await held.cleanup(); assert(Date.now() - started < 500);
    assert.equal(fake.calls.unrouted, 1);
    if (kind !== 'timeout-arrival') assert.equal(fake.calls.aborted, 1);
  });
}

test('capture success settles only after successful fulfill', async () => {
  const fake = fakePage('success'); const held = await captureOriginal(fake.page, '**/api/importreceipts');
  const handled = fake.start(); await held.arrived; assert.equal(fake.calls.fulfilled, 0);
  held.release(); await held.done; await handled; await held.cleanup();
  assert.equal(fake.calls.fulfilled, 1); assert.equal(fake.calls.aborted, 0); assert.equal(fake.calls.unrouted, 1);
});

for (const kind of ['fetch', 'status', 'fulfill', 'timeout']) {
  test(`failed ${kind} runner exits FAIL and executes finally cleanup`, () => {
    const script = `
      import { captureOriginal } from './tools/ERP.BrowserQa/permission-browser.mjs';
      let handler;
      const page = { route: async (_, fn) => { handler = fn; }, unroute: async () => {} };
      const held = await captureOriginal(page, '*', { arrivalMs: 20, deadlineMs: 40, cleanupMs: 10 });
      void handler({ request: () => ({ url: () => 'http://127.0.0.1/api/test' }),
        fetch: async () => { if ('${kind}' === 'fetch') throw Error('fetch'); return { status: () => '${kind}' === 'status' ? 403 : 200 }; },
        fulfill: async () => { if ('${kind}' === 'fulfill') throw Error('fulfill'); }, abort: async () => {} });
      try { await held.arrived; if ('${kind}' !== 'timeout') held.release(); await held.done; }
      catch { process.exitCode = 1; console.log('FAIL'); }
      finally { await held.cleanup(); console.log('CLEANED'); }
    `;
    const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], { cwd: path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..'), encoding: 'utf8', timeout: 2000 });
    assert.equal(result.status, 1, result.stderr); assert.match(result.stdout, /FAIL\s+CLEANED/); assert.equal(result.signal, null);
  });
}

test('failed or hanging route registration fails setup and cleans within deadline', { timeout: 2000 }, async () => {
  for (const hangs of [false, true]) {
    let cleaned = 0;
    const page = { route: async () => { if (hangs) return new Promise(() => {}); throw new Error('registration failed'); }, unroute: async () => { cleaned++; } };
    await assert.rejects(captureOriginal(page, '*', { arrivalMs: 20, deadlineMs: 40, cleanupMs: 10 }), /registration failed|not observed/);
    assert.equal(cleaned, 1);
  }
});

test('case cancellation rejects pending completion instead of leaving it alive', async () => {
  const fake = fakePage('success'); const held = await captureOriginal(fake.page, '*');
  const handled = fake.start(); await held.arrived; await held.cleanup();
  await assert.rejects(held.done, /cancelled/); await handled;
  assert.equal(fake.calls.fulfilled, 0); assert.equal(fake.calls.aborted, 1);
});
