import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import exec from 'k6/execution';

const expectedRejections = new Counter('erp_expected_rejections');
const unexpectedErrors = new Rate('erp_unexpected_errors');
const measuredLatency = new Trend('erp_measurement_latency', true);
const credentials = JSON.parse(__ENV.ERP_CAPACITY_CREDENTIALS_JSON || '[]');
const fixtures = JSON.parse(__ENV.ERP_CAPACITY_FIXTURES_JSON || '{}');
const warmupSeconds = Number(__ENV.ERP_CAPACITY_WARMUP_SECONDS || 0);
const requestTimeout = `${Number(__ENV.ERP_CAPACITY_REQUEST_TIMEOUT_SECONDS || 10)}s`;
const started = Date.now();

export const options = {
  scenarios: { capacity: { executor: 'constant-vus', vus: Number(__ENV.ERP_CAPACITY_VUS), duration: __ENV.ERP_CAPACITY_TOTAL_DURATION } },
  thresholds: { erp_unexpected_errors: [{ threshold: 'rate<0.02', abortOnFail: true, delayAbortEval: '10s' }] },
  discardResponseBodies: true,
};

function identity() {
  const item = credentials[(exec.vu.idInTest - 1) % credentials.length];
  if (!item) throw new Error('No credential mapped to this worker.');
  return item;
}

function authenticate(user, force = false) {
  if (!force && user.token && Date.now() < user.expiresAtMs - 30000) return user.token;
  const response = http.post(`${__ENV.ERP_CAPACITY_API}/api/Auth/login`, JSON.stringify({ username: user.username, password: user.password }), { headers: { 'Content-Type': 'application/json' }, timeout: requestTimeout, responseType: 'text', tags: { operation: 'login', phase: phase() } });
  if (response.status !== 200) throw new Error(`Authentication failed with status ${response.status}.`);
  const body = response.json(); user.token = body.token; user.expiresAtMs = Date.parse(body.accessTokenExpiresAtUtc);
  if (!user.token || !Number.isFinite(user.expiresAtMs)) throw new Error('Authentication response omitted token expiry data.');
  return user.token;
}

function phase() { return (Date.now() - started) / 1000 < warmupSeconds ? 'warmup' : 'measurement'; }
function request(method, path, body, expected = [200], idempotencyKey = '', expectedRejection = null, category = 'read') {
  const user = identity(); const token = authenticate(user); const tags = { operation: path, category, phase: phase(), worker: String(exec.vu.idInTest) };
  const response = http.request(method, `${__ENV.ERP_CAPACITY_API}${path}`, body ? JSON.stringify(body) : null, { headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }, timeout: requestTimeout, tags });
  const isRejection = [403,404,409].includes(response.status);
  const labelledRejection = isRejection && expectedRejection?.scenario && expectedRejection?.reasonCode;
  const ok = expected.includes(response.status) && (!isRejection || labelledRejection);
  if (!ok) unexpectedErrors.add(true, tags); else unexpectedErrors.add(false, tags);
  if (labelledRejection && expected.includes(response.status)) expectedRejections.add(1, { ...tags, status: String(response.status), scenario: expectedRejection.scenario, reason: expectedRejection.reasonCode });
  if (phase() === 'measurement') measuredLatency.add(response.timings.duration, tags);
  check(response, { 'status matches labelled expectation': () => ok }, tags); return response;
}

export default function () {
  const owned = fixtures.workers?.[String(exec.vu.idInTest)];
  if (!owned) throw new Error('Worker fixture ownership is missing.');
  const draw = Math.random() * 100;
  if (phase() === 'warmup') {
    if (draw < 10) authenticate(identity(), true);
    else if (draw < 65) request('GET', `/api/InventoryStocks/current?warehouseId=${owned.warehouseId}`, null);
    else request('GET', `/api/InventoryTransactions?warehouseId=${owned.warehouseId}&page=1&pageSize=20`, null);
    sleep(0.2); return;
  }
  if (draw < 5) authenticate(identity(), true);
  else if (draw < 40) request('GET', `/api/InventoryStocks/current?warehouseId=${owned.warehouseId}`, null);
  else if (draw < 60) runOwnedOperation(owned, 'read');
  else if (draw < 70) request('GET', '/api/approvals/queue?pageIndex=1&pageSize=20', null);
  else if (draw < 75) request('GET', `/api/approvals/${owned.documentType}/${owned.documentId}`, null);
  else if (draw < 90) runOwnedOperation(owned, 'create');
  else runOwnedOperation(owned, exec.scenario.iterationInTest % 2 === 0 ? 'approve' : 'reject');
  sleep(0.2);
}

function runOwnedOperation(owned, kind) {
  const operations = owned.operations?.[kind] || [];
  const operation = operations[exec.scenario.iterationInTest % operations.length];
  if (!operation) throw new Error(`No ${kind} operation owned by this worker.`);
  request(operation.method || 'POST', operation.path, operation.body || null, operation.expectedStatuses || [200], operation.idempotencyKey || '', operation.expectedRejection || null, kind === 'read' ? 'read' : 'mutation');
}

export function handleSummary(data) {
  const samples = data.metrics.erp_measurement_latency?.values?.count;
  const status = Number.isFinite(samples) && samples > 0 ? 'REQUIRES_RECONCILIATION' : 'INVALID_ZERO_SAMPLES';
  return { [__ENV.ERP_CAPACITY_SUMMARY_PATH]: JSON.stringify({ schemaVersion: 1, runId: __ENV.ERP_CAPACITY_RUN_ID, status, metrics: data.metrics }, null, 2) };
}
