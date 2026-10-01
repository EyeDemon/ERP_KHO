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
