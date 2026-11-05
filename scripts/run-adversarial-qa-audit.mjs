#!/usr/bin/env node
/**
 * ERP_KHO — Canonical Independent Adversarial QA & Live Chrome Audit Runner
 * Directive: CA-WAVE20-TRUE-PIXEL-ZERO-TRUNCATION-DESIGN-SYSTEM-V2-AND-UX-CLOSURE-020
 * Governing Specs: CA-QA-ADVERSARIAL-001, CA-GOV-INTEGRATION-001, CA-AGENT4-SECURITY-INTEGRATION-001,
 *                  CA-MOBBIN-WORKFLOW-INTEGRATION-001, CA-PAGE-ARCH-GOV-001,
 *                  Wave-20 True Pixel & Geometry Gate (54-Route Sweep + W20_01..12.png),
 *                  OWN-DEC-CLEAN-SOURCE-ARCHITECTURE-AND-ARTIFACT-HYGIENE,
 *                  OWN-DEC-VIETQR-ON-DEMAND-ONLY, OWN-DEC-PLATFORM-RBAC-FUTURE-READY
 *
 * Exclusive Write Scope (AGENT-03 ONLY):
 *   - artifacts/live-browser-audit/current/qa-adversarial/*.json
 *   - artifacts/live-browser-audit/current/screenshots/W20_*.png (12 canonical files; 0 non-W20 PNGs)
 *
 * Strictly Read-Only:
 *   - Application source code (ERP.*, frontend/src/**, frontend/package.json, tools/**, docs/**)
 *   - artifacts/live-browser-audit/current/security-audit/** (owned by AGENT-04)
 *   - artifacts/live-browser-audit/current/ux-mobbin-research/** (owned by AGENT-02)
 *   - artifacts/live-browser-audit/current/engineering-response/** (owned by AGENT-00)
 *
 * Usage:
 *   node scripts/run-adversarial-qa-audit.mjs --round=1
 *   node scripts/run-adversarial-qa-audit.mjs --round=3
 *   node scripts/run-adversarial-qa-audit.mjs --round=5
 *   node scripts/run-adversarial-qa-audit.mjs --help
 */

import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const REPO_ROOT = path.resolve(path.dirname(__filename), '..');

const DIRECTIVE_ID = 'CA-WAVE20-TRUE-PIXEL-ZERO-TRUNCATION-DESIGN-SYSTEM-V2-AND-UX-CLOSURE-020';
const WAVE_ID = 'WAVE-20-FINAL-PIXEL-VERIFIED-UIUX-AUDIT-REMEDIATION-AND-35-ROUTE-SWEEP';
const LEGACY_PLACEHOLDER_ACC = ['0000', '10002026'].join('');
const BASE_API = process.env.ERP_API_BASE_URL || 'http://127.0.0.1:5108';
const BASE_WEB = process.env.ERP_WEB_BASE_URL || 'http://127.0.0.1:5173';

const CURRENT_AUDIT_DIR = path.join(REPO_ROOT, 'artifacts', 'live-browser-audit', 'current');
const QA_OUT_DIR = path.join(CURRENT_AUDIT_DIR, 'qa-adversarial');
const SCREENSHOT_DIR = path.join(CURRENT_AUDIT_DIR, 'screenshots');
const SEC_AUDIT_DIR_READONLY = path.join(CURRENT_AUDIT_DIR, 'security-audit');
const MOBBIN_DIR_READONLY = path.join(CURRENT_AUDIT_DIR, 'ux-mobbin-research');
const ENG_RESP_DIR_READONLY = path.join(CURRENT_AUDIT_DIR, 'engineering-response');

const ALLOWED_WRITE_ROOTS = [
  path.resolve(QA_OUT_DIR).toLowerCase(),
  path.resolve(SCREENSHOT_DIR).toLowerCase(),
];

function parseCliArgs(argv) {
  const opts = {
    round: null,
    help: false,
    selfCheck: false,
    strictExit: true,
  };
  for (const arg of argv) {
    if (arg === '--help' || arg === '-h') opts.help = true;
    else if (arg === '--self-check') opts.selfCheck = true;
    else if (arg === '--no-strict-exit') opts.strictExit = false;
    else if (arg.startsWith('--round=')) {
      const n = Number(arg.slice('--round='.length));
      if ([1, 3, 5].includes(n)) opts.round = n;
    }
  }
  return opts;
}

function assertAllowedWritePath(targetPath) {
  const resolved = path.resolve(targetPath).toLowerCase();
  const allowed = ALLOWED_WRITE_ROOTS.some(
    (root) => resolved === root || resolved.startsWith(`${root}${path.sep.toLowerCase()}`),
  );
  if (!allowed) {
    throw new Error(`SECURITY BOUNDARY VIOLATION: Attempted write outside AGENT-03 scope: ${targetPath}`);
  }
}

function safeWriteJson(targetPath, payload) {
  assertAllowedWritePath(targetPath);
  fs.mkdirSync(path.dirname(targetPath), { recursive: true });
  fs.writeFileSync(targetPath, `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
}

function safeWritePng(targetPath, pngBuffer) {
  assertAllowedWritePath(targetPath);
  const magic = pngBuffer.subarray(0, 8);
  const expectedMagic = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  if (!magic.equals(expectedMagic)) {
    throw new Error(`Invalid PNG header for ${path.basename(targetPath)}`);
  }
  fs.mkdirSync(path.dirname(targetPath), { recursive: true });
  fs.writeFileSync(targetPath, pngBuffer);
  const width = pngBuffer.readUInt32BE(16);
  const height = pngBuffer.readUInt32BE(20);
  return {
    fileName: path.basename(targetPath),
    byteLength: pngBuffer.length,
    width,
    height,
    sha256: crypto.createHash('sha256').update(pngBuffer).digest('hex'),
  };
}

function safeReadJsonOptional(filePath) {
  try {
    if (!fs.existsSync(filePath)) return null;
    const raw = fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '');
    return JSON.parse(raw);
  } catch (err) {
    return { _readError: String(err?.message || err), _path: filePath };
  }
}

async function resolvePlaywright() {
  const candidates = [
    path.join(REPO_ROOT, 'frontend', 'node_modules', 'playwright', 'index.mjs'),
    path.join(REPO_ROOT, 'node_modules', 'playwright', 'index.mjs'),
  ];
  if (process.env.APPDATA) {
    candidates.push(path.join(process.env.APPDATA, 'npm', 'node_modules', 'playwright', 'index.mjs'));
  }
  if (process.env.LOCALAPPDATA) {
    const npxRoot = path.join(process.env.LOCALAPPDATA, 'npm-cache', '_npx');
    if (fs.existsSync(npxRoot)) {
      for (const ent of fs.readdirSync(npxRoot, { withFileTypes: true })) {
        if (ent.isDirectory()) {
          candidates.push(path.join(npxRoot, ent.name, 'node_modules', 'playwright', 'index.mjs'));
        }
      }
    }
  }
  for (const c of candidates) {
    if (fs.existsSync(c)) {
      return await import(pathToFileURL(c).href);
    }
  }
  return await import('playwright');
}

function buildValidPdf14Buffer() {
  const streamText = [
    'BT',
    '/F1 14 Tf',
    '50 760 Td',
    '(UY NHIEM CHI / BANK CREDIT ADVICE - VIETCOMBANK) Tj',
    '0 -24 Td',
    '/F1 11 Tf',
    '(Ma giao dich / Ref: FT2627716888) Tj',
    '0 -18 Td',
    '(Thoi diem ghi co / Credit Time: 02/11/2026 18:15) Tj',
    '0 -18 Td',
    '(Don vi thu huong: HUYNH PHI VAN - TPBank 98418102005) Tj',
    '0 -18 Td',
    '(Don vi thanh toan: CONG TY TNHH THUONG MAI VA KHO VAN DAI PHAT - TENANT-001) Tj',
    '0 -18 Td',
    '(Ma hoa don SaaS: INV-SAAS-202611-002 - Ky cuoc 11/2026) Tj',
    '0 -18 Td',
    '(So tien thuc nhan: 1.490.000 VND - Da gom 10% VAT) Tj',
    'ET',
  ].join('\n');

  const streamBytes = Buffer.from(streamText, 'ascii');
  const objects = [
    '1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n',
    '2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n',
    '3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n',
    '4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n',
    `5 0 obj\n<< /Length ${streamBytes.length} >>\nstream\n${streamText}\nendstream\nendobj\n`,
  ];

  const chunks = [Buffer.from('%PDF-1.4\n', 'ascii')];
  const offsets = [0];
  let currentOffset = chunks[0].length;
  for (const obj of objects) {
    offsets.push(currentOffset);
    const buf = Buffer.from(obj, 'ascii');
    chunks.push(buf);
    currentOffset += buf.length;
  }
  const xrefOffset = currentOffset;
  const xrefLines = [`xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`];
  for (let i = 1; i < offsets.length; i += 1) {
    xrefLines.push(`${String(offsets[i]).padStart(10, '0')} 00000 n \n`);
  }
  const trailer =
    xrefLines.join('') +
    `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xrefOffset}\n%%EOF\n`;
  chunks.push(Buffer.from(trailer, 'ascii'));
  return Buffer.concat(chunks);
}

function walkFiles(dirPath, predicate) {
  if (!fs.existsSync(dirPath)) return [];
  const results = [];
  const stack = [dirPath];
  while (stack.length > 0) {
    const cur = stack.pop();
    const entries = fs.readdirSync(cur, { withFileTypes: true });
    for (const ent of entries) {
      const full = path.join(cur, ent.name);
      if (ent.isDirectory()) {
        if (ent.name === 'bin' || ent.name === 'obj' || ent.name === 'node_modules' || ent.name === '__tests__') {
          continue;
        }
        stack.push(full);
      } else if (ent.isFile() && (!predicate || predicate(full, ent.name))) {
        results.push(full);
      }
    }
  }
  return results.sort();
}

function countLines(filePath) {
  const content = fs.readFileSync(filePath, 'utf8');
  if (content.length === 0) return 0;
  return content.split(/\r?\n/).length;
}

function getGitHeadSha() {
  try {
    return execFileSync('git', ['rev-parse', 'HEAD'], { cwd: REPO_ROOT, encoding: 'utf8' }).trim();
  } catch {
    return 'UNKNOWN_SHA';
  }
}

function auditStaticArchitectureAndHygiene(addFinding, addCheck, round = 1) {
  // 1. Historical EF Core migrations count & [DB-004] immutability
  const migrationsDir = path.join(REPO_ROOT, 'ERP.Infrastructure', 'Migrations');
  const migrationFiles = fs.existsSync(migrationsDir)
    ? fs
        .readdirSync(migrationsDir)
        .filter((f) => /^\d{14}_.*\.cs$/i.test(f) && !f.endsWith('.Designer.cs'))
    : [];
  let modifiedHistoricalMigrations = [];
  try {
    const gitMigStatus = execFileSync('git', ['status', '--porcelain', '--', 'ERP.Infrastructure/Migrations'], {
      cwd: REPO_ROOT,
      encoding: 'utf8',
    })
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter((l) => l.length > 0 && !l.includes('ErpKhoDbContextModelSnapshot.cs') && !l.startsWith('??'));
    modifiedHistoricalMigrations = gitMigStatus;
  } catch {
    modifiedHistoricalMigrations = [];
  }

  addCheck('GATE1-MIGRATIONS-65', migrationFiles.length === 65 && modifiedHistoricalMigrations.length === 0, {
    migrationCount: migrationFiles.length,
    modifiedHistoricalMigrations,
  });
  if (migrationFiles.length !== 65 || modifiedHistoricalMigrations.length > 0) {
    addFinding({
      id: 'QA-W17-ARCH-01',
      severity: 'HIGH',
      domain: 'Database & Migration Hygiene ([DB-004])',
      ownerAgent: 'AGENT-01',
      summary: `Expected 65 untouched EF Core migrations; got count=${migrationFiles.length}, modified=${modifiedHistoricalMigrations.length}`,
      evidence: { migrationCount: migrationFiles.length, modifiedHistoricalMigrations },
    });
  }

  // 2. Backend Services <= 450 LOC & Controllers <= 350 LOC
  const serviceFiles = walkFiles(path.join(REPO_ROOT, 'ERP.Infrastructure', 'Services'), (f) => f.endsWith('.cs'));
  const servicesGt450 = serviceFiles
    .map((f) => ({ file: path.relative(REPO_ROOT, f).replace(/\\/g, '/'), loc: countLines(f) }))
    .filter((x) => x.loc > 450);

  const controllerFiles = walkFiles(path.join(REPO_ROOT, 'ERP.Api', 'Controllers'), (f) => f.endsWith('.cs'));
  const controllersGt350 = controllerFiles
    .map((f) => ({ file: path.relative(REPO_ROOT, f).replace(/\\/g, '/'), loc: countLines(f) }))
    .filter((x) => x.loc > 350);

  addCheck('GATE1-BACKEND-LOC', servicesGt450.length === 0 && controllersGt350.length === 0, {
    totalServices: serviceFiles.length,
    BACKEND_SERVICES_GT_450_LOC: servicesGt450.length,
    servicesGt450,
    totalControllers: controllerFiles.length,
    BACKEND_CONTROLLERS_GT_350_LOC: controllersGt350.length,
    controllersGt350,
  });
  if (servicesGt450.length > 0 || controllersGt350.length > 0) {
    addFinding({
      id: 'QA-W17-ARCH-02',
      severity: 'MEDIUM',
      domain: 'Backend Clean Architecture LOC Ceilings',
      ownerAgent: 'AGENT-01',
      summary: `Backend LOC ceiling exceeded: services>450=${servicesGt450.length}, controllers>350=${controllersGt350.length}`,
      evidence: { servicesGt450, controllersGt350 },
    });
  }

  // 3. Frontend Pages <= 500 LOC & Modules <= 400 LOC
  const isProdTsFile = (f) =>
    /\.(tsx|ts)$/i.test(f) && !/\.(test|spec)\.(tsx|ts)$/i.test(f) && !f.endsWith('.d.ts');
  const pageFiles = walkFiles(path.join(REPO_ROOT, 'frontend', 'src', 'pages'), isProdTsFile);
  const pagesGt500 = pageFiles
    .map((f) => ({ file: path.relative(REPO_ROOT, f).replace(/\\/g, '/'), loc: countLines(f) }))
    .filter((x) => x.loc > 500);

  const modFiles = walkFiles(path.join(REPO_ROOT, 'frontend', 'src', 'modules'), isProdTsFile);
  const modsGt400 = modFiles
    .map((f) => ({ file: path.relative(REPO_ROOT, f).replace(/\\/g, '/'), loc: countLines(f) }))
    .filter((x) => x.loc > 400);

  addCheck('GATE1-FRONTEND-LOC', pagesGt500.length === 0 && modsGt400.length === 0, {
    totalPages: pageFiles.length,
    PAGES_GT_500: pagesGt500.length,
    pagesGt500,
    totalModules: modFiles.length,
    MODS_GT_400: modsGt400.length,
    modsGt400,
  });
  if (pagesGt500.length > 0 || modsGt400.length > 0) {
    addFinding({
      id: 'QA-W17-ARCH-03',
      severity: 'MEDIUM',
      domain: 'Frontend Clean Module LOC Ceilings',
      ownerAgent: 'AGENT-02',
      summary: `Frontend LOC ceiling exceeded: pages>500=${pagesGt500.length}, modules>400=${modsGt400.length}`,
      evidence: { pagesGt500, modsGt400 },
    });
  }

  // 4. Single Canonical Mock Data File (MOCK_DATA_FILES_COUNT == 1) & demoApiAdapter <= 350 LOC
  const mocksDir = path.join(REPO_ROOT, 'frontend', 'src', 'mocks');
  const mockFiles = walkFiles(mocksDir, isProdTsFile).map((f) => path.relative(REPO_ROOT, f).replace(/\\/g, '/'));
  const legacyMockCandidates = [
    'frontend/src/services/demoApiData.ts',
    'frontend/src/services/erpWmsMockData.ts',
    'frontend/src/services/inventoryReconciliationDemo.ts',
  ].filter((rel) => fs.existsSync(path.join(REPO_ROOT, rel)));
  const adapterPath = path.join(REPO_ROOT, 'frontend', 'src', 'services', 'demoApiAdapter.ts');
  const adapterLoc = fs.existsSync(adapterPath) ? countLines(adapterPath) : -1;

  const mockOk =
    mockFiles.length === 1 &&
    mockFiles[0] === 'frontend/src/mocks/canonicalErpWmsMockData.ts' &&
    legacyMockCandidates.length === 0 &&
    adapterLoc > 0 &&
    adapterLoc <= 350;

  addCheck('GATE1-SINGLE-MOCK-DATA', mockOk, {
    MOCK_DATA_FILES_COUNT: mockFiles.length + legacyMockCandidates.length,
    mockFiles,
    legacyMockCandidates,
    demoApiAdapterLoc: adapterLoc,
  });
  if (!mockOk) {
    addFinding({
      id: 'QA-W17-MOCK-01',
      severity: 'MEDIUM',
      domain: 'Single Canonical Mock Data Governance',
      ownerAgent: 'AGENT-00',
      summary: 'Mock data must be consolidated into frontend/src/mocks/canonicalErpWmsMockData.ts with demoApiAdapter.ts <= 350 LOC',
      evidence: { mockFiles, legacyMockCandidates, demoApiAdapterLoc: adapterLoc },
    });
  }

  // 5. Single Master Project Report (v21.0/v22.0 in Rounds 1-3, v22.0 in Round 5) & Zero Standalone Reassessment Markdown
  const productReassessmentExists = fs.existsSync(path.join(REPO_ROOT, 'product_reassessment'));
  const masterReportPath = path.join(REPO_ROOT, 'docs', 'project', 'ERP_WMS_MASTER_PROJECT_REPORT.md');
  const masterReportExists = fs.existsSync(masterReportPath);
  const masterReportHeader = masterReportExists
    ? fs.readFileSync(masterReportPath, 'utf8').slice(0, 4000)
    : '';
  const masterReportVersionOk =
    round === 5 ? /v22\.0/i.test(masterReportHeader) : /v(?:21|22)\.0/i.test(masterReportHeader);

  addCheck('GATE1-MASTER-REPORT', !productReassessmentExists && masterReportExists && masterReportVersionOk, {
    STANDALONE_REPORT_MD_COUNT: productReassessmentExists ? 1 : 0,
    productReassessmentExists,
    masterReportExists,
    round,
    masterReportVersionOk,
  });
  if (productReassessmentExists || !masterReportExists || !masterReportVersionOk) {
    addFinding({
      id: 'QA-W20-GOV-01',
      severity: 'MEDIUM',
      domain: 'Master Report Consolidation (v21.0 / v22.0)',
      ownerAgent: 'AGENT-00',
      summary:
        round === 5
          ? 'product_reassessment/ must remain removed and ERP_WMS_MASTER_PROJECT_REPORT.md must be upgraded to v22.0 for Round 5'
          : 'product_reassessment/ must remain removed and ERP_WMS_MASTER_PROJECT_REPORT.md must be v21.0 or v22.0',
      evidence: { productReassessmentExists, masterReportExists, round, masterReportVersionOk },
    });
  }

  // 6. Legacy Wave Artifact Directories Count == 0
  const liveAuditRoot = path.join(REPO_ROOT, 'artifacts', 'live-browser-audit');
  const legacyWaveDirs = fs.existsSync(liveAuditRoot)
    ? fs
        .readdirSync(liveAuditRoot, { withFileTypes: true })
        .filter((d) => d.name !== 'current')
        .map((d) => d.name)
    : [];

  addCheck('GATE1-LEGACY-ARTIFACT-HYGIENE', legacyWaveDirs.length === 0, {
    LEGACY_WAVE_ARTIFACT_DIRS_COUNT: legacyWaveDirs.length,
    legacyWaveDirs,
  });
  if (legacyWaveDirs.length > 0) {
    addFinding({
      id: 'QA-W18-HYG-01',
      severity: 'LOW',
      domain: 'Artifact Directory Hygiene',
      ownerAgent: 'AGENT-00',
      summary: `Legacy wave entries found outside current/: ${legacyWaveDirs.join(', ')}`,
      evidence: { legacyWaveDirs },
    });
  }

  // 7. Distinct inventory_opening_balance.import & Customer catalog separation (141 perms, 0 platform.*) in AppPermissions.cs
  const domainPermPath = path.join(REPO_ROOT, 'ERP.Domain', 'Security', 'AppPermissions.cs');
  const domainPermText = fs.existsSync(domainPermPath) ? fs.readFileSync(domainPermPath, 'utf8') : '';
  const hasOpeningBalancePerm = domainPermText.includes('inventory_opening_balance.import');
  const hasCustomerCatalogSeparation =
    /CustomerPermissionCatalog|CustomerAllPermissions|CustomerDefinitions/.test(domainPermText);
  addCheck('SEC-STATIC-OPENING-BALANCE-AND-CUSTOMER-CATALOG', hasOpeningBalancePerm && hasCustomerCatalogSeparation, {
    file: 'ERP.Domain/Security/AppPermissions.cs',
    hasOpeningBalancePerm,
    hasCustomerCatalogSeparation,
  });
  if (!hasOpeningBalancePerm || !hasCustomerCatalogSeparation) {
    addFinding({
      id: 'QA-W18-SEC-F04',
      severity: 'HIGH',
      domain: 'Authorization & CustomerPermissionCatalog Separation (FND-W18-02)',
      ownerAgent: 'AGENT-01',
      summary: 'ERP.Domain/Security/AppPermissions.cs must define inventory_opening_balance.import and CustomerAllPermissions/CustomerDefinitions',
      evidence: { file: 'ERP.Domain/Security/AppPermissions.cs', hasOpeningBalancePerm, hasCustomerCatalogSeparation },
    });
  }

  // 8. Mobbin Reference-Backed UX Research Artifact (CA-MOBBIN-WORKFLOW-INTEGRATION-001 / FND-W18-07)
  const mobbinJsonPath = path.join(MOBBIN_DIR_READONLY, 'mobbin-design-research.json');
  const mobbinDoc = safeReadJsonOptional(mobbinJsonPath);
  const mobbinKeys = mobbinDoc && !mobbinDoc._readError ? Object.keys(mobbinDoc) : [];
  const hasSectionsAtoG = ['A', 'B', 'C', 'D', 'E', 'F', 'G'].every((letter) =>
    mobbinKeys.some((k) => k.startsWith(`section${letter}_`)),
  );
  const fabricatedMobbinUrlsCount = Number(mobbinDoc?.sectionA_capabilityProbeAndGovernance?.fabricatedMobbinUrlsCount ?? -1);
  const fabricatedScreenIdsCount = Number(mobbinDoc?.sectionA_capabilityProbeAndGovernance?.fabricatedScreenIdsCount ?? -1);
  const mobbinOk =
    Boolean(mobbinDoc && !mobbinDoc._readError) &&
    hasSectionsAtoG &&
    fabricatedMobbinUrlsCount === 0 &&
    fabricatedScreenIdsCount === 0;
  addCheck('GOV-MOBBIN-RESEARCH-SECTIONS-A-TO-G-ZERO-FABRICATION', mobbinOk, {
    file: 'artifacts/live-browser-audit/current/ux-mobbin-research/mobbin-design-research.json',
    hasSectionsAtoG,
    probeStatus: mobbinDoc?.sectionA_capabilityProbeAndGovernance?.probeStatus || null,
    fabricatedMobbinUrlsCount,
    fabricatedScreenIdsCount,
  });
  if (!mobbinOk) {
    addFinding({
      id: 'QA-W18-MOBBIN-01',
      severity: 'MEDIUM',
      domain: 'Mobbin UX Workflow Governance (CA-MOBBIN-WORKFLOW-INTEGRATION-001)',
      ownerAgent: 'AGENT-02',
      summary: 'mobbin-design-research.json must exist with sections A..G and 0 fabricated Mobbin URLs/IDs',
      evidence: { hasSectionsAtoG, fabricatedMobbinUrlsCount, fabricatedScreenIdsCount },
    });
  }

  // 9. Repo-Wide Zero Legacy 12-Digit Placeholder Account Check (FND-W18-01)
  const scanRoots = [
    path.join(REPO_ROOT, 'ERP.Domain'),
    path.join(REPO_ROOT, 'ERP.Application'),
    path.join(REPO_ROOT, 'ERP.Infrastructure'),
    path.join(REPO_ROOT, 'ERP.Api'),
    path.join(REPO_ROOT, 'ERP.Application.Tests'),
    path.join(REPO_ROOT, 'ERP.Api.Tests'),
    path.join(REPO_ROOT, 'ERP.SqlIntegrationHarness'),
    path.join(REPO_ROOT, 'tools'),
    path.join(REPO_ROOT, 'frontend', 'src'),
    path.join(REPO_ROOT, 'scripts'),
    path.join(REPO_ROOT, 'docs'),
    path.join(REPO_ROOT, 'artifacts', 'live-browser-audit', 'current'),
  ];
  const isTextFile = (f) => /\.(cs|ts|tsx|js|mjs|cjs|json|md|sql|ps1|yml|yaml|css|html)$/i.test(f);
  const legacyPlaceholderFiles = [];
  for (const root of scanRoots) {
    if (!fs.existsSync(root)) continue;
    const stack = [root];
    while (stack.length > 0) {
      const cur = stack.pop();
      for (const ent of fs.readdirSync(cur, { withFileTypes: true })) {
        const full = path.join(cur, ent.name);
        if (ent.isDirectory()) {
          if (ent.name === 'bin' || ent.name === 'obj' || ent.name === 'node_modules' || ent.name === '.git') continue;
          stack.push(full);
        } else if (ent.isFile() && isTextFile(full)) {
          const txt = fs.readFileSync(full, 'utf8');
          if (txt.includes(LEGACY_PLACEHOLDER_ACC)) {
            legacyPlaceholderFiles.push(path.relative(REPO_ROOT, full).replace(/\\/g, '/'));
          }
        }
      }
    }
  }
  addCheck('FND-W18-01-ZERO-LEGACY-12DIGIT-PLACEHOLDER-IN-REPO', legacyPlaceholderFiles.length === 0, {
    legacy12DigitPlaceholderHitsCount: legacyPlaceholderFiles.length,
    files: legacyPlaceholderFiles,
  });
  if (legacyPlaceholderFiles.length > 0) {
    addFinding({
      id: 'QA-W18-FND-01',
      severity: 'HIGH',
      domain: 'Canonical Beneficiary Account Purity (FND-W18-01)',
      ownerAgent: 'AGENT-03',
      summary: `Found ${legacyPlaceholderFiles.length} file(s) still containing legacy 12-digit placeholder account`,
      evidence: { files: legacyPlaceholderFiles },
    });
  }

  // 10. Layer 4 UI Honest Initial State, Zero Fabricated Metrics & Zero localStorage Paid Override ([FE-002], PROJECT_RULES §4.2 & §4.4, SEC-OBS-P3-002)
  const pccPath = path.join(REPO_ROOT, 'frontend', 'src', 'pages', 'PlatformControlCenter.tsx');
  const pbhPath = path.join(REPO_ROOT, 'frontend', 'src', 'modules', 'platform', 'platformBillingHelpers.ts');
  const phwPath = path.join(REPO_ROOT, 'frontend', 'src', 'modules', 'platform', 'PlatformHealthAndWorkersSection.tsx');
  const sbPath = path.join(REPO_ROOT, 'frontend', 'src', 'pages', 'SaasBilling.tsx');

  const pccText = fs.existsSync(pccPath) ? fs.readFileSync(pccPath, 'utf8') : '';
  const pbhText = fs.existsSync(pbhPath) ? fs.readFileSync(pbhPath, 'utf8') : '';
  const phwText = fs.existsSync(phwPath) ? fs.readFileSync(phwPath, 'utf8') : '';
  const sbText = fs.existsSync(sbPath) ? fs.readFileSync(sbPath, 'utf8') : '';

  const uiFakeStateViolations = [];
  if (/useState<string>\(\s*['"]235512['"]\s*\)/.test(pccText) || /useState<string>\(\s*['"]0c06\*{4}12be['"]\s*\)/.test(pccText)) {
    uiFakeStateViolations.push({
      file: 'frontend/src/pages/PlatformControlCenter.tsx',
      violation: 'Hardcoded initial state ghnShopId="235512" and maskedToken="0c06****12be" before API resolution',
    });
  }
  if (/useState<PlatformWorkerHealthItem\[\]>\(\s*DEFAULT_WORKERS\s*\)/.test(pccText) || /useState<PlatformIntegrationMetrics>\(\s*DEFAULT_INTEGRATION_METRICS\s*\)/.test(pccText)) {
    uiFakeStateViolations.push({
      file: 'frontend/src/pages/PlatformControlCenter.tsx',
      violation: 'Initializes workers and integrationMetrics from non-empty fake DEFAULT_WORKERS / DEFAULT_INTEGRATION_METRICS',
    });
  }
  if (/currentOrders\s*>\s*0\s*\?\s*currentOrders\s*:\s*6420/.test(pbhText)) {
    uiFakeStateViolations.push({
      file: 'frontend/src/modules/platform/platformBillingHelpers.ts',
      violation: 'Fabricates currentMonthOrderCount = 6420 whenever backend returns 0',
    });
  }
  if (
    pccText.includes("localStorage.setItem(\n        'erp_saas_inv_202611_paid'") ||
    pccText.includes("localStorage.setItem('erp_saas_inv_202611_paid'") ||
    pbhText.includes("getItem('erp_saas_inv_202611_paid')") ||
    sbText.includes("getItem('erp_saas_inv_202611_paid')")
  ) {
    uiFakeStateViolations.push({
      file: 'frontend/src/pages/SaasBilling.tsx',
      violation: 'Client-side localStorage key erp_saas_inv_202611_paid overrides real backend UNPAID invoice status to PAID',
    });
  }
  if (phwText.includes(LEGACY_PLACEHOLDER_ACC) || sbText.includes(LEGACY_PLACEHOLDER_ACC)) {
    uiFakeStateViolations.push({
      file: 'frontend/src/pages/SaasBilling.tsx',
      violation: 'Contains forbidden legacy 12-digit placeholder bank account',
    });
  }

  addCheck('UI-NO-FAKE-INITIAL-STATE-OR-6420-FALLBACK', uiFakeStateViolations.length === 0, {
    violationCount: uiFakeStateViolations.length,
    violations: uiFakeStateViolations,
  });
  if (uiFakeStateViolations.length > 0) {
    addFinding({
      id: 'QA-W18-FE-STATE-01',
      severity: 'MEDIUM',
      domain: 'Layer 4 Frontend Honest State & Anti-Fake Policy ([FE-002], PROJECT_RULES §4.2/§4.4)',
      ownerAgent: 'AGENT-02',
      summary: 'Platform & SaaS Billing UI contains hardcoded fake initial states or forbidden overrides',
      evidence: { violations: uiFakeStateViolations },
    });
  }
}

function parseRgbString(rgbStr) {
  const m = String(rgbStr || '').match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
  if (!m) return null;
  return [Number(m[1]), Number(m[2]), Number(m[3])];
}

function relativeLuminance([r, g, b]) {
  const toLinear = (v) => {
    const s = v / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  const [R, G, B] = [toLinear(r), toLinear(g), toLinear(b)];
  return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}

function computeContrastRatio(bgRgbStr, fgRgbStr) {
  const bg = parseRgbString(bgRgbStr);
  const fg = parseRgbString(fgRgbStr);
  if (!bg || !fg) return 0;
  const L1 = relativeLuminance(bg);
  const L2 = relativeLuminance(fg);
  return Number(((Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05)).toFixed(2));
}

async function measureButtonContrast(page, selector, route, label) {
  const loc = page.locator(selector).first();
  await loc.waitFor({ state: 'visible', timeout: 10000 });
  const styles = await loc.evaluate((el) => {
    const cs = window.getComputedStyle(el);
    return {
      text: (el.textContent || '').trim(),
      backgroundColor: cs.backgroundColor,
      color: cs.color,
      heightPx: Math.round(el.getBoundingClientRect().height),
    };
  });
  return {
    route,
    label,
    selector,
    ...styles,
    contrastRatio: computeContrastRatio(styles.backgroundColor, styles.color),
  };
}

async function capturePureCdpBuffer(page) {
  const cdp = await page.context().newCDPSession(page);
  try {
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'png' });
    const buf = Buffer.from(data, 'base64');
    return {
      buf,
      width: buf.readUInt32BE(16),
      height: buf.readUInt32BE(20),
    };
  } finally {
    await cdp.detach().catch(() => {});
  }
}

async function capturePureCdpPng(page, fileName) {
  const { buf } = await capturePureCdpBuffer(page);
  return safeWritePng(path.join(SCREENSHOT_DIR, fileName), buf);
}

async function stitchTwoPngBuffersSideBySide(browser, leftBuf, rightBuf, fileName, gapPx = 16) {
  const leftW = leftBuf.readUInt32BE(16);
  const leftH = leftBuf.readUInt32BE(20);
  const rightW = rightBuf.readUInt32BE(16);
  const rightH = rightBuf.readUInt32BE(20);
  const totalW = leftW + gapPx + rightW;
  const totalH = Math.max(leftH, rightH);

  const auxCtx = await browser.newContext({
    viewport: { width: Math.min(totalW, 3400), height: Math.min(totalH, 2200) },
    deviceScaleFactor: 1,
  });
  const auxPage = await auxCtx.newPage();
  try {
    await auxPage.goto('about:blank');
    const dataUrl = await auxPage.evaluate(
      async ({ lB64, rB64, lW, lH, rW, rH, gPx, tW, tH }) => {
        const loadImg = (b64) =>
          new Promise((resolve, reject) => {
            const img = new Image();
            img.onload = () => resolve(img);
            img.onerror = reject;
            img.src = `data:image/png;base64,${b64}`;
          });
        const [imgL, imgR] = await Promise.all([loadImg(lB64), loadImg(rB64)]);
        const canvas = document.createElement('canvas');
        canvas.width = tW;
        canvas.height = tH;
        const ctx = canvas.getContext('2d');
        ctx.fillStyle = '#0f172a';
        ctx.fillRect(0, 0, tW, tH);
        ctx.drawImage(imgL, 0, 0, lW, lH);
        ctx.drawImage(imgR, lW + gPx, 0, rW, rH);
        return canvas.toDataURL('image/png');
      },
      {
        lB64: leftBuf.toString('base64'),
        rB64: rightBuf.toString('base64'),
        lW: leftW,
        lH: leftH,
        rW: rightW,
        rH: rightH,
        gPx: gapPx,
        tW: totalW,
        tH: totalH,
      },
    );
    const pngBase64 = dataUrl.replace(/^data:image\/png;base64,/, '');
    const stitchedBuf = Buffer.from(pngBase64, 'base64');
    return safeWritePng(path.join(SCREENSHOT_DIR, fileName), stitchedBuf);
  } finally {
    await auxCtx.close().catch(() => {});
  }
}

async function measureSortHeadersOnPage(page, route, viewportLabel) {
  return page.evaluate(
    ({ rt, vp }) => {
      const sortThs = Array.from(document.querySelectorAll('th.ui-sort-th'));
      const headers = [];
      let collisionCount = 0;
      for (const th of sortThs) {
        const cs = window.getComputedStyle(th);
        if (cs.display === 'none' || cs.visibility === 'hidden') continue;
        const padRight = parseFloat(cs.paddingRight || '0');
        const btn = th.querySelector('button.ui-sort-th-btn');
        const btnCs = btn ? window.getComputedStyle(btn) : null;
        const gapPx = btnCs ? parseFloat(btnCs.columnGap || btnCs.gap || '0') : 0;
        const spans = btn ? Array.from(btn.querySelectorAll(':scope > span')) : [];
        let labelToIconGapPx = gapPx;
        if (spans.length >= 2) {
          const lRect = spans[0].getBoundingClientRect();
          const iRect = spans[1].getBoundingClientRect();
          labelToIconGapPx = Number((iRect.left - lRect.right).toFixed(1));
          if (labelToIconGapPx < 2) collisionCount += 1;
        }
        headers.push({
          label: (spans[0]?.textContent || th.textContent || '').trim(),
          paddingRightPx: padRight,
          gapPx,
          labelToIconGapPx,
        });
      }
      return {
        route: rt,
        viewport: vp,
        sortHeaderCount: headers.length,
        minPaddingRightPx: headers.length ? Math.min(...headers.map((h) => h.paddingRightPx)) : 0,
        minGapPx: headers.length ? Math.min(...headers.map((h) => h.gapPx)) : 0,
        collisionCount,
        headers,
      };
    },
    { rt: route, vp: viewportLabel },
  );
}

async function fetchAxeMinSource() {
  const res = await fetch('https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js');
  if (!res.ok) {
    throw new Error(`Failed to fetch axe-core 4.10.2 from CDN: HTTP ${res.status}`);
  }
  return await res.text();
}

async function collectViewportHygiene(page, routeLabel) {
  return page.evaluate((rLabel) => {
    const docEl = document.documentElement;
    const bodyText = document.body?.innerText || '';
    const imgs = Array.from(document.querySelectorAll('img'));
    const brokenImages = imgs.filter((img) => !img.complete || img.naturalWidth === 0).length;
    const emojiMatches = bodyText.match(/[\u{1F300}-\u{1FAFF}]/gu) || [];
    return {
      route: rLabel,
      innerWidth: window.innerWidth,
      innerHeight: window.innerHeight,
      scrollWidth: docEl.scrollWidth,
      clientWidth: docEl.clientWidth,
      horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      brokenImageCount: brokenImages,
      emojiCount: emojiMatches.length,
      hasReplacementChar: bodyText.includes('\uFFFD'),
      hasUndefinedOrNaN: /\b(?:undefined|NaN)\b/.test(bodyText),
    };
  }, routeLabel);
}

async function auditIconOnlyButtons(page, contextName) {
  return page.evaluate((ctxName) => {
    const candidates = Array.from(
      document.querySelectorAll('button, summary, a.sidebar-nav-link, [role="button"]'),
    );
    const missing = [];
    let checkedCount = 0;
    for (const el of candidates) {
      const cs = window.getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden' || Number(cs.opacity) === 0) continue;
      const rect = el.getBoundingClientRect();
      if (rect.width <= 1 || rect.height <= 1) continue;
      const visibleText = (el.innerText || '').replace(/[\u2715×✕]/g, '').trim();
      if (visibleText.length > 0) continue;
      checkedCount += 1;
      const ariaLabel = (el.getAttribute('aria-label') || '').trim();
      const ariaLabelledBy = (el.getAttribute('aria-labelledby') || '').trim();
      if (!ariaLabel && !ariaLabelledBy) {
        missing.push({
          context: ctxName,
          tagName: el.tagName,
          testId: el.getAttribute('data-testid') || null,
          className: String(el.className || '').slice(0, 80),
        });
      }
    }
    return { context: ctxName, iconOnlyControlsCount: checkedCount, missingAriaLabelCount: missing.length, missing };
  }, contextName);
}

async function measureVisibleTablesAtScrollZero(page, route, viewportLabel) {
  return page.evaluate(
    ({ rt, vp }) => {
      for (const sc of Array.from(document.querySelectorAll('.ui-table-scroll'))) {
        if (sc.scrollLeft !== 0) {
          sc.scrollLeft = 0;
          sc.dispatchEvent(new Event('scroll'));
        }
      }
      const results = [];
      const tables = Array.from(document.querySelectorAll('table'));
      for (let idx = 0; idx < tables.length; idx += 1) {
        const tbl = tables[idx];
        const cs = window.getComputedStyle(tbl);
        if (cs.display === 'none' || cs.visibility === 'hidden') continue;
        const tblRect = tbl.getBoundingClientRect();
        if (tblRect.width <= 1 || tblRect.height <= 1) continue;
        const uiScrollWrap = tbl.closest('.ui-table-scroll');
        const container = uiScrollWrap || tbl.parentElement || tbl;
        const cRect = container.getBoundingClientRect();
        const containerClientWidth = Math.round(container.clientWidth);
        const tableScrollWidth = Math.round(Math.max(container.scrollWidth, tbl.scrollWidth));
        const hasOverflow = tableScrollWidth > containerClientWidth + 2;
        const hasUiOverflowClass = Boolean(
          uiScrollWrap &&
            (uiScrollWrap.classList.contains('ui-table-scroll--has-overflow') ||
              uiScrollWrap.getAttribute('data-has-overflow') === 'true'),
        );
        const ths = Array.from(tbl.querySelectorAll('thead th'));
        const stickyActionTh = tbl.querySelector('thead th.ui-sticky-action');
        let stickyActionRightOverflowPx = 0;
        let stickyActionAdjacentOverlapPx = 0;
        let stickyActionWidthPx = 0;
        let stickyRightWidthRatio = 0;
        let actRect = null;
        if (stickyActionTh) {
          actRect = stickyActionTh.getBoundingClientRect();
          stickyActionWidthPx = Math.round(actRect.width);
          stickyRightWidthRatio =
            containerClientWidth > 0 ? Number((actRect.width / containerClientWidth).toFixed(4)) : 0;
          stickyActionRightOverflowPx = Math.max(0, Math.round(actRect.right - cRect.right - 2));
          const actionIdx = ths.indexOf(stickyActionTh);
          const prevTh = actionIdx > 0 ? ths[actionIdx - 1] : null;
          if (prevTh && !hasOverflow) {
            const prevRect = prevTh.getBoundingClientRect();
            stickyActionAdjacentOverlapPx = Math.max(0, Math.round(prevRect.right - actRect.left - 1));
          }
        }

        const statusColIdx = ths.findIndex((th) => (th.textContent || '').trim().includes('Trạng thái'));
        let hasStatusColumn = statusColIdx >= 0;
        let statusColIndex1Based = statusColIdx >= 0 ? statusColIdx + 1 : null;
        let statusVisibleAtScroll0 = true;
        if (statusColIdx >= 0) {
          const statusTh = ths[statusColIdx];
          const sThRect = statusTh.getBoundingClientRect();
          const rightBoundary =
            actRect && stickyActionTh !== statusTh && hasOverflow ? actRect.left : cRect.right;
          const thVisible =
            sThRect.width > 10 &&
            sThRect.left >= cRect.left - 2 &&
            sThRect.right <= rightBoundary + 2;
          const firstRow = tbl.querySelector('tbody tr:not(.permission-module-group-row)');
          let tdVisible = true;
          if (firstRow) {
            const tds = Array.from(firstRow.querySelectorAll(':scope > td'));
            const statusTd = tds[statusColIdx];
            if (statusTd) {
              const sTdRect = statusTd.getBoundingClientRect();
              tdVisible =
                sTdRect.width > 10 &&
                sTdRect.left >= cRect.left - 2 &&
                sTdRect.right <= rightBoundary + 2;
            }
          }
          statusVisibleAtScroll0 = Boolean(thVisible && tdVisible);
        }

        // Check warehouse cells ("Kho Trung tâm (WH01)") for horizontal text clipping/ellipsis
        const whCells = Array.from(tbl.querySelectorAll('tbody td')).filter((td) => {
          const t = td.innerText || '';
          return t.includes('Kho Trung tâm') || t.includes('WH01');
        });
        let clippedWarehouseCellsCount = 0;
        for (const td of whCells) {
          const checkNodes = [td, ...Array.from(td.querySelectorAll('div, span, strong'))];
          const isClipped = checkNodes.some((n) => {
            const ncs = window.getComputedStyle(n);
            const t = (n.textContent || '').trim();
            if (!t.includes('Kho Trung tâm') && !t.includes('WH01')) return false;
            return (
              (ncs.overflowX === 'hidden' || ncs.textOverflow === 'ellipsis') &&
              n.scrollWidth > n.clientWidth + 2
            );
          });
          if (isClipped) clippedWarehouseCellsCount += 1;
        }

        results.push({
          route: rt,
          viewport: vp,
          tableId: tbl.getAttribute('data-testid') || tbl.getAttribute('aria-label') || `table-${idx + 1}`,
          scrollLeft: container.scrollLeft,
          containerClientWidth,
          tableScrollWidth,
          hasOverflow,
          hasUiOverflowClass,
          unindicatedOverflow: Boolean(hasOverflow && !hasUiOverflowClass),
          stickyActionWidthPx,
          stickyRightWidthRatio,
          stickyActionRightOverflowPx,
          stickyActionAdjacentOverlapPx,
          hasStatusColumn,
          statusColIndex1Based,
          statusVisibleAtScroll0,
          clippedWarehouseCellsCount,
        });
      }
      return results;
    },
    { rt: route, vp: viewportLabel },
  );
}

const FORBIDDEN_RAW_ENUM_PATTERNS = [
  { token: 'PENDING_INSPECTION', regex: /\bPENDING_INSPECTION\b/ },
  { token: 'QUARANTINED', regex: /\bQUARANTINED\b/ },
  { token: 'REPACK_REQUIRED', regex: /\bREPACK_REQUIRED\b/ },
  { token: 'DAMAGED_BOX', regex: /\bDAMAGED_BOX\b/ },
  { token: 'RECEIVING', regex: /\bRECEIVING\b/ },
  { token: 'RESTOCK_AVAILABLE', regex: /\bRESTOCK_AVAILABLE\b/ },
  { token: 'REFURBISH_REPACK', regex: /\bREFURBISH_REPACK\b/ },
  { token: 'RTV_VENDOR', regex: /\bRTV_VENDOR\b/ },
  { token: 'SCRAP_DISPOSAL', regex: /\bSCRAP_DISPOSAL\b/ },
  { token: 'ENTERPRISE_DASH', regex: /ENTERPRISE\s*[—–-]/ },
  { token: 'Express Post', regex: /Express\s+Post/i },
  { token: 'du_lieu_cu', regex: /dữ\s+liệu\s+cũ/i },
];

async function scanDomForRawEnglishEnums(page, contextLabel) {
  const visibleTexts = await page.evaluate(() => {
    const bodyText = document.body?.innerText || '';
    const badgeTexts = Array.from(document.querySelectorAll('.ui-badge, [role="status"], button, th, td, label, h1, h2, h3'))
      .filter((el) => {
        const cs = window.getComputedStyle(el);
        return cs.display !== 'none' && cs.visibility !== 'hidden';
      })
      .map((el) => (el.innerText || '').trim())
      .filter(Boolean);
    const selectedOptions = Array.from(document.querySelectorAll('select'))
      .filter((sel) => {
        const cs = window.getComputedStyle(sel);
        return cs.display !== 'none' && cs.visibility !== 'hidden';
      })
      .map((sel) => sel.selectedOptions?.[0]?.textContent?.trim() || '')
      .filter(Boolean);
    return { bodyText, sampleSurfaceCount: badgeTexts.length, selectedOptions };
  });

  const combined = `${visibleTexts.bodyText}\n${visibleTexts.selectedOptions.join('\n')}`;
  const hits = [];
  for (const pat of FORBIDDEN_RAW_ENUM_PATTERNS) {
    if (pat.regex.test(combined)) {
      hits.push({ context: contextLabel, token: pat.token });
    }
  }
  return {
    context: contextLabel,
    hitCount: hits.length,
    hits,
  };
}

const CUSTOMER_46_SWEEP_ROUTES = [
  '/',
  '/dock-yard',
  '/purchase-orders',
  '/asns',
  '/import-receipts',
  '/putaway-tasks',
  '/sales-orders',
  '/backorders',
  '/stock-reservations',
  '/stock-allocations',
  '/picking-tasks',
  '/packing-sessions',
  '/export-receipts',
  '/shipments',
  '/handover-manifests',
  '/customer-returns',
  '/inventory',
  '/inventory-locks',
  '/inventory-movements',
  '/stock-transfers',
  '/stocktakes',
  '/inventory-reversals',
  '/inventory-reconciliation',
  '/products',
  '/categories',
  '/units',
  '/kit-work-orders',
  '/inventory-traceability',
  '/business-partners',
  '/cod-reconciliation',
  '/cost-valuation',
  '/profit-report',
  '/misa-exports',
  '/partner-debts',
  '/warehouses',
  '/warehouse-structure',
  '/warehouse-map',
  '/warehouse-calendar',
  '/approvals',
  '/users',
  '/roles',
  '/warehouse-scopes',
  '/account-security',
  '/saas-billing',
  '/onboarding-wizard',
  '/permissions',
];

const PLATFORM_8_SWEEP_ROUTES = [
  '/platform',
  '/platform/tenants',
  '/platform/billing',
  '/platform/quotas',
  '/platform/integrations',
  '/platform/monitoring',
  '/platform/support',
  '/platform/audit-logs',
];

async function evaluateTruePixelAndGeometryOnPage(page, contextLabel) {
  return page.evaluate((ctxLbl) => {
    for (const sc of Array.from(document.querySelectorAll('.ui-table-scroll'))) {
      if (sc.scrollLeft !== 0) {
        sc.scrollLeft = 0;
        sc.dispatchEvent(new Event('scroll'));
      }
    }

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');

    const isEffectivelyVisible = (node) => {
      let cur = node;
      while (cur && cur instanceof HTMLElement) {
        if (cur.getAttribute('aria-hidden') === 'true') return false;
        const c = window.getComputedStyle(cur);
        if (
          c.display === 'none' ||
          c.visibility === 'hidden' ||
          Number(c.opacity) === 0 ||
          c.clip === 'rect(0px, 0px, 0px, 0px)' ||
          (cur.clientWidth <= 2 && cur.clientHeight <= 2 && (c.overflow === 'hidden' || c.position === 'absolute'))
        ) {
          return false;
        }
        cur = cur.parentElement;
      }
      return true;
    };

    const isInsideBoundedHorizontalScroll = (node) => {
      let cur = node.parentElement;
      while (cur && cur !== document.body && cur !== document.documentElement) {
        const pCs = window.getComputedStyle(cur);
        if (pCs.overflowX === 'auto' || pCs.overflowX === 'scroll') {
          const pRect = cur.getBoundingClientRect();
          if (pRect.right <= window.innerWidth + 1) return true;
        }
        cur = cur.parentElement;
      }
      return false;
    };

    const truncatedItems = [];
    const selector =
      'h1, h2, h3, h4, .ui-metric *, .ui-card-title, label, th, .ui-badge, button, select, input[placeholder]';
    const candidates = Array.from(document.querySelectorAll(selector));

    for (const el of candidates) {
      if (!(el instanceof HTMLElement)) continue;
      if (el.classList.contains('skip-link')) continue;
      if (el.getClientRects().length === 0) continue;
      const rect = el.getBoundingClientRect();
      if (rect.width <= 0 || rect.height <= 0 || rect.right <= 0 || rect.bottom <= 0) continue;
      if (!isEffectivelyVisible(el)) continue;
      const cs = window.getComputedStyle(el);

      const tag = el.tagName;
      const insideBoundedScroll = Boolean(el.closest('.ui-table-scroll')) || isInsideBoundedHorizontalScroll(el);
      if (!insideBoundedScroll && rect.right > window.innerWidth + 1) {
        truncatedItems.push({
          context: ctxLbl,
          kind: 'element-bleeds-past-viewport-right',
          tag,
          testId: el.getAttribute('data-testid') || el.getAttribute('aria-label') || null,
          text: (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80),
          rectRightPx: Math.round(rect.right * 10) / 10,
          viewportWidthPx: window.innerWidth,
        });
        continue;
      }

      const metricCard = el.closest('.ui-metric');
      if (metricCard) {
        const mRect = metricCard.getBoundingClientRect();
        if (rect.right > mRect.right + 1) {
          truncatedItems.push({
            context: ctxLbl,
            kind: 'metric-card-text-bleed',
            tag,
            text: (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80),
            rectRightPx: Math.round(rect.right * 10) / 10,
            cardRightPx: Math.round(mRect.right * 10) / 10,
          });
          continue;
        }
      }

      if (tag === 'SELECT' && el instanceof HTMLSelectElement) {
        const optText = (el.selectedOptions?.[0]?.textContent || '').replace(/\s+/g, ' ').trim();
        if (optText && ctx) {
          ctx.font = `${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`;
          const measured = ctx.measureText(optText).width;
          const padL = parseFloat(cs.paddingLeft || '0') || 0;
          const padR = parseFloat(cs.paddingRight || '0') || 0;
          const avail = rect.width - padL - padR - 20;
          if (measured > avail) {
            truncatedItems.push({
              context: ctxLbl,
              kind: 'select-option-clipped',
              tag,
              testId: el.getAttribute('data-testid') || el.getAttribute('aria-label') || null,
              text: optText.slice(0, 80),
              measuredWidthPx: Math.round(measured * 10) / 10,
              availableWidthPx: Math.round(avail * 10) / 10,
            });
          }
        }
        continue;
      }

      if (tag === 'INPUT' && el instanceof HTMLInputElement) {
        if (el.value === '') {
          const ph = (el.getAttribute('placeholder') || '').trim();
          if (ph && ctx) {
            ctx.font = `${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`;
            const measuredPh = ctx.measureText(ph).width;
            const padL = parseFloat(cs.paddingLeft || '0') || 0;
            const padR = parseFloat(cs.paddingRight || '0') || 0;
            const avail = rect.width - padL - padR + 2;
            if (measuredPh > avail) {
              truncatedItems.push({
                context: ctxLbl,
                kind: 'input-placeholder-clipped',
                tag,
                testId: el.getAttribute('data-testid') || el.getAttribute('aria-label') || null,
                placeholder: ph.slice(0, 80),
                measuredWidthPx: Math.round(measuredPh * 10) / 10,
                availableWidthPx: Math.round(avail * 10) / 10,
              });
            }
          }
        }
        continue;
      }

      const hasClippedCss =
        cs.overflowX === 'hidden' || cs.overflow === 'hidden' || cs.textOverflow === 'ellipsis';
      if (hasClippedCss && el.scrollWidth > el.clientWidth + 1) {
        truncatedItems.push({
          context: ctxLbl,
          kind: 'element-overflow-ellipsis',
          tag,
          testId: el.getAttribute('data-testid') || null,
          className: String(el.className || '').slice(0, 60),
          text: (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80),
          scrollWidth: el.scrollWidth,
          clientWidth: el.clientWidth,
        });
      }
    }

    const crushedHeaders = [];
    const stickyOverlaps = [];
    if (window.innerWidth >= 768) {
      const tables = Array.from(document.querySelectorAll('table'));
      for (let tIdx = 0; tIdx < tables.length; tIdx += 1) {
        const tbl = tables[tIdx];
        if (!isEffectivelyVisible(tbl)) continue;
        const tblRect = tbl.getBoundingClientRect();
        if (tblRect.width <= 1 || tblRect.height <= 1) continue;
        const tableId = tbl.getAttribute('data-testid') || tbl.getAttribute('aria-label') || `table-${tIdx + 1}`;

        const visibleThs = Array.from(tbl.querySelectorAll('thead th')).filter((th) => {
          const thCs = window.getComputedStyle(th);
          if (thCs.display === 'none' || thCs.visibility === 'hidden') return false;
          const thRect = th.getBoundingClientRect();
          return thRect.width > 0 && thRect.height > 0;
        });
        for (let hIdx = 0; hIdx < visibleThs.length; hIdx += 1) {
          const th = visibleThs[hIdx];
          const thText = (th.innerText || th.textContent || '').replace(/\s+/g, ' ').trim();
          if (!thText) continue;
          const thRect = th.getBoundingClientRect();
          const nextTh = visibleThs[hIdx + 1] || null;
          let bleedsIntoNextTh = false;
          if (nextTh) {
            const nextRect = nextTh.getBoundingClientRect();
            const hRange = document.createRange();
            hRange.selectNodeContents(th);
            const hContentRect = hRange.getBoundingClientRect();
            if (nextRect.left > thRect.left && hContentRect.right > nextRect.left + 1) {
              bleedsIntoNextTh = true;
            }
          }
          if (th.clientWidth < 72 || thRect.height > 56 || bleedsIntoNextTh) {
            crushedHeaders.push({
              context: ctxLbl,
              tableId,
              headerText: thText,
              clientWidthPx: th.clientWidth,
              rectHeightPx: Math.round(thRect.height * 10) / 10,
              bleedsIntoNextTh,
            });
          }
        }

        const allRows = Array.from(tbl.querySelectorAll('thead tr, tbody tr'));
        for (let rIdx = 0; rIdx < allRows.length; rIdx += 1) {
          const tr = allRows[rIdx];
          if (tr.classList.contains('permission-module-group-row')) continue;
          const cells = Array.from(tr.querySelectorAll(':scope > th, :scope > td'));
          const stickyRightCells = cells.filter((c) => {
            const cCs = window.getComputedStyle(c);
            if (cCs.display === 'none' || cCs.visibility === 'hidden' || cCs.position !== 'sticky') return false;
            const rightPx = parseFloat(cCs.right);
            if (!Number.isFinite(rightPx) || rightPx < 0) return false;
            const cRect = c.getBoundingClientRect();
            return cRect.width > 0 && cRect.left > tblRect.left + 80;
          });
          if (stickyRightCells.length === 0) continue;
          const stickyCell = stickyRightCells[0];
          const stickyRect = stickyCell.getBoundingClientRect();

          for (const cell of cells) {
            if (cell === stickyCell || stickyRightCells.includes(cell)) continue;
            if (cell.tagName !== 'TD' && cell.tagName !== 'TH') continue;
            const cCs = window.getComputedStyle(cell);
            if (cCs.display === 'none' || cCs.visibility === 'hidden') continue;
            const cellRect = cell.getBoundingClientRect();
            if (cellRect.width <= 0 || cellRect.left >= stickyRect.left - 1) continue;
            const range = document.createRange();
            range.selectNodeContents(cell);
            const contentRect = range.getBoundingClientRect();
            if (contentRect.width > 0 && contentRect.right > stickyRect.left + 1) {
              stickyOverlaps.push({
                context: ctxLbl,
                tableId,
                rowIndex: rIdx,
                tag: cell.tagName,
                cellText: (cell.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 60),
                contentRightPx: Math.round(contentRect.right * 10) / 10,
                stickyLeftPx: Math.round(stickyRect.left * 10) / 10,
                overlapPx: Math.round((contentRect.right - stickyRect.left) * 10) / 10,
              });
            }
          }
        }
      }
    }

    const bodyText = document.body?.innerText || '';
    const techRegex =
      /Authentication\.LoginSucceeded|UserSession#|ERP_KHO|PENDING_INSPECTION|REPACK_REQUIRED|Express Post|dữ liệu cũ|\b\d+\s*ĐVT\b|\b\d+\/10\s*ký tự tối thiểu/gi;
    const rawTechMatches = Array.from(bodyText.matchAll(techRegex)).map((m) => m[0]);
    const docEl = document.documentElement;

    return {
      context: ctxLbl,
      viewport: `${window.innerWidth}x${window.innerHeight}`,
      horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      truncatedElementsCount: truncatedItems.length,
      truncatedItems: truncatedItems.slice(0, 15),
      crushedTableHeaderCount: crushedHeaders.length,
      crushedHeaders: crushedHeaders.slice(0, 15),
      stickyColumnOverlapCount: stickyOverlaps.length,
      stickyOverlaps: stickyOverlaps.slice(0, 15),
      rawTechnicalStringsCount: rawTechMatches.length,
      rawTechMatches: rawTechMatches.slice(0, 15),
    };
  }, contextLabel);
}

async function runFull54RouteTruePixelGeometrySweep(browser, ownerAuth, tenantAdminAuth) {
  const sweepCtx = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });
  await wireStaleOrigins(sweepCtx);
  const sweepPage = await sweepCtx.newPage();
  const routeResults = [];
  let currentConsoleErrors = [];
  let currentPageErrors = [];
  let currentHttpErrors = [];

  sweepPage.on('console', (msg) => {
    if (msg.type() === 'error') {
      currentConsoleErrors.push(msg.text().slice(0, 200));
    }
  });
  sweepPage.on('pageerror', (err) => {
    currentPageErrors.push(String(err?.message || err).slice(0, 200));
  });
  sweepPage.on('response', (res) => {
    const status = res.status();
    const url = res.url();
    if (status >= 400 && url.includes('/api/v1/')) {
      currentHttpErrors.push({ status, url });
    }
  });

  const visitAndProbe = async (portal, actor, rt) => {
    currentConsoleErrors = [];
    currentPageErrors = [];
    currentHttpErrors = [];
    await sweepPage.goto(`${BASE_WEB}${rt}`, { waitUntil: 'networkidle' });
    await sweepPage.waitForTimeout(160);
    const geo = await evaluateTruePixelAndGeometryOnPage(sweepPage, `SWEEP:${portal}:${rt}@1440x900`);
    routeResults.push({
      portal,
      actor,
      route: rt,
      ...geo,
      consoleErrorsCount: currentConsoleErrors.length,
      consoleErrors: currentConsoleErrors,
      pageErrorsCount: currentPageErrors.length,
      pageErrors: currentPageErrors,
      httpErrorsCount: currentHttpErrors.length,
      httpErrors: currentHttpErrors,
    });
  };

  try {
    await sweepPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await applyAuthTokenInBrowser(sweepPage, tenantAdminAuth.token, tenantAdminAuth.user, true);
    for (const rt of CUSTOMER_46_SWEEP_ROUTES) {
      await visitAndProbe('CUSTOMER_WMS', 'tenant_admin', rt);
    }

    await sweepPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await sweepPage.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await applyAuthTokenInBrowser(sweepPage, ownerAuth.token, ownerAuth.user, true);
    for (const rt of PLATFORM_8_SWEEP_ROUTES) {
      await visitAndProbe('PLATFORM_CONTROL_CENTER', 'qa_admin', rt);
    }
  } finally {
    await sweepCtx.close().catch(() => {});
  }

  const failingRoutes = routeResults.filter(
    (r) =>
      r.horizontalOverflowPx > 0 ||
      r.truncatedElementsCount > 0 ||
      r.crushedTableHeaderCount > 0 ||
      r.stickyColumnOverlapCount > 0 ||
      r.rawTechnicalStringsCount > 0 ||
      r.consoleErrorsCount > 0 ||
      r.pageErrorsCount > 0 ||
      r.httpErrorsCount > 0,
  );

  return {
    sweptCustomerRoutesCount: routeResults.filter((r) => r.portal === 'CUSTOMER_WMS').length,
    sweptPlatformRoutesCount: routeResults.filter((r) => r.portal === 'PLATFORM_CONTROL_CENTER').length,
    sweptRoutesTotal: routeResults.length,
    routesWithFailuresCount: failingRoutes.length,
    sweepTruncatedCount: routeResults.reduce((s, r) => s + r.truncatedElementsCount, 0),
    sweepCrushedHeaderCount: routeResults.reduce((s, r) => s + r.crushedTableHeaderCount, 0),
    sweepStickyOverlapCount: routeResults.reduce((s, r) => s + r.stickyColumnOverlapCount, 0),
    sweepRawTechCount: routeResults.reduce((s, r) => s + r.rawTechnicalStringsCount, 0),
    sweepMaxOverflowPx: Math.max(0, ...routeResults.map((r) => r.horizontalOverflowPx)),
    sweepConsoleErrorsCount: routeResults.reduce((s, r) => s + r.consoleErrorsCount, 0),
    sweepPageErrorsCount: routeResults.reduce((s, r) => s + r.pageErrorsCount, 0),
    sweepHttpErrorsCount: routeResults.reduce((s, r) => s + r.httpErrorsCount, 0),
    failingRoutes,
    routeResults,
  };
}

async function measureTouchTargetHeightsAtOrBelow1024(page, contextLabel) {
  return page.evaluate((ctxLbl) => {
    const selectors = [
      'main .ui-btn',
      'main .ui-btn-primary',
      'main .ui-btn-secondary',
      'main .ui-mobile-card button',
      'main .ui-mobile-card summary',
      '[data-testid="scanner-confirm-btn"]',
      '[data-testid="scanner-undo-last-btn"]',
      '[data-testid="pda-bottom-nav"] a',
      '[data-testid="pda-bottom-nav"] button',
    ];
    const seen = new Set();
    const heights = [];
    const violations = [];
    for (const sel of selectors) {
      for (const el of Array.from(document.querySelectorAll(sel))) {
        if (seen.has(el)) continue;
        seen.add(el);
        const cs = window.getComputedStyle(el);
        if (cs.display === 'none' || cs.visibility === 'hidden' || Number(cs.opacity) === 0) continue;
        const r = el.getBoundingClientRect();
        if (r.width <= 1 || r.height <= 1) continue;
        const h = Math.round(r.height);
        heights.push(h);
        if (h < 44) {
          violations.push({
            context: ctxLbl,
            tag: el.tagName,
            testId: el.getAttribute('data-testid') || null,
            text: (el.innerText || el.getAttribute('aria-label') || '').trim().slice(0, 40),
            heightPx: h,
          });
        }
      }
    }
    return {
      context: ctxLbl,
      innerWidth: window.innerWidth,
      checkedCount: heights.length,
      minHeightPx: heights.length ? Math.min(...heights) : null,
      violations,
    };
  }, contextLabel);
}

async function runWave19AccessibilityAndMotionSuite(browser, ownerAuth, tenantAdminAuth, addFinding, addCheck) {
  const a11yCtx = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });
  await wireStaleOrigins(a11yCtx);
  const a11yPage = await a11yCtx.newPage();

  let skipLinkCustomer = { ok: false };
  let skipLinkPlatform = { ok: false };
  let reconcileModalTrap = { ok: false };
  let kitReversalModalTrap = { ok: false };
  let reducedMotionCheck = { ok: false, maxDurationSec: 999 };

  try {
    // 1A. Skip-link on First Tab — Platform (/platform)
    await a11yPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await applyAuthTokenInBrowser(a11yPage, ownerAuth.token, ownerAuth.user, true);
    await a11yPage.goto(`${BASE_WEB}/platform`, { waitUntil: 'networkidle' });
    await a11yPage.getByTestId('platform-control-center-page').waitFor({ state: 'visible', timeout: 15000 });
    await a11yPage.evaluate(() => {
      if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
      document.body.focus();
    });
    await a11yPage.keyboard.press('Tab');
    await a11yPage.waitForTimeout(80);
    skipLinkPlatform = await a11yPage.evaluate(() => {
      const el = document.activeElement;
      if (!(el instanceof HTMLElement)) return { ok: false };
      const cs = window.getComputedStyle(el);
      const r = el.getBoundingClientRect();
      const hasSkipClass = el.classList.contains('skip-link');
      const href = el.getAttribute('href') || '';
      const targetId = href.startsWith('#') ? href.slice(1) : '';
      const targetExists = Boolean(targetId && document.getElementById(targetId));
      const visibleWhenFocused = r.width >= 40 && r.height >= 24 && r.top >= -2 && r.left >= -2;
      const hasFocusRing = cs.outlineStyle !== 'none' || cs.boxShadow !== 'none';
      return {
        ok: Boolean(hasSkipClass && targetExists && visibleWhenFocused && hasFocusRing),
        className: el.className,
        href,
        targetExists,
        rect: { top: Math.round(r.top), left: Math.round(r.left), width: Math.round(r.width), height: Math.round(r.height) },
        outline: `${cs.outlineWidth} ${cs.outlineStyle} ${cs.outlineColor}`,
      };
    });

    // 1B. Modal Focus Trap + Escape Return — PlatformReconcileInvoiceModal (/platform/billing)
    await a11yPage.goto(`${BASE_WEB}/platform/billing`, { waitUntil: 'networkidle' });
    await a11yPage.getByTestId('platform-billing-invoices-table').waitFor({ state: 'visible', timeout: 15000 });
    const triggerReconcile = a11yPage
      .locator('tr', { hasText: 'INV-SAAS-202611-002' })
      .locator('[data-testid^="btn-reconcile-saas-invoice-"]')
      .first();
    const triggerReconcileTestId = await triggerReconcile.getAttribute('data-testid');
    await triggerReconcile.focus();
    await triggerReconcile.click();
    const recModal = a11yPage.getByTestId('modal-confirm-invoice-reconcile');
    await recModal.waitFor({ state: 'visible', timeout: 10000 });
    await a11yPage.waitForTimeout(120);

    const recTrapSteps = [];
    for (let i = 0; i < 10; i += 1) {
      await a11yPage.keyboard.press('Tab');
      const inside = await a11yPage.evaluate(() => {
        const m = document.querySelector('[data-testid="modal-confirm-invoice-reconcile"]');
        return Boolean(m && document.activeElement && m.contains(document.activeElement));
      });
      recTrapSteps.push(inside);
    }
    for (let i = 0; i < 3; i += 1) {
      await a11yPage.keyboard.press('Shift+Tab');
      const inside = await a11yPage.evaluate(() => {
        const m = document.querySelector('[data-testid="modal-confirm-invoice-reconcile"]');
        return Boolean(m && document.activeElement && m.contains(document.activeElement));
      });
      recTrapSteps.push(inside);
    }
    await a11yPage.keyboard.press('Escape');
    await recModal.waitFor({ state: 'hidden', timeout: 5000 });
    await a11yPage.waitForTimeout(80);
    const recRestoredTestId = await a11yPage.evaluate(
      () => document.activeElement?.getAttribute('data-testid') || '',
    );
    reconcileModalTrap = {
      ok: recTrapSteps.every(Boolean) && Boolean(triggerReconcileTestId) && recRestoredTestId === triggerReconcileTestId,
      allTabStepsInsideModal: recTrapSteps.every(Boolean),
      triggerTestId: triggerReconcileTestId,
      restoredTestId: recRestoredTestId,
    };

    // 2A. Skip-link on First Tab — Customer (/sales-orders)
    await a11yPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await a11yPage.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await applyAuthTokenInBrowser(a11yPage, tenantAdminAuth.token, tenantAdminAuth.user, true);
    await a11yPage.goto(`${BASE_WEB}/sales-orders`, { waitUntil: 'networkidle' });
    await a11yPage.getByTestId('sales-orders-table').waitFor({ state: 'visible', timeout: 15000 });
    await a11yPage.evaluate(() => {
      if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
      document.body.focus();
    });
    await a11yPage.keyboard.press('Tab');
    await a11yPage.waitForTimeout(80);
    skipLinkCustomer = await a11yPage.evaluate(() => {
      const el = document.activeElement;
      if (!(el instanceof HTMLElement)) return { ok: false };
      const cs = window.getComputedStyle(el);
      const r = el.getBoundingClientRect();
      const hasSkipClass = el.classList.contains('skip-link');
      const href = el.getAttribute('href') || '';
      const targetId = href.startsWith('#') ? href.slice(1) : '';
      const targetExists = Boolean(targetId && document.getElementById(targetId));
      const visibleWhenFocused = r.width >= 40 && r.height >= 24 && r.top >= -2 && r.left >= -2;
      const hasFocusRing = cs.outlineStyle !== 'none' || cs.boxShadow !== 'none';
      return {
        ok: Boolean(hasSkipClass && targetExists && visibleWhenFocused && hasFocusRing),
        className: el.className,
        href,
        targetExists,
        rect: { top: Math.round(r.top), left: Math.round(r.left), width: Math.round(r.width), height: Math.round(r.height) },
        outline: `${cs.outlineWidth} ${cs.outlineStyle} ${cs.outlineColor}`,
      };
    });

    // 2B. Modal Focus Trap + Escape Return — KitWorkOrderReversalModal (/kit-work-orders)
    await a11yPage.goto(`${BASE_WEB}/kit-work-orders`, { waitUntil: 'networkidle' });
    await a11yPage.getByTestId('kit-work-orders-table').waitFor({ state: 'visible', timeout: 15000 });
    const kitTrigger = a11yPage.getByTestId('btn-reverse-kit-KWO-2026-001');
    await kitTrigger.focus();
    await kitTrigger.click();
    const kitModal = a11yPage.getByTestId('modal-confirm-kit-reversal');
    await kitModal.waitFor({ state: 'visible', timeout: 10000 });
    await a11yPage.waitForTimeout(120);

    const kitTrapSteps = [];
    for (let i = 0; i < 8; i += 1) {
      await a11yPage.keyboard.press('Tab');
      const inside = await a11yPage.evaluate(() => {
        const m = document.querySelector('[data-testid="modal-confirm-kit-reversal"]');
        return Boolean(m && document.activeElement && m.contains(document.activeElement));
      });
      kitTrapSteps.push(inside);
    }
    for (let i = 0; i < 3; i += 1) {
      await a11yPage.keyboard.press('Shift+Tab');
      const inside = await a11yPage.evaluate(() => {
        const m = document.querySelector('[data-testid="modal-confirm-kit-reversal"]');
        return Boolean(m && document.activeElement && m.contains(document.activeElement));
      });
      kitTrapSteps.push(inside);
    }
    await a11yPage.keyboard.press('Escape');
    await kitModal.waitFor({ state: 'hidden', timeout: 5000 });
    await a11yPage.waitForTimeout(80);
    const kitRestoredTestId = await a11yPage.evaluate(
      () => document.activeElement?.getAttribute('data-testid') || '',
    );
    kitReversalModalTrap = {
      ok: kitTrapSteps.every(Boolean) && kitRestoredTestId === 'btn-reverse-kit-KWO-2026-001',
      allTabStepsInsideModal: kitTrapSteps.every(Boolean),
      triggerTestId: 'btn-reverse-kit-KWO-2026-001',
      restoredTestId: kitRestoredTestId,
    };

    // 3. prefers-reduced-motion: reduce
    await a11yPage.emulateMedia({ reducedMotion: 'reduce' });
    await a11yPage.waitForTimeout(100);
    reducedMotionCheck = await a11yPage.evaluate(() => {
      const parseDurationsSec = (val) =>
        String(val || '0s')
          .split(',')
          .map((part) => {
            const p = part.trim();
            if (p.endsWith('ms')) return parseFloat(p) / 1000;
            if (p.endsWith('s')) return parseFloat(p);
            return 0;
          })
          .filter((n) => Number.isFinite(n));
      const samples = Array.from(
        document.querySelectorAll('button, a, .sidebar-nav-link, .ui-btn, #system-sidebar, .wms-secondary-flyout'),
      ).slice(0, 40);
      let maxDurationSec = 0;
      for (const el of samples) {
        const cs = window.getComputedStyle(el);
        const durs = [
          ...parseDurationsSec(cs.transitionDuration),
          ...parseDurationsSec(cs.animationDuration),
        ];
        for (const d of durs) {
          if (d > maxDurationSec) maxDurationSec = d;
        }
      }
      return {
        ok: samples.length > 0 && maxDurationSec <= 0.02,
        sampledElementsCount: samples.length,
        maxDurationSec: Number(maxDurationSec.toFixed(6)),
      };
    });
  } finally {
    await a11yCtx.close().catch(() => {});
  }

  const skipLinkWorksOnFirstTab = Boolean(skipLinkPlatform.ok && skipLinkCustomer.ok);
  const modalFocusTrapAndEscapeRestore = Boolean(reconcileModalTrap.ok && kitReversalModalTrap.ok);
  const prefersReducedMotionRespected = Boolean(reducedMotionCheck.ok);

  addCheck('FND-W19-05-SKIP-LINK-FIRST-TAB', skipLinkWorksOnFirstTab, {
    skipLinkPlatform,
    skipLinkCustomer,
  });
  if (!skipLinkWorksOnFirstTab) {
    addFinding({
      id: 'QA-W19-A11Y-SKIPLINK',
      severity: 'MEDIUM',
      domain: 'WCAG 2.2 AA Bypass Blocks — Skip-Link First Tab (FND-W19-05 / V-01)',
      ownerAgent: 'AGENT-02',
      summary: 'Pressing Tab on fresh page load failed to focus visible .skip-link on /platform or /sales-orders',
      evidence: { skipLinkPlatform, skipLinkCustomer },
    });
  }

  addCheck('FND-W19-05-MODAL-FOCUS-TRAP-AND-ESCAPE-RESTORE', modalFocusTrapAndEscapeRestore, {
    reconcileModalTrap,
    kitReversalModalTrap,
  });
  if (!modalFocusTrapAndEscapeRestore) {
    addFinding({
      id: 'QA-W19-A11Y-FOCUSTRAP',
      severity: 'HIGH',
      domain: 'WCAG 2.2 AA Dialog Focus Trap & Escape Return (FND-W19-05 / V-02)',
      ownerAgent: 'AGENT-02',
      summary: 'Tab/Shift+Tab focus escaped open modal or Escape failed to restore focus to opener button',
      evidence: { reconcileModalTrap, kitReversalModalTrap },
    });
  }

  addCheck('FND-W19-05-PREFERS-REDUCED-MOTION', prefersReducedMotionRespected, reducedMotionCheck);
  if (!prefersReducedMotionRespected) {
    addFinding({
      id: 'QA-W19-A11Y-REDUCED-MOTION',
      severity: 'LOW',
      domain: 'WCAG 2.2 AA Motion Suppression — prefers-reduced-motion (FND-W19-05 / UIX-18)',
      ownerAgent: 'AGENT-02',
      summary: `Computed transition/animation duration (${reducedMotionCheck.maxDurationSec}s) exceeded 0.02s under reducedMotion: reduce`,
      evidence: reducedMotionCheck,
    });
  }

  return {
    skipLinkWorksOnFirstTab,
    modalFocusTrapAndEscapeRestore,
    prefersReducedMotionRespected,
    details: {
      skipLinkPlatform,
      skipLinkCustomer,
      reconcileModalTrap,
      kitReversalModalTrap,
      reducedMotionCheck,
    },
  };
}

async function tryLoginCandidates(requestCtx, usernameCandidates, passwordCandidates) {
  for (const username of usernameCandidates) {
    for (const password of passwordCandidates) {
      const res = await requestCtx.post(`${BASE_API}/api/v1/auth/login`, {
        data: { username, password },
      });
      if (res.status() === 200) {
        const body = await res.json();
        const token = body.accessToken || body.token;
        const setCookie = res.headers()['set-cookie'] || '';
        if (typeof token === 'string' && token.length > 20) {
          let mergedUser = { ...body };
          try {
            const meRes = await requestCtx.get(`${BASE_API}/api/v1/auth/me`, {
              headers: { Authorization: `Bearer ${token}` },
            });
            if (meRes.status() === 200) {
              const meBody = await meRes.json();
              if (Array.isArray(meBody?.permissions)) {
                mergedUser = { ...mergedUser, permissions: meBody.permissions };
              }
            }
          } catch {
            // Keep login payload if /auth/me probe fails
          }
          return { ok: true, username, token, user: mergedUser, setCookieHeader: setCookie };
        }
      }
    }
  }
  return { ok: false, username: usernameCandidates[0], token: null, user: null, setCookieHeader: '' };
}

async function applyAuthTokenInBrowser(page, token, userObj, clearFlags = true) {
  await page.evaluate(
    ({ tok, usr, shouldClear }) => {
      window.sessionStorage.removeItem('wms_auth_me_cache');
      window.sessionStorage.setItem('wms_access_token', tok);
      if (usr?.securityDomain) {
        window.sessionStorage.setItem('securityDomain', String(usr.securityDomain));
      }
      if (Array.isArray(usr?.permissions)) {
        const sortedPerms = JSON.stringify(Array.from(new Set(usr.permissions)).sort());
        window.sessionStorage.setItem('wms_permissions', sortedPerms);
        window.localStorage.setItem('permissions', sortedPerms);
      }
      window.localStorage.removeItem('accessToken');
      window.localStorage.removeItem('token');
      window.localStorage.removeItem('wms_access_token');
      window.localStorage.setItem('username', usr?.username || 'qa_admin');
      window.localStorage.setItem('role', usr?.role || 'SuperAdmin');
      if (usr?.userId != null) {
        window.localStorage.setItem('userId', String(usr.userId));
      }
      window.localStorage.setItem('tenantId', String(usr?.tenantId ?? 1));
      window.localStorage.setItem('wmsComplexityMode', 'FULL');
      if (shouldClear) {
        for (const k of [
          'erp_saas_inv_202611_paid',
          'tenantLifecycleStatus',
          'tenantSuspensionReason',
          'erp_support_impersonation_session',
          'wms_active_support_session',
          'activeImpersonation',
          'erp_active_impersonation_session',
          'erp_platform_active_role',
        ]) {
          window.localStorage.removeItem(k);
        }
      }
      window.dispatchEvent(new Event('permissions-changed'));
    },
    { tok: token, usr: userObj, shouldClear: clearFlags },
  );
}

function runReadOnlyErpKhoSql(sqlQuery) {
  const servers = ['localhost', '.\\SQLEXPRESS', '(localdb)\\MSSQLLocalDB'];
  for (const srv of servers) {
    try {
      const psScript = `
        $conn = New-Object System.Data.SqlClient.SqlConnection("Server=${srv};Database=ERP_KHO;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=5");
        $conn.Open();
        try {
          $cmd = $conn.CreateCommand();
          $cmd.CommandText = @'
${sqlQuery}
'@;
          $reader = $cmd.ExecuteReader();
          $sb = New-Object System.Text.StringBuilder;
          while ($reader.Read()) { [void]$sb.Append($reader.GetString(0)) }
          $reader.Close();
          Write-Output $sb.ToString();
        } finally {
          $conn.Close();
        }
      `;
      const out = execFileSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', psScript], {
        encoding: 'utf8',
        timeout: 12000,
      }).trim();
      if (out) return { server: srv, raw: out };
    } catch {
      // Try next local SQL Server instance with Windows Integrated Auth on ERP_KHO only
    }
  }
  return null;
}

function queryErpKhoReadOnlySqlJson() {
  const sql = `
SELECT
  (SELECT TOP 1 Status FROM dbo.TenantSubscriptions WHERE TenantId = 1) AS tenant1Status,
  (SELECT TOP 1 MaxWarehouses FROM dbo.TenantSubscriptions WHERE TenantId = 1) AS tenant1MaxWarehouses,
  (SELECT TOP 1 Status FROM dbo.SaasBillingInvoices WHERE TenantId = 1 AND InvoiceCode = N'INV-SAAS-202611-002') AS invoice202611Status,
  (SELECT TOP 1 IsActive FROM dbo.Warehouses WHERE Code = N'WH01') AS wh01IsActive,
  (SELECT TOP 1 Id FROM dbo.Warehouses WHERE Code = N'WH01') AS wh01Id,
  (SELECT TOP 1 Id FROM dbo.Warehouses WHERE Code = N'WH02') AS wh02Id,
  (SELECT TOP 1 Status FROM dbo.KitWorkOrders WHERE TenantId = 1 AND WorkOrderCode = N'KWO-2026-001') AS kwo1Status
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
`;
  const res = runReadOnlyErpKhoSql(sql);
  if (!res?.raw) return null;
  try {
    return { server: res.server, ...JSON.parse(res.raw) };
  } catch {
    return null;
  }
}

function discoverRoleUsersFromErpKhoReadOnly() {
  const sql = `
SELECT u.Username AS username, r.RoleName AS roleName, u.TenantId AS tenantId, u.SecurityDomain AS securityDomain
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
WHERE u.IsActive = 1
FOR JSON PATH;
`;
  const res = runReadOnlyErpKhoSql(sql);
  if (!res?.raw) return [];
  try {
    return JSON.parse(res.raw);
  } catch {
    return [];
  }
}

async function checkPortOpen(port, host = '127.0.0.1') {
  return new Promise((resolve) => {
    const sock = new net.Socket();
    const done = (open) => {
      sock.destroy();
      resolve(open);
    };
    sock.setTimeout(1200);
    sock.once('connect', () => done(true));
    sock.once('timeout', () => done(false));
    sock.once('error', () => done(false));
    sock.connect(port, host);
  });
}

async function resolveBillingMetadata(requestCtx, token) {
  const res = await requestCtx.get(`${BASE_API}/api/v1/platform/billing/invoices`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (res.status() !== 200) {
    throw new Error(`GET /api/v1/platform/billing/invoices returned HTTP ${res.status()}`);
  }
  const body = await res.json();
  const items = body.items || [];
  const nov = items.find((x) => String(x.invoiceCode || '').includes('202611')) || items[0];
  const oct = items.find((x) => String(x.invoiceCode || '').includes('202610') && x.bankTransactionRef);
  return {
    novInvoiceId: Number(nov?.id || nov?.invoiceId),
    novStatus: String(nov?.status || ''),
    existingPaidBankRef: String(oct?.bankTransactionRef || 'FT2627690123'),
  };
}

async function restoreCleanBaselineViaApi(requestCtx) {
  const login = await tryLoginCandidates(requestCtx, ['qa_admin'], ['QaAdmin@123456!']);
  if (!login.ok) return null;
  await requestCtx.post(`${BASE_API}/api/v1/platform/tenants/1/status`, {
    headers: { Authorization: `Bearer ${login.token}` },
    data: {
      status: 'ACTIVE',
      lifecycleStatus: 'ACTIVE',
      reason: 'Khôi phục trạng thái Đang hoạt động cho doanh nghiệp thuê bao',
      confirmationCode: 'TENANT-001',
    },
  });
  const relogin = await tryLoginCandidates(requestCtx, ['qa_admin'], ['QaAdmin@123456!']);
  const meta = await resolveBillingMetadata(requestCtx, relogin.token);
  if (meta.novInvoiceId) {
    await requestCtx.post(`${BASE_API}/api/v1/platform/billing/invoices/${meta.novInvoiceId}/reconcile`, {
      headers: { Authorization: `Bearer ${relogin.token}` },
      data: { resetToPending: true },
    });
  }
  return { ...relogin, ...meta };
}

async function runAdversarialSecurityAndRoleProbes(requestCtx, ownerAuth, addFinding, addCheck) {
  const dbUsers = discoverRoleUsersFromErpKhoReadOnly();
  const sqlBaselinePre = queryErpKhoReadOnlySqlJson();
  const wh01Id = sqlBaselinePre?.wh01Id || 1;
  const wh02Id = sqlBaselinePre?.wh02Id || 2;

  const findUsersByRole = (roleNames, fallbackNames) => {
    const matched = dbUsers
      .filter((u) => roleNames.some((r) => String(u.roleName).toLowerCase() === r.toLowerCase()))
      .map((u) => u.username);
    return Array.from(new Set([...matched, ...fallbackNames]));
  };

  const commonPasswords = [
    'QaAdmin@123456!',
    'QaSeed!LocalOnly#2026',
    'Platform@123456!',
    'QaRole@123456!',
  ];

  // 1. Auth cookie hardening check (SEC-F06 / FND-016: HttpOnly, SameSite=Strict, Path=/)
  const cookieHeader = ownerAuth.setCookieHeader || '';
  const cookieLower = cookieHeader.toLowerCase();
  const cookieHardened =
    !cookieHeader ||
    (cookieLower.includes('httponly') &&
      cookieLower.includes('samesite=strict') &&
      /path=\/(?:;|$|\s)/i.test(cookieHeader));
  addCheck('SEC-AUTH-COOKIE-HARDENING', cookieHardened, {
    hasSetCookieHeader: Boolean(cookieHeader),
    containsHttpOnly: cookieLower.includes('httponly'),
    containsSameSiteStrict: cookieLower.includes('samesite=strict'),
    containsRootPath: /path=\/(?:;|$|\s)/i.test(cookieHeader),
  });
  if (!cookieHardened) {
    addFinding({
      id: 'QA-W17-SEC-F06',
      severity: 'MEDIUM',
      domain: 'Authentication Cookie Hardening (SEC-F06)',
      ownerAgent: 'AGENT-01',
      summary: 'Refresh token cookie is missing HttpOnly, SameSite=Strict, or Path=/',
      evidence: { setCookieRedacted: cookieHeader.replace(/=[^;]+/, '=<REDACTED>') },
    });
  }

  // 2. ProvisionSaaSTenant anonymous bypass check (SEC-F02 / FND-002)
  const anonProvisionRes = await requestCtx.post(`${BASE_API}/api/v1/permissions/saas/tenants/provision`, {
    data: { companyName: 'Unauthorized Probe Co', adminEmail: 'probe@invalid.local', planCode: 'STARTER' },
  });
  const anonBlocked = [401, 403, 404].includes(anonProvisionRes.status());
  addCheck('SEC-PROVISION-TENANT-NO-ANON', anonBlocked, { status: anonProvisionRes.status() });
  if (!anonBlocked) {
    addFinding({
      id: 'QA-W17-SEC-F02',
      severity: 'CRITICAL',
      domain: 'Authentication & Tenant Provisioning (SEC-F02)',
      ownerAgent: 'AGENT-01',
      summary: `Anonymous call to SaaS tenant provisioning was not rejected (HTTP ${anonProvisionRes.status()})`,
      evidence: { status: anonProvisionRes.status() },
    });
  }

  // 3. TOTP unauthenticated & cross-user/client-secret tampering check (SEC-F01 & SEC-F08)
  const anonTotpRes = await requestCtx.post(`${BASE_API}/api/v1/account-security/999999/totp/verify`, {
    data: { code: '000000', base32Secret: 'JBSWY3DPEHPK3PXP' },
  });
  addCheck('SEC-TOTP-UNAUTH-BLOCKED', [401, 403, 404].includes(anonTotpRes.status()), {
    status: anonTotpRes.status(),
  });

  // 3b. Webhook HMAC signature enforcement across Carrier, Marketplace & VietQR (SEC-F03 / SEC-W17-012)
  const ghnUnsignedRes = await requestCtx.post(`${BASE_API}/api/v1/shipments/webhooks/carrier/GHN`, {
    data: { trackingNumber: 'GHN-PROBE-001', status: 'DELIVERED', eventId: 'EVT-UNSIGNED-1' },
  });
  const shopeeAnonRes = await requestCtx.post(`${BASE_API}/api/v1/integrations/webhooks/SHOPEE/buyer-cancel`, {
    data: { orderCode: 'SO-PROBE-001', reason: 'Unsigned probe' },
  });
  const vietQrUnsignedRes = await requestCtx.post(`${BASE_API}/api/v1/webhooks/billing/vietqr`, {
    data: { invoiceCode: 'INV-SAAS-202611-002', planCode: 'GROWTH', paidAmountVnd: 1490000, bankTransactionRef: 'FT-UNSIGNED' },
  });
  const vietQrStaticBypassRes = await requestCtx.post(`${BASE_API}/api/v1/webhooks/billing/vietqr`, {
    headers: { 'X-VietQR-Signature': 'VALID_VIETQR_HMAC' },
    data: { invoiceCode: 'INV-SAAS-202611-002', planCode: 'GROWTH', paidAmountVnd: 1490000, bankTransactionRef: 'FT-BYPASS' },
  });
  const webhooksAll401 =
    ghnUnsignedRes.status() === 401 &&
    shopeeAnonRes.status() === 401 &&
    vietQrUnsignedRes.status() === 401 &&
    vietQrStaticBypassRes.status() === 401;
  addCheck('SEC-WEBHOOKS-UNSIGNED-AND-STATIC-BYPASS-401', webhooksAll401, {
    ghnUnsignedStatus: ghnUnsignedRes.status(),
    shopeeAnonStatus: shopeeAnonRes.status(),
    vietQrUnsignedStatus: vietQrUnsignedRes.status(),
    vietQrStaticBypassStatus: vietQrStaticBypassRes.status(),
  });
  if (!webhooksAll401) {
    addFinding({
      id: 'QA-W17-SEC-WEBHOOKS',
      severity: 'CRITICAL',
      domain: 'Webhook HMAC Verification (Carrier / Marketplace / VietQR)',
      ownerAgent: 'AGENT-01',
      summary: 'One or more webhook endpoints accepted an unsigned or static-bypass request instead of returning HTTP 401',
      evidence: {
        ghnUnsignedStatus: ghnUnsignedRes.status(),
        shopeeAnonStatus: shopeeAnonRes.status(),
        vietQrUnsignedStatus: vietQrUnsignedRes.status(),
        vietQrStaticBypassStatus: vietQrStaticBypassRes.status(),
      },
    });
  }

  // 3c. Self-registration hardening (/api/v1/auth/register-tenant): reserved username 400 & duplicate username 409
  const regReservedRes = await requestCtx.post(`${BASE_API}/api/v1/auth/register-tenant`, {
    data: {
      companyName: 'Cong Ty Probe Reserved',
      taxCode: '0399887766',
      ownerEmail: 'probe.reserved@erpkho.local',
      ownerUsername: 'qa_admin',
      ownerPassword: 'ProbePassword@2026!',
      planCode: 'STARTER',
    },
  });
  const regExistingRes = await requestCtx.post(`${BASE_API}/api/v1/auth/register-tenant`, {
    data: {
      companyName: 'Cong Ty Probe Existing',
      taxCode: '0399887755',
      ownerEmail: 'probe.existing@erpkho.local',
      ownerUsername: 'qa_admin_local',
      ownerPassword: 'ProbePassword@2026!',
      planCode: 'STARTER',
    },
  });
  const regHardenedOk = regReservedRes.status() === 400 && [400, 409].includes(regExistingRes.status());
  addCheck('SEC-REGISTER-TENANT-RESERVED-400-EXISTING-409', regHardenedOk, {
    reservedQaAdminStatus: regReservedRes.status(),
    existingQaAdminLocalStatus: regExistingRes.status(),
  });
  if (!regHardenedOk) {
    addFinding({
      id: 'QA-W17-SEC-REGISTER',
      severity: 'HIGH',
      domain: 'Tenant Self-Registration Hardening (/api/v1/auth/register-tenant)',
      ownerAgent: 'AGENT-01',
      summary: `Expected 400 for reserved ownerUsername=qa_admin and 409 for existing ownerUsername=qa_admin_local; got ${regReservedRes.status()} and ${regExistingRes.status()}`,
      evidence: {
        reservedQaAdminStatus: regReservedRes.status(),
        existingQaAdminLocalStatus: regExistingRes.status(),
      },
    });
  }

  // 4. Customer WMS & Platform 10-Role Logins, RBAC Mutation Guard, Cost Masking, 3PL OwnerId & Warehouse Scope (WH01 vs WH02)
  const roleDefinitions = [
    { role: 'SuperAdmin', primaryPw: 'QaAdmin@123456!', candidates: ['qa_admin'] },
    { role: 'BillingAdmin', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_billing_admin', ...findUsersByRole(['BillingAdmin', 'PlatformBillingAdmin'], ['billing_admin'])])) },
    { role: 'SupportAdmin', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_support_admin', ...findUsersByRole(['SupportAdmin', 'PlatformSupportAgent'], ['support_admin'])])) },
    { role: 'SecurityAuditor', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_security_auditor', ...findUsersByRole(['SecurityAuditor'], ['security_auditor'])])) },
    { role: 'Admin', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['tenant_admin', 'qa_admin_local', ...findUsersByRole(['Admin'], [])])) },
    { role: 'Manager', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_manager_wh01', ...findUsersByRole(['Manager'], [])])) },
    { role: 'Supervisor', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_supervisor_wh01', ...findUsersByRole(['Supervisor'], ['qa_supervisor'])])) },
    { role: 'Accountant', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_accountant', ...findUsersByRole(['Accountant'], ['qa_accountant_wh01'])])) },
    { role: 'WarehouseStaff', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_staff_wh01', ...findUsersByRole(['WarehouseStaff'], [])])) },
    { role: 'Viewer', primaryPw: 'QaSeed!LocalOnly#2026', candidates: Array.from(new Set(['qa_viewer_wh01', ...findUsersByRole(['Viewer'], [])])) },
  ];

  const roleProbeSummary = [];
  const roleAuthTokens = {};

  for (const rd of roleDefinitions) {
    const pwList = Array.from(
      new Set([
        rd.primaryPw,
        ...commonPasswords,
        ...rd.candidates.map((u) => `QaSeed!${u}#2026`),
      ]),
    );
    const authRes = await tryLoginCandidates(requestCtx, rd.candidates, pwList);
    if (!authRes.ok) {
      roleProbeSummary.push({ role: rd.role, loggedIn: false, candidatesTried: rd.candidates });
      continue;
    }

    roleAuthTokens[rd.role] = authRes;
    const entry = {
      role: rd.role,
      username: authRes.username,
      userId: authRes.user?.userId,
      securityDomain: authRes.user?.securityDomain,
      loggedIn: true,
    };

    // Pure PLATFORM sub-roles without active SupportImpersonationSession must be blocked (403) from customer warehouse mutations
    if (['BillingAdmin', 'SupportAdmin', 'SecurityAuditor'].includes(rd.role)) {
      const directWhWriteRes = await requestCtx.post(`${BASE_API}/api/v1/import-receipts`, {
        headers: {
          Authorization: `Bearer ${authRes.token}`,
          'Idempotency-Key': `qa-plat-wh-block-${rd.role}-${Date.now()}`,
        },
        data: { warehouseId: wh01Id, receiptNumber: `NK-PLAT-${rd.role}`, items: [] },
      });
      entry.directCustomerWarehouseMutationStatus = directWhWriteRes.status();
      addCheck(`RBAC-PLATFORM-SUBROLE-NO-DIRECT-WMS-WRITE-${rd.role}`, directWhWriteRes.status() === 403, {
        role: rd.role,
        status: directWhWriteRes.status(),
      });
    }

    // BillingAdmin 4-eyes SoD check: self-reconcile without independent approver -> 422 BILLING_SOD_SELF_APPROVAL_FORBIDDEN
    if (rd.role === 'BillingAdmin') {
      const { novInvoiceId } = await resolveBillingMetadata(requestCtx, ownerAuth.token);
      if (novInvoiceId) {
        const sodSelfRes = await requestCtx.post(`${BASE_API}/api/v1/platform/billing/invoices/${novInvoiceId}/reconcile`, {
          headers: { Authorization: `Bearer ${authRes.token}` },
          data: {
            bankTransactionRef: 'FT-SOD-SELF-422',
            bankCreditTimestampVi: '02/11/2026 18:15',
            receivedAmountVnd: 1490000,
            reasonNote: 'Kiểm chứng BillingAdmin tự đối soát thiếu người phê duyệt độc lập phải bị chặn 422',
          },
        });
        entry.billingAdminSelfReconcileStatus = sodSelfRes.status();
        addCheck('RBAC-BILLINGADMIN-4EYES-SOD-422', sodSelfRes.status() === 422, {
          status: sodSelfRes.status(),
        });
      }
    }

    // Customer Admin: verify tenant_admin (owner@erpkho.vn, 141 Customer permissions, 0 platform.*) + webhook/TOTP guards
    if (rd.role === 'Admin' && authRes.user?.userId) {
      const adminPerms = Array.isArray(authRes.user?.permissions) ? authRes.user.permissions : [];
      const platformLeakedPerms = adminPerms.filter((p) => String(p).startsWith('platform.'));
      const [permCatalogRes, permRolesRes, permUsersRes] = await Promise.all([
        requestCtx.get(`${BASE_API}/api/v1/permissions`, { headers: { Authorization: `Bearer ${authRes.token}` } }),
        requestCtx.get(`${BASE_API}/api/v1/permissions/roles`, { headers: { Authorization: `Bearer ${authRes.token}` } }),
        requestCtx.get(`${BASE_API}/api/v1/permissions/users`, { headers: { Authorization: `Bearer ${authRes.token}` } }),
      ]);
      const permCatalogJson = permCatalogRes.status() === 200 ? await permCatalogRes.json() : [];
      const permRolesJson = permRolesRes.status() === 200 ? await permRolesRes.json() : [];
      const permUsersJson = permUsersRes.status() === 200 ? await permUsersRes.json() : [];
      const roleNamesReturned = Array.isArray(permRolesJson) ? permRolesJson.map((r) => r.roleName) : [];
      const platformRolesInCustomerApi = roleNamesReturned.filter((rn) =>
        ['SuperAdmin', 'BillingAdmin', 'SupportAdmin', 'SecurityAuditor'].includes(rn),
      );
      const userItems = Array.isArray(permUsersJson) ? permUsersJson : permUsersJson?.items || [];
      const hasTenantAdminUser = userItems.some((u) => u.username === 'tenant_admin');
      const platformUsersLeakedInCustomerList = userItems.filter((u) =>
        ['qa_admin', 'qa_billing_admin', 'qa_support_admin', 'qa_security_auditor'].includes(u.username),
      );
      const pureCustomerAdminOk =
        authRes.username === 'tenant_admin' &&
        authRes.user?.securityDomain === 'CUSTOMER_TENANT' &&
        adminPerms.length === 141 &&
        platformLeakedPerms.length === 0 &&
        Array.isArray(permCatalogJson) &&
        permCatalogJson.length === 141 &&
        roleNamesReturned.length === 6 &&
        platformRolesInCustomerApi.length === 0 &&
        hasTenantAdminUser &&
        platformUsersLeakedInCustomerList.length === 0;
      entry.adminPermissionCount = adminPerms.length;
      entry.customerRolesReturned = roleNamesReturned;
      entry.hasTenantAdminInUserList = hasTenantAdminUser;
      addCheck('FND-W18-02-CUSTOMER-ADMIN-141-PERMS-6-ROLES-ZERO-PLATFORM', pureCustomerAdminOk, {
        username: authRes.username,
        securityDomain: authRes.user?.securityDomain,
        adminPermissionCount: adminPerms.length,
        platformLeakedPermsCount: platformLeakedPerms.length,
        catalogLength: Array.isArray(permCatalogJson) ? permCatalogJson.length : -1,
        roleNamesReturned,
        platformRolesInCustomerApi,
        hasTenantAdminUser,
        platformUsersLeakedCount: platformUsersLeakedInCustomerList.length,
      });
      if (!pureCustomerAdminOk) {
        addFinding({
          id: 'QA-W18-FND-02-API',
          severity: 'HIGH',
          domain: 'Portal Role Purity & CustomerPermissionCatalog (FND-W18-02)',
          ownerAgent: 'AGENT-01',
          summary: 'Customer Admin (tenant_admin) must have 141 Customer permissions, 6 Customer roles, and 0 Platform roles/users',
          evidence: {
            username: authRes.username,
            adminPermissionCount: adminPerms.length,
            roleNamesReturned,
            platformRolesInCustomerApi,
            hasTenantAdminUser,
          },
        });
      }

      const shopeeAuthUnsignedRes = await requestCtx.post(`${BASE_API}/api/v1/integrations/webhooks/SHOPEE/buyer-cancel`, {
        headers: {
          Authorization: `Bearer ${authRes.token}`,
          'Idempotency-Key': `qa-shopee-unsigned-${Date.now()}`,
        },
        data: { orderCode: 'SO-PROBE-001', reason: 'Missing HMAC header even with JWT' },
      });
      entry.shopeeUnsignedWithJwtStatus = shopeeAuthUnsignedRes.status();
      addCheck('SEC-MARKETPLACE-WEBHOOK-MISSING-HMAC-401', shopeeAuthUnsignedRes.status() === 401, {
        status: shopeeAuthUnsignedRes.status(),
      });

      const totpClientSecretRes = await requestCtx.post(
        `${BASE_API}/api/v1/account-security/${authRes.user.userId}/totp/verify`,
        {
          headers: {
            Authorization: `Bearer ${authRes.token}`,
            'Idempotency-Key': `qa-totp-secret-reject-${Date.now()}`,
          },
          data: { code: '123456', base32Secret: 'JBSWY3DPEHPK3PXP' },
        },
      );
      let totpErrCode = '';
      try {
        const tj = await totpClientSecretRes.json();
        totpErrCode = String(tj.errorCode || tj.code || '');
      } catch {
        totpErrCode = '';
      }
      const totpCrossUserRes = await requestCtx.post(`${BASE_API}/api/v1/account-security/999999/totp/verify`, {
        headers: {
          Authorization: `Bearer ${authRes.token}`,
          'Idempotency-Key': `qa-totp-cross-user-${Date.now()}`,
        },
        data: { code: '123456' },
      });
      entry.totpClientSecretStatus = totpClientSecretRes.status();
      entry.totpClientSecretErrorCode = totpErrCode;
      entry.totpCrossUserStatus = totpCrossUserRes.status();
      addCheck(
        'SEC-TOTP-CLIENT-SECRET-REJECTED-400',
        totpClientSecretRes.status() === 400 && totpErrCode === 'TOTP_CLIENT_SECRET_REJECTED',
        { status: totpClientSecretRes.status(), errorCode: totpErrCode },
      );
      addCheck('SEC-TOTP-CROSS-USER-403-OR-404', [403, 404].includes(totpCrossUserRes.status()), {
        status: totpCrossUserRes.status(),
      });
    }

    if (rd.role === 'Manager') {
      const managerTotpRes = await requestCtx.post(`${BASE_API}/api/v1/account-security/1/totp/verify`, {
        headers: {
          Authorization: `Bearer ${authRes.token}`,
          'Idempotency-Key': `qa-totp-mgr-deny-${Date.now()}`,
        },
        data: { code: '123456' },
      });
      entry.managerTotpAdminStatus = managerTotpRes.status();
      addCheck('RBAC-MANAGER-TOTP-ADMIN-403', managerTotpRes.status() === 403, {
        status: managerTotpRes.status(),
      });
    }

    // Platform boundary checks for non-SuperAdmin roles
    if (['Admin', 'Manager', 'Supervisor', 'Accountant', 'WarehouseStaff', 'Viewer'].includes(rd.role)) {
      if (authRes.username !== 'qa_admin') {
        const platRes = await requestCtx.get(`${BASE_API}/api/v1/platform/tenants`, {
          headers: { Authorization: `Bearer ${authRes.token}` },
        });
        entry.platformTenantsStatus = platRes.status();
        const blockedFromPlatform = platRes.status() === 403;
        addCheck(`RBAC-CUSTOMER-BLOCKED-FROM-PLATFORM-${rd.role}`, blockedFromPlatform, {
          role: rd.role,
          username: authRes.username,
          status: platRes.status(),
        });
        if (!blockedFromPlatform) {
          addFinding({
            id: `QA-W17-RBAC-PLAT-${rd.role.toUpperCase()}`,
            severity: 'HIGH',
            domain: 'Platform vs Customer SecurityDomain Isolation',
            ownerAgent: 'AGENT-01',
            summary: `Customer role ${rd.role} (${authRes.username}) received HTTP ${platRes.status()} instead of 403 on /api/v1/platform/tenants`,
            evidence: entry,
          });
        }
      }
    }

    if (['SupportAdmin', 'SecurityAuditor'].includes(rd.role)) {
      const recRes = await requestCtx.post(`${BASE_API}/api/v1/platform/billing/invoices/1/reconcile`, {
        headers: { Authorization: `Bearer ${authRes.token}` },
        data: {
          bankTransactionRef: 'FT999000111',
          bankCreditTimestampVi: '02/11/2026 18:15',
          receivedAmountVnd: 1490000,
          reasonNote: 'Kiểm chứng chặn quyền đối soát cho vai trò phụ trên nền tảng',
        },
      });
      entry.reconcileAttemptStatus = recRes.status();
      addCheck(`RBAC-PLATFORM-SUBROLE-RECONCILE-403-${rd.role}`, recRes.status() === 403, {
        role: rd.role,
        status: recRes.status(),
      });
    }

    if (['BillingAdmin', 'SecurityAuditor'].includes(rd.role)) {
      const suspRes = await requestCtx.post(`${BASE_API}/api/v1/platform/tenants/1/status`, {
        headers: { Authorization: `Bearer ${authRes.token}` },
        data: { status: 'SUSPENDED', reason: 'Probe unauthorized tenant status mutation', confirmationCode: 'TENANT-001' },
      });
      entry.tenantSuspendAttemptStatus = suspRes.status();
      addCheck(`RBAC-PLATFORM-SUBROLE-SUSPEND-403-${rd.role}`, suspRes.status() === 403, {
        role: rd.role,
        status: suspRes.status(),
      });
    }

    // Cost masking check for roles without inventory.cost.read (Supervisor, WarehouseStaff, Viewer) vs Accountant (has inventory.cost.read)
    if (['Supervisor', 'WarehouseStaff', 'Viewer'].includes(rd.role)) {
      const [stockRes, macPreviewRes, gpRes] = await Promise.all([
        requestCtx.get(`${BASE_API}/api/v1/inventory/current?warehouseId=${wh01Id}&pageNumber=1&pageSize=10`, {
          headers: { Authorization: `Bearer ${authRes.token}` },
        }),
        requestCtx.get(`${BASE_API}/api/v1/import-receipts/1/mac-preview`, {
          headers: { Authorization: `Bearer ${authRes.token}` },
        }),
        requestCtx.get(`${BASE_API}/api/v1/reports/owner-gross-profit?warehouseId=${wh01Id}`, {
          headers: { Authorization: `Bearer ${authRes.token}` },
        }),
      ]);
      entry.inventoryCurrentStatus = stockRes.status();
      entry.macPreviewStatus = macPreviewRes.status();
      entry.ownerGrossProfitStatus = gpRes.status();

      let gpMaskedOk = false;
      if (gpRes.status() === 200) {
        const gpJson = await gpRes.json();
        gpMaskedOk =
          gpJson.costHiddenByRbac === true &&
          gpJson.movingAverageCogs === null &&
          gpJson.grossProfit === null;
      } else if (gpRes.status() === 403) {
        gpMaskedOk = true;
      }

      if (stockRes.status() === 200) {
        const stockJson = await stockRes.json();
        const rows = stockJson.items || stockJson.data || (Array.isArray(stockJson) ? stockJson : []);
        const leakedCostRow = rows.find(
          (r) =>
            (typeof r.unitCost === 'number' && r.unitCost > 0) ||
            (typeof r.averageCost === 'number' && r.averageCost > 0) ||
            (typeof r.totalValue === 'number' && r.totalValue > 0) ||
            (typeof r.inventoryValue === 'number' && r.inventoryValue > 0),
        );
        const allCostProtected = !leakedCostRow && macPreviewRes.status() === 403 && gpMaskedOk;
        entry.costMaskedOnInventory = !leakedCostRow;
        entry.costMaskedOnGrossProfit = gpMaskedOk;
        addCheck(`SEC-COST-MASKING-${rd.role}`, allCostProtected, {
          role: rd.role,
          sampleCount: rows.length,
          costMaskedOnInventory: !leakedCostRow,
          macPreviewStatus: macPreviewRes.status(),
          costMaskedOnGrossProfit: gpMaskedOk,
        });
        if (!allCostProtected) {
          addFinding({
            id: `QA-W17-COST-LEAK-${rd.role.toUpperCase()}`,
            severity: 'HIGH',
            domain: 'Data Protection — inventory.cost.read Masking',
            ownerAgent: 'AGENT-01',
            summary: `Role ${rd.role} (${authRes.username}) cost protection failed (inventoryMasked=${!leakedCostRow}, macPreview=${macPreviewRes.status()}, gpMasked=${gpMaskedOk})`,
            evidence: { role: rd.role, macPreviewStatus: macPreviewRes.status(), gpMaskedOk },
          });
        }
      }
    }

    if (rd.role === 'Accountant') {
      const gpAccRes = await requestCtx.get(`${BASE_API}/api/v1/reports/owner-gross-profit?warehouseId=${wh01Id}`, {
        headers: { Authorization: `Bearer ${authRes.token}` },
      });
      let accCanReadCost = false;
      if (gpAccRes.status() === 200) {
        const gpAccJson = await gpAccRes.json();
        accCanReadCost = gpAccJson.costHiddenByRbac === false;
      }
      entry.accountantCanReadCost = accCanReadCost;
      addCheck('RBAC-ACCOUNTANT-COST-READ-ALLOWED', accCanReadCost, {
        status: gpAccRes.status(),
        accountantCanReadCost: accCanReadCost,
      });
    }

    // Viewer mutation block & WH02 out-of-scope write block (403)
    if (rd.role === 'Viewer') {
      const viewerMutRes = await requestCtx.post(`${BASE_API}/api/v1/products`, {
        headers: {
          Authorization: `Bearer ${authRes.token}`,
          'Idempotency-Key': `qa-viewer-deny-${Date.now()}`,
        },
        data: { code: 'PROBE-VIEWER-DENY', name: 'Viewer Forbidden Probe', baseUom: 'Cái' },
      });
      entry.viewerMutationStatus = viewerMutRes.status();
      addCheck('RBAC-VIEWER-MUTATION-403', viewerMutRes.status() === 403, {
        status: viewerMutRes.status(),
      });
    }

    if (['WarehouseStaff', 'Viewer'].includes(rd.role) && authRes.username.includes('wh01')) {
      const outScopeReadRes = await requestCtx.get(
        `${BASE_API}/api/v1/warehouses/${wh02Id}`,
        { headers: { Authorization: `Bearer ${authRes.token}` } },
      );
      entry.outOfScopeWarehouseReadStatus = outScopeReadRes.status();
      addCheck(`SEC-WAREHOUSE-SCOPE-READ-${rd.role}`, [403, 404].includes(outScopeReadRes.status()), {
        role: rd.role,
        wh02Id,
        status: outScopeReadRes.status(),
      });

      if (rd.role === 'WarehouseStaff') {
        const outScopeWriteRes = await requestCtx.post(`${BASE_API}/api/v1/inventory-movements/move-handling-unit`, {
          headers: {
            Authorization: `Bearer ${authRes.token}`,
            'Idempotency-Key': `qa-wh02-deny-${Date.now()}`,
          },
          data: {
            warehouseId: wh02Id,
            handlingUnitCode: 'HU-QA-001',
            destinationLocationCode: 'BIN-A01-03',
            reason: 'Probe out-of-scope WH02 write by WH01 staff',
          },
        });
        const outScopeWriteStatus = outScopeWriteRes.status();
        let outScopeWriteBody = null;
        try {
          outScopeWriteBody = await outScopeWriteRes.json();
        } catch {
          outScopeWriteBody = null;
        }
        const outScopeWriteBlockedCleanly = [403, 404].includes(outScopeWriteStatus);
        entry.outOfScopeWarehouseWriteStatus = outScopeWriteStatus;
        addCheck('SEC-WAREHOUSE-SCOPE-WRITE-403-WarehouseStaff', outScopeWriteBlockedCleanly, {
          wh02Id,
          status: outScopeWriteStatus,
          errorCode: outScopeWriteBody?.code || outScopeWriteBody?.errorCode || null,
        });
        if (!outScopeWriteBlockedCleanly) {
          addFinding({
            id: 'QA-W17-R1-004',
            severity: 'HIGH',
            domain: 'Layer 2/3 Warehouse-Scoped Command Guard & Exception Contract ([SEC-002], [API-002], [API-003])',
            ownerAgent: 'AGENT-01',
            summary: `POST /api/v1/inventory-movements/move-handling-unit with out-of-scope/inactive warehouseId=${wh02Id} by qa_staff_wh01 returned HTTP ${outScopeWriteStatus} (${outScopeWriteBody?.code || 'INTERNAL_SERVER_ERROR'}) instead of 403/404: MoveHandlingUnitAsync skips upfront EnsureWarehouseCommandAccessAsync(request.WarehouseId), falls back across warehouses on HandlingUnits lookup, and throws unmapped BCL KeyNotFoundException / InvalidOperationException`,
            evidence: {
              files: [
                'ERP.Infrastructure/Services/M04_Inventory/InventoryMovementService.HandlingUnitMove.cs:28,37,45-49,57,65,80-87,103,112,117,127,131,148,153,194',
                'ERP.Api/Middleware/GlobalExceptionHandlingMiddleware.cs:46-61',
                'ERP.Api/Controllers/M04_Inventory/InventoryMovementsController.cs:74,85,135-145',
              ],
              wh02Id,
              status: outScopeWriteStatus,
              responseBody: outScopeWriteBody,
            },
          });
        }

        // 3PL 9D OwnerId depositor portal query-tampering check
        const threePlTamperRes = await requestCtx.get(
          `${BASE_API}/api/v1/inventory-control/3pl-depositor-portal?warehouseId=${wh02Id}&ownerId=1&authenticatedOwnerId=999`,
          { headers: { Authorization: `Bearer ${authRes.token}` } },
        );
        entry.threePlOutScopeAndTamperStatus = threePlTamperRes.status();
        addCheck('SEC-3PL-OWNERID-AND-WAREHOUSE-ISOLATION', [403, 404].includes(threePlTamperRes.status()), {
          status: threePlTamperRes.status(),
        });
      }
    }

    roleProbeSummary.push(entry);
  }

  const all10LoggedIn = roleProbeSummary.length === 10 && roleProbeSummary.every((r) => r.loggedIn);
  addCheck('RBAC-ALL-10-CANONICAL-ROLES-AUTHENTICATED', all10LoggedIn, {
    authenticatedCount: roleProbeSummary.filter((r) => r.loggedIn).length,
    roles: roleProbeSummary.map((r) => ({ role: r.role, username: r.username, loggedIn: r.loggedIn })),
  });

  // Also test SupportReadOnly header guard on /platform/billing reconcile -> 403
  const previewRbacRes = await requestCtx.post(`${BASE_API}/api/v1/platform/billing/invoices/1/reconcile`, {
    headers: {
      Authorization: `Bearer ${ownerAuth.token}`,
      'X-Platform-Role-Preview': 'SupportReadOnly',
    },
    data: {
      bankTransactionRef: 'FT2627799222',
      bankCreditTimestampVi: '02/11/2026 18:15',
      receivedAmountVnd: 1490000,
      reasonNote: 'Đối soát thanh toán hóa đơn dịch vụ thuê bao tháng 11/2026',
    },
  });
  addCheck('RBAC-PLATFORM-PREVIEW-SUPPORTREADONLY-403', previewRbacRes.status() === 403, {
    status: previewRbacRes.status(),
  });

  return { roleProbeSummary, roleAuthTokens, sqlBaselinePre };
}

async function runIsolatedMultiRoleBrowserProbes(browser, roleAuthTokens, addFinding, addCheck) {
  const isoCtx = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });
  for (const staleOrigin of ['http://localhost:5260', 'https://localhost:7198', 'http://127.0.0.1:5067']) {
    await isoCtx.route(`${staleOrigin}/**`, async (route) => {
      const redirected = route.request().url().replace(staleOrigin, BASE_API);
      const resp = await route.fetch({ url: redirected });
      await route.fulfill({ response: resp });
    });
  }

  const isoPage = await isoCtx.newPage();
  const subRoleHttp403s = [];
  isoPage.on('response', (resp) => {
    if (resp.url().includes('/api/') && resp.status() === 403) {
      subRoleHttp403s.push({ method: resp.request().method(), url: resp.url(), status: 403 });
    }
  });

  try {
    // A. Real /login UI form submission with tenant_admin -> verify 0 raw JWTs in window.localStorage
    await isoPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await isoPage.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await isoPage.locator('#login-username').fill('tenant_admin');
    await isoPage.locator('#login-password').fill('QaSeed!LocalOnly#2026');
    await Promise.all([
      isoPage.waitForResponse((r) => r.url().includes('/api/v1/auth/login') && r.request().method() === 'POST', {
        timeout: 15000,
      }),
      isoPage.locator('form.login-form button[type="submit"]').click(),
    ]);
    await isoPage.waitForLoadState('networkidle').catch(() => {});
    await isoPage.waitForTimeout(300);

    const jwtStorageCheck = await isoPage.evaluate(() => {
      const leakedLocalStorageKeys = [];
      for (let i = 0; i < window.localStorage.length; i += 1) {
        const k = window.localStorage.key(i);
        if (!k) continue;
        const v = String(window.localStorage.getItem(k) || '');
        if (/eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\./.test(v)) {
          leakedLocalStorageKeys.push(k);
        }
      }
      const sessionJwtPresent = /^eyJ[A-Za-z0-9_-]+\./.test(
        String(window.sessionStorage.getItem('wms_access_token') || ''),
      );
      return { leakedLocalStorageKeys, sessionJwtPresent, postLoginPath: window.location.pathname };
    });

    const zeroLocalJwtOk = jwtStorageCheck.leakedLocalStorageKeys.length === 0 && jwtStorageCheck.sessionJwtPresent;
    addCheck('SEC-ZERO-RAW-JWT-IN-LOCALSTORAGE', zeroLocalJwtOk, jwtStorageCheck);
    if (!zeroLocalJwtOk) {
      addFinding({
        id: 'QA-W17-SEC-LOCALSTORAGE-JWT',
        severity: 'HIGH',
        domain: 'Token Storage Hardening (sessionStorage vs localStorage)',
        ownerAgent: 'AGENT-02',
        summary: 'Raw JWT token was found in window.localStorage or missing from sessionStorage after /login submission',
        evidence: jwtStorageCheck,
      });
    }

    // Also verify Customer Admin navigating to /platform receives 403 platform-domain-forbidden-alert
    await isoPage.goto(`${BASE_WEB}/platform`, { waitUntil: 'networkidle' });
    await isoPage.waitForTimeout(300);
    const customerPlatformBlockedUi = await isoPage.evaluate(() => Boolean(
      document.querySelector('[data-testid="platform-domain-forbidden-alert"]'),
    ));
    addCheck('UI-CUSTOMER-ADMIN-BLOCKED-ON-PLATFORM-ROUTE', customerPlatformBlockedUi, {
      customerPlatformBlockedUi,
    });

    // B. Pure PLATFORM Sub-Role (BillingAdmin: qa_billing_admin) real /login redirect & /platform/* scoped fetch audit
    subRoleHttp403s.length = 0;
    await isoPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await isoPage.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await isoPage.locator('#login-username').fill('qa_billing_admin');
    await isoPage.locator('#login-password').fill('QaSeed!LocalOnly#2026');
    await Promise.all([
      isoPage.waitForResponse((r) => r.url().includes('/api/v1/auth/login') && r.request().method() === 'POST', {
        timeout: 15000,
      }),
      isoPage.locator('form.login-form button[type="submit"]').click(),
    ]);
    await isoPage.waitForTimeout(600);
    const billingPostLoginPath = await isoPage.evaluate(() => window.location.pathname);
    const login403s = [...subRoleHttp403s];

    // Now navigate to /platform/billing?rolePreview=SuperAdmin to verify both ?rolePreview immunity AND scoped fetches
    subRoleHttp403s.length = 0;
    await isoPage.goto(`${BASE_WEB}/platform/billing?rolePreview=SuperAdmin`, { waitUntil: 'networkidle' });
    await isoPage.waitForTimeout(400);
    const billingTabCheck = await isoPage.evaluate(() => {
      const badgeText = (document.querySelector('[data-testid="platform-role-readonly-badge"]')?.textContent || '').trim();
      const dangerAlertText = (document.querySelector('[data-testid="platform-action-notice"]')?.textContent || '').trim();
      const integrationsNavVisible = Boolean(document.querySelector('[data-testid="nav-platform-integrations"]'));
      return {
        badgeText,
        rolePreviewIgnored: !badgeText.includes('Chủ dự án'),
        dangerAlertText,
        integrationsNavVisible,
      };
    });
    const platformBilling403s = [...subRoleHttp403s];

    addCheck('SEC-ROLEPREVIEW-QUERY-IGNORED-ON-PLATFORM', billingTabCheck.rolePreviewIgnored, {
      badgeText: billingTabCheck.badgeText,
    });

    // Check direct URL navigation to /platform/integrations as BillingAdmin (must hide nav item, emit 0 403s, render forbidden alert, and show 0 fake fallback tokens)
    subRoleHttp403s.length = 0;
    await isoPage.goto(`${BASE_WEB}/platform/integrations`, { waitUntil: 'networkidle' });
    await isoPage.waitForTimeout(300);
    const directIntegrationsProbe = await isoPage.evaluate(() => {
      const bodyText = document.body?.innerText || '';
      const forbiddenAlertVisible = Boolean(
        document.querySelector('[data-testid="platform-integrations-forbidden-alert"]'),
      );
      return {
        forbiddenAlertVisible,
        integrationsFakeFallbackRendered: bodyText.includes('235512') || bodyText.includes('0c06****12be'),
      };
    });
    const integrations403s = [...subRoleHttp403s];
    const integrationsFakeFallbackRendered = directIntegrationsProbe.integrationsFakeFallbackRendered;

    const platformSubRoleClean =
      billingPostLoginPath.startsWith('/platform') &&
      login403s.length === 0 &&
      platformBilling403s.length === 0 &&
      integrations403s.length === 0 &&
      !billingTabCheck.dangerAlertText &&
      !billingTabCheck.integrationsNavVisible &&
      directIntegrationsProbe.forbiddenAlertVisible &&
      !integrationsFakeFallbackRendered;

    addCheck('UI-PLATFORM-SUBROLE-LOGIN-AND-SCOPED-FETCH', platformSubRoleClean, {
      billingPostLoginPath,
      login403Count: login403s.length,
      platformBilling403s,
      integrations403s,
      dangerAlertText: billingTabCheck.dangerAlertText,
      integrationsNavVisible: billingTabCheck.integrationsNavVisible,
      directIntegrationsForbiddenAlertVisible: directIntegrationsProbe.forbiddenAlertVisible,
      integrationsFakeFallbackRendered,
    });

    if (!platformSubRoleClean) {
      addFinding({
        id: 'QA-W17-R1-001',
        severity: 'HIGH',
        domain: 'Platform Sub-Role Routing & RBAC-Scoped Data Loading (OWN-DEC-PLATFORM-RBAC-FUTURE-READY)',
        ownerAgent: 'AGENT-02',
        summary:
          'Pure PLATFORM sub-roles (BillingAdmin/SupportAdmin/SecurityAuditor) are redirected to Customer "/" after /login (frontend/src/pages/Login.tsx:108), and opening /platform/* unconditionally calls unauthorized endpoints (e.g. GET /api/v1/platform/integrations/health -> 403 at PlatformControlCenter.tsx:76-87), triggering a danger alert banner on authorized tabs and exposing fake fallback data (235512 / 0c06****12be) on /platform/integrations',
        evidence: {
          files: [
            'frontend/src/pages/Login.tsx:108',
            'frontend/src/pages/PlatformControlCenter.tsx:39-44,76-87',
            'frontend/src/layouts/PlatformControlCenterLayout.tsx:32-41,200-219',
          ],
          billingPostLoginPath,
          login403s,
          platformBilling403s,
          dangerAlertText: billingTabCheck.dangerAlertText,
          integrationsFakeFallbackRendered,
        },
      });
    }

    // C. Customer WMS Role UI Guards: Manager on /permissions (scoped user.read check) & Viewer on /sales-orders & /cost-valuation
    let managerPermissions403s = [];
    if (roleAuthTokens.Manager?.ok) {
      subRoleHttp403s.length = 0;
      await applyAuthTokenInBrowser(isoPage, roleAuthTokens.Manager.token, roleAuthTokens.Manager.user, true);
      await isoPage.goto(`${BASE_WEB}/permissions`, { waitUntil: 'networkidle' });
      await isoPage.waitForTimeout(400);
      managerPermissions403s = [...subRoleHttp403s];
    }

    let viewerUiGuardOk = true;
    if (roleAuthTokens.Viewer?.ok) {
      await applyAuthTokenInBrowser(isoPage, roleAuthTokens.Viewer.token, roleAuthTokens.Viewer.user, true);
      await isoPage.goto(`${BASE_WEB}/sales-orders`, { waitUntil: 'networkidle' });
      await isoPage.waitForTimeout(300);
      const createBtnCount = await isoPage.getByTestId('btn-open-create-so').count();
      await isoPage.goto(`${BASE_WEB}/cost-valuation`, { waitUntil: 'networkidle' });
      await isoPage.waitForTimeout(300);
      const costDeniedVisible = await isoPage.evaluate(() =>
        (document.body?.innerText || '').includes('Không có quyền truy cập'),
      );
      viewerUiGuardOk = createBtnCount === 0 && costDeniedVisible;
      addCheck('UI-VIEWER-HIDE-CREATE-SO-AND-BLOCK-COST-ROUTE', viewerUiGuardOk, {
        createBtnCount,
        costDeniedVisible,
      });
    }

    const managerPermissionsClean = managerPermissions403s.length === 0;
    addCheck('UI-MANAGER-PERMISSIONS-NO-UNSCOPED-403-FETCH', managerPermissionsClean, {
      managerPermissions403s,
    });

    if (!managerPermissionsClean) {
      addFinding({
        id: 'QA-W17-R1-003',
        severity: 'LOW',
        domain: 'Customer WMS Permission-Scoped Data Fetching (/permissions)',
        ownerAgent: 'AGENT-02',
        summary:
          'Permissions.tsx:118 unconditionally fetches GET /api/v1/permissions/users on mount without checking usePermission("user.read"), causing avoidable HTTP 403 console/network errors when Manager (or BillingAdmin/SupportAdmin/SecurityAuditor with permission.read but without user.read) opens /permissions',
        evidence: {
          files: ['frontend/src/pages/Permissions.tsx:49-53,118'],
          managerPermissions403s,
        },
      });
    }
  } finally {
    await isoCtx.close().catch(() => {});
  }
}

async function wireStaleOrigins(browserCtx) {
  for (const staleOrigin of ['http://localhost:5260', 'https://localhost:7198', 'http://127.0.0.1:5067']) {
    await browserCtx.route(`${staleOrigin}/**`, async (route) => {
      const redirected = route.request().url().replace(staleOrigin, BASE_API);
      const resp = await route.fetch({ url: redirected });
      await route.fulfill({ response: resp });
    });
  }
}

async function runDedicatedAxeAudit(browser, ownerAuth, tenantAdminAuth, addFinding, addCheck) {
  const axeSource = await fetchAxeMinSource();
  const axeCtx = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });
  await wireStaleOrigins(axeCtx);
  const axePage = await axeCtx.newPage();
  const routeResults = [];

  const auditRouteWithAxeAndContrast = async (rt, portal, actor) => {
    await axePage.goto(`${BASE_WEB}${rt}`, { waitUntil: 'networkidle' });
    await axePage.waitForTimeout(300);
    await axePage.evaluate(axeSource);
    const res = await axePage.evaluate(async () => {
      const out = await window.axe.run(document, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'] },
      });
      const parseRgb = (str) => {
        const m = String(str || '').match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)/i);
        if (!m) return null;
        return { r: Number(m[1]), g: Number(m[2]), b: Number(m[3]), a: m[4] === undefined ? 1 : Number(m[4]) };
      };
      const srgbToLin = (c) => {
        const s = c / 255;
        return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
      };
      const lum = ({ r, g, b }) => 0.2126 * srgbToLin(r) + 0.7152 * srgbToLin(g) + 0.0722 * srgbToLin(b);
      const resolveBg = (el) => {
        let cur = el;
        while (cur && cur !== document.documentElement) {
          const bg = parseRgb(window.getComputedStyle(cur).backgroundColor);
          if (bg && bg.a >= 0.95) return bg;
          cur = cur.parentElement;
        }
        return { r: 255, g: 255, b: 255, a: 1 };
      };

      const mutedNodes = Array.from(document.querySelectorAll('main .ui-muted-text, main .ui-metric-sub, main td div, main th'));
      let lowContrastMutedCount = 0;
      let minMutedContrastRatio = 21;
      for (const node of mutedNodes.slice(0, 60)) {
        const cs = window.getComputedStyle(node);
        if (cs.display === 'none' || cs.visibility === 'hidden' || Number(cs.opacity) < 0.9) continue;
        const txt = (node.textContent || '').trim();
        if (!txt) continue;
        const fg = parseRgb(cs.color);
        if (!fg || fg.a < 0.95) continue;
        const bg = resolveBg(node);
        const l1 = Math.max(lum(fg), lum(bg));
        const l2 = Math.min(lum(fg), lum(bg));
        const ratio = Number(((l1 + 0.05) / (l2 + 0.05)).toFixed(2));
        if (ratio < minMutedContrastRatio) minMutedContrastRatio = ratio;
        const fontPx = parseFloat(cs.fontSize || '14');
        const weight = Number(cs.fontWeight || 400);
        const isLarge = fontPx >= 24 || (fontPx >= 18.66 && weight >= 700);
        const minReq = isLarge ? 3.0 : 4.5;
        if (ratio < minReq) {
          lowContrastMutedCount += 1;
        }
      }

      const colorContrastViolationsCount = out.violations.filter((v) => v.id === 'color-contrast').length;
      return {
        violationsCount: out.violations.length,
        colorContrastViolationsCount,
        lowContrastMutedCount,
        minMutedContrastRatio: minMutedContrastRatio === 21 ? null : minMutedContrastRatio,
        passesCount: out.passes.length,
        violations: out.violations.map((v) => ({
          id: v.id,
          impact: v.impact,
          description: v.description,
          nodes: v.nodes.slice(0, 3).map((n) => n.html.slice(0, 140)),
        })),
      };
    });
    routeResults.push({ route: rt, portal, actor, ...res });
  };

  try {
    await axePage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await applyAuthTokenInBrowser(axePage, ownerAuth.token, ownerAuth.user, true);
    for (const rt of ['/platform', '/platform/billing', '/platform/tenants']) {
      await auditRouteWithAxeAndContrast(rt, 'PLATFORM', 'qa_admin');
    }

    await axePage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await axePage.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await applyAuthTokenInBrowser(axePage, tenantAdminAuth.token, tenantAdminAuth.user, true);

    const customerAxeRoutes = [
      '/',
      '/saas-billing',
      '/permissions',
      '/sales-orders',
      '/backorders',
      '/import-receipts',
      '/kit-work-orders',
      '/customer-returns',
      '/cost-valuation',
      '/picking-tasks',
    ];
    for (const rt of customerAxeRoutes) {
      await auditRouteWithAxeAndContrast(rt, 'CUSTOMER_TENANT', 'tenant_admin');
    }
  } finally {
    await axeCtx.close().catch(() => {});
  }

  const axeViolationCount = routeResults.reduce((s, r) => s + r.violationsCount, 0);
  const axeColorContrastViolationsCount = routeResults.reduce((s, r) => s + r.colorContrastViolationsCount, 0);
  const lowContrastMutedCount = routeResults.reduce((s, r) => s + r.lowContrastMutedCount, 0);
  const allTextContrastPairsPassAA =
    axeColorContrastViolationsCount === 0 && lowContrastMutedCount === 0;

  addCheck('A11Y-AXE-CORE-WCAG22AA-13-ROUTES-ZERO-VIOLATIONS', axeViolationCount === 0, {
    routesAuditedCount: routeResults.length,
    axeViolationCount,
    axeColorContrastViolationsCount,
    lowContrastMutedCount,
    allTextContrastPairsPassAA,
    routeSummary: routeResults.map((r) => ({
      route: r.route,
      actor: r.actor,
      violationsCount: r.violationsCount,
      colorContrastViolationsCount: r.colorContrastViolationsCount,
      lowContrastMutedCount: r.lowContrastMutedCount,
      minMutedContrastRatio: r.minMutedContrastRatio,
      passesCount: r.passesCount,
    })),
    violationsDetail: routeResults.filter((r) => r.violationsCount > 0 || r.lowContrastMutedCount > 0),
  });
  addCheck('FND-W19-05-ALL-TEXT-CONTRAST-PAIRS-PASS-AA', allTextContrastPairsPassAA, {
    axeColorContrastViolationsCount,
    lowContrastMutedCount,
  });
  if (axeViolationCount > 0 || !allTextContrastPairsPassAA) {
    addFinding({
      id: 'QA-W19-AXE-CONTRAST-01',
      severity: 'HIGH',
      domain: 'Automated WCAG 2.2 AA Accessibility & Contrast (axe-core 4.10.2 / FND-W19-05)',
      ownerAgent: 'AGENT-02',
      summary: `axe-core detected ${axeViolationCount} WCAG 2.2 AA violation(s) or ${lowContrastMutedCount} low-contrast text pair(s) across 13 audited live routes`,
      evidence: routeResults.filter((r) => r.violationsCount > 0 || r.lowContrastMutedCount > 0),
    });
  }
  return { axeViolationCount, allTextContrastPairsPassAA, routeResults };
}

async function runEightViewportZeroOverflowMatrix(browser, ownerAuth, tenantAdminAuth, addFinding, addCheck) {
  const viewports = [
    { width: 360, height: 800 },
    { width: 375, height: 812 },
    { width: 390, height: 844 },
    { width: 768, height: 1024 },
    { width: 1024, height: 768 },
    { width: 1280, height: 800 },
    { width: 1440, height: 900 },
    { width: 1920, height: 1080 },
  ];
  const vpCtx = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });
  await wireStaleOrigins(vpCtx);
  const vpPage = await vpCtx.newPage();
  const matrix = [];
  const operationalTableMatrix = [];
  const touchTargetProbes = [];
  const geometryMatrixProbes = [];

  try {
    await vpPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await applyAuthTokenInBrowser(vpPage, ownerAuth.token, ownerAuth.user, true);
    for (const vp of viewports) {
      await vpPage.setViewportSize(vp);
      const vpLabel = `${vp.width}x${vp.height}`;
      await vpPage.goto(`${BASE_WEB}/platform`, { waitUntil: 'networkidle' });
      await vpPage.waitForTimeout(180);
      const m = await collectViewportHygiene(vpPage, `/platform@${vpLabel}`);
      matrix.push({
        viewport: vpLabel,
        route: '/platform',
        actor: 'qa_admin',
        horizontalOverflowPx: m.horizontalOverflowPx,
        scrollWidth: m.scrollWidth,
        innerWidth: m.innerWidth,
      });
      geometryMatrixProbes.push(await evaluateTruePixelAndGeometryOnPage(vpPage, `8VP:/platform@${vpLabel}`));
      if (vp.width <= 1024) {
        touchTargetProbes.push(
          await measureTouchTargetHeightsAtOrBelow1024(vpPage, `/platform@${vpLabel}`),
        );
      }
    }

    await vpPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await vpPage.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await applyAuthTokenInBrowser(vpPage, tenantAdminAuth.token, tenantAdminAuth.user, true);

    const customerMatrixRoutes = [
      '/saas-billing',
      '/permissions',
      '/sales-orders',
      '/import-receipts',
      '/kit-work-orders',
      '/customer-returns',
      '/picking-tasks',
    ];
    const operationalTableRoutes = new Set([
      '/sales-orders',
      '/import-receipts',
      '/kit-work-orders',
      '/customer-returns',
    ]);

    for (const vp of viewports) {
      await vpPage.setViewportSize(vp);
      const vpLabel = `${vp.width}x${vp.height}`;
      for (const rt of customerMatrixRoutes) {
        await vpPage.goto(`${BASE_WEB}${rt}`, { waitUntil: 'networkidle' });
        await vpPage.waitForTimeout(180);
        const m = await collectViewportHygiene(vpPage, `${rt}@${vpLabel}`);
        matrix.push({
          viewport: vpLabel,
          route: rt,
          actor: 'tenant_admin',
          horizontalOverflowPx: m.horizontalOverflowPx,
          scrollWidth: m.scrollWidth,
          innerWidth: m.innerWidth,
        });
        geometryMatrixProbes.push(await evaluateTruePixelAndGeometryOnPage(vpPage, `8VP:${rt}@${vpLabel}`));
        if (vp.width >= 768 && operationalTableRoutes.has(rt)) {
          const tblM = await measureVisibleTablesAtScrollZero(vpPage, rt, vpLabel);
          operationalTableMatrix.push(...tblM);
        }
        if (vp.width <= 1024) {
          touchTargetProbes.push(await measureTouchTargetHeightsAtOrBelow1024(vpPage, `${rt}@${vpLabel}`));
        }
      }
    }
  } finally {
    await vpCtx.close().catch(() => {});
  }

  const maxMatrixOverflowPx = Math.max(0, ...matrix.map((x) => x.horizontalOverflowPx));
  const all8ViewportsZeroOverflow = maxMatrixOverflowPx === 0 && viewports.length === 8;
  addCheck('UI-ALL-8-VIEWPORTS-ZERO-HORIZONTAL-OVERFLOW', all8ViewportsZeroOverflow, {
    viewportsTested: viewports.map((v) => `${v.width}x${v.height}`),
    totalProbes: matrix.length,
    maxMatrixOverflowPx,
    overflowViolations: matrix.filter((x) => x.horizontalOverflowPx > 0),
  });
  if (!all8ViewportsZeroOverflow) {
    addFinding({
      id: 'QA-W19-OVERFLOW-8VP',
      severity: 'HIGH',
      domain: '8-Viewport Responsive Zero-Overflow Matrix',
      ownerAgent: 'AGENT-02',
      summary: `Horizontal overflow detected (${maxMatrixOverflowPx}px) in 8-viewport responsive matrix`,
      evidence: matrix.filter((x) => x.horizontalOverflowPx > 0),
    });
  }

  const isStatusColCompliant = (t) =>
    t.statusVisibleAtScroll0 &&
    (String(t.tableId || '').toLowerCase().includes('bom') || t.statusColIndex1Based <= 3);
  const statusTables = operationalTableMatrix.filter((t) => t.hasStatusColumn);
  const statusColumnVisibleAtScrollLeft0 =
    statusTables.length > 0 && statusTables.every(isStatusColCompliant);
  const stickyRightWidthRatio = operationalTableMatrix.length
    ? Math.max(0, ...operationalTableMatrix.map((t) => t.stickyRightWidthRatio || 0))
    : 0;
  const maxStickyActionWidthPx = operationalTableMatrix.length
    ? Math.max(0, ...operationalTableMatrix.map((t) => t.stickyActionWidthPx || 0))
    : 0;
  const clippedWarehouseTextCount = operationalTableMatrix.reduce(
    (s, t) => s + (t.clippedWarehouseCellsCount || 0),
    0,
  );
  const rmaDesktopTables = operationalTableMatrix.filter(
    (t) =>
      t.route === '/customer-returns' &&
      ['1280x800', '1440x900', '1920x1080'].includes(t.viewport),
  );
  const customerReturnsZeroHorizontalScrollDesktop =
    rmaDesktopTables.length >= 3 && rmaDesktopTables.every((t) => !t.hasOverflow);

  addCheck(
    'FND-W19-01-OPERATIONAL-TABLES-STATUS-VISIBLE-SCROLL0-AND-STICKY-BUDGET',
    statusColumnVisibleAtScrollLeft0 &&
      stickyRightWidthRatio <= 0.25 &&
      maxStickyActionWidthPx <= 140 &&
      clippedWarehouseTextCount === 0 &&
      customerReturnsZeroHorizontalScrollDesktop,
    {
      statusTablesChecked: statusTables.length,
      statusColumnVisibleAtScrollLeft0,
      stickyRightWidthRatio,
      maxStickyActionWidthPx,
      clippedWarehouseTextCount,
      customerReturnsZeroHorizontalScrollDesktop,
      failingStatusTables: statusTables.filter((t) => !isStatusColCompliant(t)),
      rmaDesktopTables,
    },
  );
  if (
    !statusColumnVisibleAtScrollLeft0 ||
    stickyRightWidthRatio > 0.25 ||
    maxStickyActionWidthPx > 140 ||
    clippedWarehouseTextCount > 0 ||
    !customerReturnsZeroHorizontalScrollDesktop
  ) {
    addFinding({
      id: 'QA-W19-TABLE-VISIBILITY-01',
      severity: 'MEDIUM',
      domain: 'Operational Table Visibility at scrollLeft=0 & Sticky Budget (FND-W19-01)',
      ownerAgent: 'AGENT-02',
      summary:
        'Operational table failed status visibility at scrollLeft=0, exceeded sticky-right width budget, clipped WH01 text, or overflowed /customer-returns at >=1280px',
      evidence: {
        statusColumnVisibleAtScrollLeft0,
        stickyRightWidthRatio,
        maxStickyActionWidthPx,
        clippedWarehouseTextCount,
        customerReturnsZeroHorizontalScrollDesktop,
        failingTables: operationalTableMatrix.filter(
          (t) =>
            (t.hasStatusColumn && !isStatusColCompliant(t)) ||
            t.stickyRightWidthRatio > 0.25 ||
            t.stickyActionWidthPx > 140 ||
            t.clippedWarehouseCellsCount > 0,
        ),
        rmaDesktopTables,
      },
    });
  }

  const validHeights = touchTargetProbes.map((p) => p.minHeightPx).filter((h) => typeof h === 'number');
  const minTouchTargetHeightPx = validHeights.length ? Math.min(...validHeights) : 0;
  const touchViolations = touchTargetProbes.flatMap((p) => p.violations);
  addCheck('FND-W19-04-TOUCH-TARGETS-MIN-44PX-AT-OR-BELOW-1024', minTouchTargetHeightPx >= 44 && touchViolations.length === 0, {
    probesCount: touchTargetProbes.length,
    minTouchTargetHeightPx,
    violationsCount: touchViolations.length,
    sampleViolations: touchViolations.slice(0, 10),
  });
  if (minTouchTargetHeightPx < 44 || touchViolations.length > 0) {
    addFinding({
      id: 'QA-W19-TOUCH-TARGET-44PX',
      severity: 'LOW',
      domain: 'Touch & Pointer Target Ergonomics <= 1024px (FND-W19-04 / UIX-19)',
      ownerAgent: 'AGENT-02',
      summary: `Interactive control height (${minTouchTargetHeightPx}px) fell below 44px minimum at <= 1024px`,
      evidence: touchViolations.slice(0, 15),
    });
  }

  return {
    all8ViewportsZeroOverflow,
    maxMatrixOverflowPx,
    statusColumnVisibleAtScrollLeft0,
    stickyRightWidthRatio,
    maxStickyActionWidthPx,
    clippedWarehouseTextCount,
    customerReturnsZeroHorizontalScrollDesktop,
    minTouchTargetHeightPx,
    matrix,
    operationalTableMatrix,
    geometryMatrixProbes,
  };
}

async function runTouchFlyoutVerificationAndCaptureW2010(
  browser,
  tenantAdminAuth,
  captureScreenshots,
  screenshotsManifest,
  sortHeaderMetrics,
  routeHygieneChecks,
  screenshotGeometryProbes,
  addFinding,
  addCheck,
) {
  const touchCtx = await browser.newContext({
    viewport: { width: 1024, height: 768 },
    hasTouch: true,
    isMobile: false,
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });
  await wireStaleOrigins(touchCtx);
  const touchPage = await touchCtx.newPage();
  const touchResults = [];
  let salesOrders1024HeaderCheck = null;

  try {
    await touchPage.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await applyAuthTokenInBrowser(touchPage, tenantAdminAuth.token, tenantAdminAuth.user, true);

    for (const vp of [
      { width: 1024, height: 768, captureW2010: true },
      { width: 768, height: 1024, captureW2010: false },
    ]) {
      await touchPage.setViewportSize({ width: vp.width, height: vp.height });
      await touchPage.goto(`${BASE_WEB}/sales-orders`, { waitUntil: 'networkidle' });
      if (vp.width <= 768) {
        await touchPage
          .locator('[data-testid="sales-orders-mobile-cards"], [data-testid="sales-orders-table"]')
          .first()
          .waitFor({ state: 'visible', timeout: 15000 });
      } else {
        await touchPage.getByTestId('sales-orders-table').waitFor({ state: 'visible', timeout: 15000 });
      }
      await touchPage.waitForTimeout(250);

      const beforeMain = await touchPage.evaluate(() => {
        const m = document.querySelector('main');
        const r = m ? m.getBoundingClientRect() : { x: 0, width: 0 };
        return { x: Math.round(r.x), width: Math.round(r.width) };
      });

      const railBtn = touchPage.getByTestId('nav-group-xuat-kho');
      await railBtn.tap();
      await touchPage.getByTestId('wms-secondary-flyout').waitFor({ state: 'visible', timeout: 5000 });
      await touchPage.waitForTimeout(200);

      const duringTap = await touchPage.evaluate(() => {
        const m = document.querySelector('main');
        const r = m ? m.getBoundingClientRect() : { x: 0, width: 0 };
        const btn = document.querySelector('[data-testid="nav-group-xuat-kho"]');
        const flyout = document.querySelector('[data-testid="wms-secondary-flyout"]');
        return {
          mainX: Math.round(r.x),
          mainWidth: Math.round(r.width),
          ariaExpanded: btn?.getAttribute('aria-expanded') || '',
          flyoutOpen: Boolean(flyout && flyout.classList.contains('is-open')),
        };
      });

      // Tap outside sidebar & flyout in empty page header margin (x = vp.width - 40, y = 88) and blur focus
      await touchPage.touchscreen.tap(vp.width - 40, 88);
      await touchPage.evaluate(() => {
        if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
      });
      await touchPage.waitForTimeout(220);

      const afterOutsideTap = await touchPage.evaluate(() => {
        const btn = document.querySelector('[data-testid="nav-group-xuat-kho"]');
        const flyout = document.querySelector('[data-testid="wms-secondary-flyout"]');
        const cs = flyout ? window.getComputedStyle(flyout) : null;
        const flyoutStillOpen = Boolean(
          flyout && flyout.classList.contains('is-open') && cs && cs.visibility !== 'hidden',
        );
        return {
          ariaExpandedAfterClose: btn?.getAttribute('aria-expanded') || '',
          flyoutClosedCleanly: !flyoutStillOpen,
        };
      });

      if (vp.captureW2010) {
        const sortM = await measureSortHeadersOnPage(touchPage, '/sales-orders', '1024x768');
        sortHeaderMetrics.push(sortM);
        routeHygieneChecks.push(await collectViewportHygiene(touchPage, '/sales-orders@1024x768'));
        const geo1024 = await evaluateTruePixelAndGeometryOnPage(touchPage, 'W20_10:/sales-orders@1024x768');
        screenshotGeometryProbes.push(geo1024);
        salesOrders1024HeaderCheck = await touchPage.evaluate(() => {
          const tbl = document.querySelector('[data-testid="sales-orders-table"]');
          const ths = tbl ? Array.from(tbl.querySelectorAll('thead th')) : [];
          const daGiuTh = ths.find((th) => (th.textContent || '').includes('Đã giữ / PB'));
          const stickyTh = ths.find((th) => window.getComputedStyle(th).position === 'sticky' && th === ths[ths.length - 1]);
          const stickyCs = stickyTh ? window.getComputedStyle(stickyTh) : null;
          return {
            hasDaGiuPbHeader: Boolean(daGiuTh),
            daGiuPbWidthPx: daGiuTh ? Math.round(daGiuTh.getBoundingClientRect().width) : 0,
            stickyActionWidthPx: stickyTh ? Math.round(stickyTh.getBoundingClientRect().width) : 0,
            stickyHasBoxShadow: Boolean(stickyCs && stickyCs.boxShadow && stickyCs.boxShadow !== 'none'),
          };
        });
        if (captureScreenshots) {
          const info = await capturePureCdpPng(
            touchPage,
            'W20_10_Tablet_1024x768_SalesOrders_UnclippedHeaders_StickyShadow_ZeroOverlap_1024x768.png',
          );
          screenshotsManifest.push({
            ...info,
            route: '/sales-orders',
            viewport: '1024x768',
            actor: 'tenant_admin',
            checks: salesOrders1024HeaderCheck,
            zeroDomMutationsInjected: true,
          });
        }
      }

      touchResults.push({
        viewport: `${vp.width}x${vp.height}`,
        beforeMain,
        duringTap,
        mainShiftXPx: Math.abs(duringTap.mainX - beforeMain.x),
        mainShiftWidthPx: Math.abs(duringTap.mainWidth - beforeMain.width),
        ...afterOutsideTap,
      });
    }
  } finally {
    await touchCtx.close().catch(() => {});
  }

  const touchFlyoutVerified =
    touchResults.length === 2 &&
    touchResults.every(
      (t) =>
        t.duringTap.ariaExpanded === 'true' &&
        t.duringTap.flyoutOpen &&
        t.mainShiftXPx === 0 &&
        t.mainShiftWidthPx === 0 &&
        t.ariaExpandedAfterClose === 'false' &&
        t.flyoutClosedCleanly,
    );

  addCheck('FND-W18-04-TOUCH-TAP-FLYOUT-1024-AND-768-ZERO-SHIFT', touchFlyoutVerified, {
    touchFlyoutVerified,
    salesOrders1024HeaderCheck,
    touchResults,
  });
  if (!touchFlyoutVerified) {
    addFinding({
      id: 'QA-W20-TOUCH-FLYOUT',
      severity: 'HIGH',
      domain: 'Tablet Touch Flyout Navigation',
      ownerAgent: 'AGENT-02',
      summary: 'Touch tap() on Level 1 rail failed to open flyout with 0px <main> shift or failed to close on outside tap',
      evidence: touchResults,
    });
  }
  return { touchFlyoutVerified, salesOrders1024HeaderCheck, touchResults };
}

async function executeLiveChromeAudit({ round, captureScreenshots }) {
  const findings = [];
  const checks = [];
  const addFinding = (f) => findings.push(f);
  const addCheck = (checkId, passed, details = {}) => checks.push({ checkId, passed: Boolean(passed), details });

  // Part A: Static Clean Architecture, Single Mock, Master Report & Migration Hygiene
  auditStaticArchitectureAndHygiene(addFinding, addCheck, round);

  // Verify live servers are reachable before launching Chrome
  const [apiUp, webUp] = await Promise.all([
    checkPortOpen(Number(new URL(BASE_API).port || 5108)),
    checkPortOpen(Number(new URL(BASE_WEB).port || 5173)),
  ]);
  if (!apiUp || !webUp) {
    throw new Error(
      `Live servers are not reachable (API ${BASE_API} up=${apiUp}, Web ${BASE_WEB} up=${webUp}). Start ERP.Api and Vite before running --round=${round}.`,
    );
  }

  const { chromium } = await resolvePlaywright();
  const pdfBuffer = buildValidPdf14Buffer();
  const pdfSha256 = crypto.createHash('sha256').update(pdfBuffer).digest('hex');

  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  });

  for (const staleOrigin of ['http://localhost:5260', 'https://localhost:7198', 'http://127.0.0.1:5067']) {
    await context.route(`${staleOrigin}/**`, async (route) => {
      const redirected = route.request().url().replace(staleOrigin, BASE_API);
      const resp = await route.fetch({ url: redirected });
      await route.fulfill({ response: resp });
    });
  }

  const page = await context.newPage();
  const consoleErrors = [];
  const pageErrors = [];
  const unexpected5xxErrors = [];
  const unexpected4xxErrors = [];
  const expectedNegativePathResponses = [];
  const contrastMeasurements = [];
  const tableScrollMetrics = [];
  const sortHeaderMetrics = [];
  const iconButtonAriaAudits = [];
  const routeHygieneChecks = [];
  const screenshotsManifest = [];

  if (captureScreenshots && fs.existsSync(SCREENSHOT_DIR)) {
    for (const f of fs.readdirSync(SCREENSHOT_DIR)) {
      if (f.toLowerCase().endsWith('.png')) {
        const stalePath = path.join(SCREENSHOT_DIR, f);
        assertAllowedWritePath(stalePath);
        fs.unlinkSync(stalePath);
      }
    }
  }

  let allowExpectedNegativeStatus = false;

  page.on('console', (msg) => {
    if (msg.type() === 'error') {
      const txt = msg.text();
      if (allowExpectedNegativeStatus && txt.includes('Failed to load resource') && /403|409|422/.test(txt)) {
        return;
      }
      consoleErrors.push(txt);
    }
  });
  page.on('pageerror', (err) => pageErrors.push(String(err?.message || err)));
  page.on('response', (resp) => {
    const url = resp.url();
    const status = resp.status();
    if (!url.includes('/api/')) return;
    if (status >= 500) {
      unexpected5xxErrors.push({ status, method: resp.request().method(), url });
    } else if (status >= 400) {
      if (allowExpectedNegativeStatus && [403, 404, 409, 422].includes(status)) {
        expectedNegativePathResponses.push({ status, method: resp.request().method(), url });
        return;
      }
      unexpected4xxErrors.push({ status, method: resp.request().method(), url });
    }
  });

  const maybeShot = async (fileName, meta = {}) => {
    if (!captureScreenshots) return null;
    const info = await capturePureCdpPng(page, fileName);
    const record = { ...info, ...meta, zeroDomMutationsInjected: true };
    screenshotsManifest.push(record);
    return record;
  };

  const maybeStitchedShot = async (fileName, leftBuf, rightBuf, meta = {}) => {
    if (!captureScreenshots) return null;
    const info = await stitchTwoPngBuffersSideBySide(browser, leftBuf, rightBuf, fileName);
    const record = { ...info, ...meta, zeroDomMutationsInjected: true };
    screenshotsManifest.push(record);
    return record;
  };

  const screenshotGeometryProbes = [];
  const probeShotGeometry = async (p, label) => {
    const g = await evaluateTruePixelAndGeometryOnPage(p, label);
    screenshotGeometryProbes.push(g);
    return g;
  };

  let roleMatrixAndSecurity = null;
  let postAuditSqlBaseline = null;
  let axeAuditSummary = { axeViolationCount: -1, allTextContrastPairsPassAA: false, routeResults: [] };
  let eightViewportMatrix = {
    all8ViewportsZeroOverflow: false,
    maxMatrixOverflowPx: -1,
    statusColumnVisibleAtScrollLeft0: false,
    stickyRightWidthRatio: 1,
    clippedWarehouseTextCount: 999,
    customerReturnsZeroHorizontalScrollDesktop: false,
    minTouchTargetHeightPx: 0,
    matrix: [],
    operationalTableMatrix: [],
    geometryMatrixProbes: [],
  };
  let sweep54Summary = {
    sweptCustomerRoutesCount: 0,
    sweptPlatformRoutesCount: 0,
    sweptRoutesTotal: 0,
    routesWithFailuresCount: 999,
    sweepTruncatedCount: 999,
    sweepCrushedHeaderCount: 999,
    sweepStickyOverlapCount: 999,
    sweepRawTechCount: 999,
    sweepMaxOverflowPx: 999,
    sweepConsoleErrorsCount: 999,
    sweepPageErrorsCount: 999,
    sweepHttpErrorsCount: 999,
    failingRoutes: [],
    routeResults: [],
  };
  let touchFlyoutSummary = { touchFlyoutVerified: false, touchResults: [] };
  let wave19A11yMotion = {
    skipLinkWorksOnFirstTab: false,
    modalFocusTrapAndEscapeRestore: false,
    prefersReducedMotionRespected: false,
    details: {},
  };
  const rawEnumScanResults = [];
  let customerPermissionsZeroPlatformRoles = false;
  let duplicateWordPermissionLabelsCount = 999;
  let vietQrCanonicalAccount98418102005Verified = false;
  let qrOpenLayoutShiftPx = 999;
  let vietQrInfoBluePendingVerified = false;
  let invoiceHistoryVisibleAboveFold1440x900 = false;
  let importReceipts375CardListVerified = false;
  let importReceiptsSinglePrimaryCtaMobile = false;
  let mobileFirstCardTopPx375 = 999;
  let mobileFirstCardTopPx390 = 999;
  let mobileSingleRowActionsVerified = false;
  let adminRevokeModalVerified = false;
  let kitReversalModalVerified = false;
  let kpiHeightDiffPx = 999;
  let cleanVnDatePlaceholdersVerified = false;
  let emptyBackordersZeroThead = false;

  try {
    const ownerAuth = await restoreCleanBaselineViaApi(context.request);
    if (!ownerAuth?.ok || ownerAuth.username !== 'qa_admin') {
      throw new Error('Failed to authenticate qa_admin (SuperAdmin) against live ERP.Api');
    }

    const rawRoleProbe = await runAdversarialSecurityAndRoleProbes(
      context.request,
      ownerAuth,
      addFinding,
      addCheck,
    );
    const tenantAdminAuth = rawRoleProbe.roleAuthTokens?.Admin;
    const staffAuth = rawRoleProbe.roleAuthTokens?.WarehouseStaff;
    if (!tenantAdminAuth?.ok || tenantAdminAuth.username !== 'tenant_admin') {
      throw new Error(`Expected Customer Admin login to resolve to tenant_admin; got ${tenantAdminAuth?.username || 'NONE'}`);
    }
    if (!staffAuth?.ok) {
      throw new Error('Failed to authenticate WarehouseStaff (qa_staff_wh01) against live ERP.Api');
    }

    await runIsolatedMultiRoleBrowserProbes(
      browser,
      rawRoleProbe.roleAuthTokens || {},
      addFinding,
      addCheck,
    );

    axeAuditSummary = await runDedicatedAxeAudit(
      browser,
      ownerAuth,
      tenantAdminAuth,
      addFinding,
      addCheck,
    );

    eightViewportMatrix = await runEightViewportZeroOverflowMatrix(
      browser,
      ownerAuth,
      tenantAdminAuth,
      addFinding,
      addCheck,
    );

    wave19A11yMotion = await runWave19AccessibilityAndMotionSuite(
      browser,
      ownerAuth,
      tenantAdminAuth,
      addFinding,
      addCheck,
    );

    sweep54Summary = await runFull54RouteTruePixelGeometrySweep(
      browser,
      ownerAuth,
      tenantAdminAuth,
    );

    roleMatrixAndSecurity = {
      roleProbeSummary: rawRoleProbe.roleProbeSummary,
      sqlBaselinePre: rawRoleProbe.sqlBaselinePre,
    };

    // =========================================================================
    // PORTAL 1: PLATFORM CONTROL CENTER (/platform/*) — STRICTLY qa_admin ONLY
    // W20_01: /platform Localized Events + /platform/billing 1-Step Reconcile Clean Counter
    // W20_02: /platform/billing Consistent Legal Name & Clean Action Col + /platform/tenants Sticky Quota Drawer
    // =========================================================================
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await applyAuthTokenInBrowser(page, ownerAuth.token, ownerAuth.user, true);

    await page.goto(`${BASE_WEB}/platform`, { waitUntil: 'networkidle' });
    await page.getByTestId('platform-control-center-page').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(250);

    const w20_01_overview = await page.evaluate((legacyAcc) => {
      const simulatorCount = document.querySelectorAll('[data-testid="select-platform-role-simulator"]').length;
      const headerSelectCount = document.querySelectorAll('[data-testid="platform-header"] select').length;
      const bodyText = document.body?.innerText || '';
      const headerText = document.querySelector('[data-testid="platform-header"]')?.innerText || bodyText;
      const hasOwnerAuthorityBadge = headerText.includes('Chủ dự án · Toàn quyền nền tảng');
      const hasLegacyPlaceholder = bodyText.includes(legacyAcc);
      const activeUser = window.localStorage.getItem('username');
      const zeroMetricsCount = document.querySelectorAll(
        '.ui-metric[data-zero="true"], .platform-kpi-card[data-zero="true"]',
      ).length;
      const recentEventsCard = document.querySelector('[data-testid="platform-recent-events-card"]');
      const recentEventsVisible = Boolean(recentEventsCard && recentEventsCard.getBoundingClientRect().height > 40);
      const hasRawAuthEvent = /Authentication\.|UserSession#|\bDLQ\b|\bOutbox\b|Full WMS/i.test(bodyText);
      const hasConsistentLegalName = bodyText.includes('Công ty TNHH Thương mại & Kho vận Đại Phát');
      return {
        simulatorCount,
        headerSelectCount,
        hasOwnerAuthorityBadge,
        hasLegacyPlaceholder,
        activeUser,
        zeroMetricsCount,
        recentEventsVisible,
        hasRawAuthEvent,
        hasConsistentLegalName,
      };
    }, LEGACY_PLACEHOLDER_ACC);

    const soloHeaderAndBalancedOverviewOk =
      w20_01_overview.simulatorCount === 0 &&
      w20_01_overview.headerSelectCount === 0 &&
      w20_01_overview.hasOwnerAuthorityBadge &&
      !w20_01_overview.hasLegacyPlaceholder &&
      w20_01_overview.activeUser === 'qa_admin' &&
      w20_01_overview.zeroMetricsCount >= 1 &&
      w20_01_overview.recentEventsVisible &&
      !w20_01_overview.hasRawAuthEvent;
    addCheck('FND-W20-02-PLATFORM-LOCALIZED-EVENTS-AND-SOLO-OWNER', soloHeaderAndBalancedOverviewOk, w20_01_overview);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/platform@1440x900'));
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/platform', '1440x900')));
    iconButtonAriaAudits.push(await auditIconOnlyButtons(page, 'W20_01A:/platform@1440x900'));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/platform@1440x900'));
    await probeShotGeometry(page, 'W20_01A:/platform@1440x900');
    const w20_01Left = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    await page.goto(`${BASE_WEB}/platform/billing`, { waitUntil: 'networkidle' });
    await page.getByTestId('platform-billing-invoices-table').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(250);

    const { novInvoiceId, existingPaidBankRef } = await resolveBillingMetadata(context.request, ownerAuth.token);
    contrastMeasurements.push(
      await measureButtonContrast(
        page,
        `[data-testid="btn-reconcile-saas-invoice-${novInvoiceId}"]`,
        '/platform/billing',
        'Platform Reconcile Trigger CTA',
      ),
    );
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/platform/billing', '1440x900')));

    await page
      .locator('tr', { hasText: 'INV-SAAS-202611-002' })
      .locator('[data-testid^="btn-reconcile-saas-invoice-"]')
      .first()
      .click();
    const reconcileModal = page.getByTestId('modal-confirm-invoice-reconcile');
    await reconcileModal.waitFor({ state: 'visible', timeout: 10000 });

    await page.getByTestId('input-reconcile-bank-ref').fill('FT2627716888');
    await page.getByTestId('input-reconcile-received-amount').fill('1490000');
    await page.getByTestId('input-reconcile-attachment-file').setInputFiles({
      name: 'UNC_VCB_20261102_1490k.pdf',
      mimeType: 'application/pdf',
      buffer: pdfBuffer,
    });
    await page.getByTestId('input-reconcile-note').fill('Đối soát 1 bước bởi Chủ dự án kỳ 11/2026 khớp 1.490.000 ₫');
    await page.waitForTimeout(200);

    const w20_01_modal = await reconcileModal.evaluate((modalEl, legacyAcc) => {
      const modalText = modalEl.innerText || '';
      const pageText = document.body?.innerText || '';
      const hasFourEyesSection =
        modalText.includes('Kiểm soát nội bộ (4 mắt)') || modalText.includes('Người kiểm soát duyệt');
      const approverSelectVisible = Boolean(modalEl.querySelector('[data-testid="input-reconcile-approver"]'));
      const nativeDateInputs = modalEl.querySelectorAll('input[type="date"], input[type="datetime-local"]').length;
      const creditInput = modalEl.querySelector('[data-testid="input-reconcile-credit-time"]');
      const creditTimePlaceholder = creditInput?.getAttribute('placeholder') || '';
      const hasCanonical98418102005 = pageText.includes('98418102005') || modalText.includes('98418102005');
      const hasLegacyPlaceholder = pageText.includes(legacyAcc) || modalText.includes(legacyAcc);
      const hasMisleadingSlashMinCounter = /\b\d+\/\d+\s*ký tự tối thiểu/i.test(modalText);
      const hasConsistentLegalName = pageText.includes('Công ty TNHH Thương mại & Kho vận Đại Phát');
      return {
        hasFourEyesSection,
        approverSelectVisible,
        nativeDateInputs,
        creditTimePlaceholder,
        hasCanonical98418102005,
        hasLegacyPlaceholder,
        hasMisleadingSlashMinCounter,
        hasConsistentLegalName,
      };
    }, LEGACY_PLACEHOLDER_ACC);

    const fourEyesHiddenOk =
      !w20_01_modal.hasFourEyesSection &&
      !w20_01_modal.approverSelectVisible &&
      w20_01_modal.nativeDateInputs === 0 &&
      w20_01_modal.creditTimePlaceholder === 'dd/MM/yyyy HH:mm' &&
      !w20_01_modal.creditTimePlaceholder.includes('VD:') &&
      !w20_01_modal.hasMisleadingSlashMinCounter &&
      !w20_01_modal.hasLegacyPlaceholder;
    addCheck('UI-PLATFORM-SUPERADMIN-NO-4EYES-MODAL', fourEyesHiddenOk, w20_01_modal);

    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/platform/billing#reconcile-modal@1440x900'));
    iconButtonAriaAudits.push(await auditIconOnlyButtons(page, 'W20_01B:ReconcileModal@1440x900'));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/platform/billing#modal@1440x900'));
    await probeShotGeometry(page, 'W20_01B:/platform/billing#reconcile-modal@1440x900');
    const w20_01Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;
    if (captureScreenshots && w20_01Left && w20_01Right) {
      await maybeStitchedShot(
        'W20_01_Platform_Overview_LocalizedEvents_And_OneStepReconcile_CleanCounter_1440x900.png',
        w20_01Left,
        w20_01Right,
        {
          route: '/platform + /platform/billing#modal',
          viewport: '1440x900 (2-up side-by-side)',
          actor: 'qa_admin',
          checks: { overview: w20_01_overview, modal: w20_01_modal },
        },
      );
    }

    await page.getByTestId('input-reconcile-credit-time').fill('02/11/2026 18:15');

    allowExpectedNegativeStatus = true;
    const dupApiRes = await context.request.post(
      `${BASE_API}/api/v1/platform/billing/invoices/${novInvoiceId}/reconcile`,
      {
        headers: { Authorization: `Bearer ${ownerAuth.token}` },
        data: {
          bankTransactionRef: existingPaidBankRef,
          bankCreditTimestampVi: '02/11/2026 18:15',
          receivedAmountVnd: 1490000,
          reasonNote: 'Xác nhận bút toán chuyển khoản cước thuê bao kỳ 11/2026',
        },
      },
    );
    addCheck('API-BILLING-DUPLICATE-BANKREF-409', dupApiRes.status() === 409, { status: dupApiRes.status() });
    allowExpectedNegativeStatus = false;

    const [reconcileResp] = await Promise.all([
      page.waitForResponse(
        (r) =>
          r.url().includes('/platform/billing/invoices/') &&
          r.url().includes('/reconcile') &&
          r.request().method() === 'POST',
        { timeout: 15000 },
      ),
      page.getByTestId('btn-submit-manual-reconcile').click(),
    ]);
    const reconcileStatus = reconcileResp.status();
    let reconcileBody = {};
    try {
      reconcileBody = await reconcileResp.json();
    } catch {
      reconcileBody = {};
    }
    const bodyStr = JSON.stringify(reconcileBody);
    await page.getByTestId('platform-billing-success-toast').waitFor({ state: 'visible', timeout: 8000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(250);

    const w20_02_toastCheck = await page.evaluate(() => {
      const toast = document.querySelector('[data-testid="platform-billing-success-toast"]');
      const banner = document.querySelector('[data-testid="platform-action-notice"]');
      const bodyText = document.body?.innerText || '';
      const toastRect = toast?.getBoundingClientRect();
      const bannerRect = banner?.getBoundingClientRect();
      const invTable = document.querySelector('[data-testid="platform-billing-invoices-table"]');
      const lastColCells = invTable
        ? Array.from(invTable.querySelectorAll('tbody tr')).map((tr) => {
            const tds = tr.querySelectorAll('td');
            return (tds[tds.length - 1]?.innerText || '').trim();
          })
        : [];
      const actionColHasLongStamp = lastColCells.some((t) => t.includes('Xác nhận thủ công bởi Chủ dự án'));
      return {
        toastVisible: Boolean(
          toast &&
            toastRect &&
            toastRect.width > 40 &&
            toastRect.height >= 14 &&
            (!bannerRect || bannerRect.height >= 32),
        ),
        toastRole: toast?.getAttribute('role') || banner?.getAttribute('role') || '',
        toastAriaLive: toast?.getAttribute('aria-live') || banner?.getAttribute('aria-live') || '',
        toastText: (toast?.textContent || '').trim(),
        hasOwnerAuditStampInDom: bodyText.includes('Xác nhận thủ công bởi Chủ dự án lúc'),
        hasConsistentLegalName: bodyText.includes('Công ty TNHH Thương mại & Kho vận Đại Phát'),
        actionColClean: !actionColHasLongStamp,
      };
    });
    const hasOwnerAuditStamp =
      bodyStr.includes('Xác nhận thủ công bởi Chủ dự án lúc') || w20_02_toastCheck.hasOwnerAuditStampInDom;
    addCheck(
      'BE-SOLO-OWNER-1STEP-RECONCILE-200-AND-ACTIVE-SUCCESS-TOAST',
      reconcileStatus === 200 &&
        hasOwnerAuditStamp &&
        w20_02_toastCheck.toastVisible &&
        w20_02_toastCheck.toastRole === 'status' &&
        w20_02_toastCheck.toastAriaLive === 'polite' &&
        w20_02_toastCheck.hasConsistentLegalName &&
        w20_02_toastCheck.actionColClean,
      { status: reconcileStatus, hasOwnerAuditStamp, pdfSha256, toastCheck: w20_02_toastCheck },
    );
    await probeShotGeometry(page, 'W20_02A:/platform/billing#reconciled@1440x900');
    const w20_02Left = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    await page.goto(`${BASE_WEB}/platform/tenants`, { waitUntil: 'networkidle' });
    await page.getByTestId('btn-open-quota-modal-1').waitFor({ state: 'visible', timeout: 15000 });
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/platform/tenants', '1440x900')));
    await page.getByTestId('btn-open-quota-modal-1').click();
    const quotaDrawer = page.getByTestId('drawer-adjust-tenant-quota');
    await quotaDrawer.waitFor({ state: 'visible', timeout: 10000 });
    await page.getByTestId('input-quota-max-warehouses').fill('5');
    await page.getByTestId('input-quota-contract-ref').fill('PLHD-2026-11-01');
    await page.getByTestId('input-quota-reason').fill('Điều chỉnh mở rộng hạn mức kho vận theo phụ lục hợp đồng thuê bao năm 2026');
    await page.waitForTimeout(200);

    const w20_02_quotaDrawerCheck = await quotaDrawer.evaluate((drawerEl) => {
      const effInput = drawerEl.querySelector('[data-testid="input-quota-effective-until"]');
      const effPlaceholder = effInput?.getAttribute('placeholder') || '';
      const numInputs = Array.from(drawerEl.querySelectorAll('input[type="number"]'));
      const numericTextInputs = Array.from(drawerEl.querySelectorAll('input[inputmode="numeric"]'));
      const allNumberInputsSpinnerless =
        numInputs.length + numericTextInputs.length >= 3 &&
        numInputs.every((inp) => {
          const spinStyle = window.getComputedStyle(inp, '::-webkit-inner-spin-button');
          const elStyle = window.getComputedStyle(inp);
          return spinStyle.webkitAppearance === 'none' || spinStyle.appearance === 'none' || elStyle.appearance === 'textfield';
        });
      const pricingImpactVisible = Boolean(drawerEl.querySelector('[data-testid="quota-pricing-impact-note"]'));
      const drawerText = drawerEl.innerText || '';
      const stickyEls = Array.from(drawerEl.querySelectorAll('*')).filter(
        (el) => window.getComputedStyle(el).position === 'sticky',
      );
      return {
        effectiveUntilPlaceholder: effPlaceholder,
        numberInputsCount: numInputs.length,
        numericTextInputsCount: numericTextInputs.length,
        allNumberInputsSpinnerless,
        pricingImpactVisible,
        hasConsistentLegalName: drawerText.includes('Công ty TNHH Thương mại & Kho vận Đại Phát'),
        hasStickyFooter: stickyEls.length >= 1,
      };
    });

    cleanVnDatePlaceholdersVerified =
      w20_01_modal.creditTimePlaceholder === 'dd/MM/yyyy HH:mm' &&
      !w20_01_modal.creditTimePlaceholder.includes('VD:') &&
      w20_02_quotaDrawerCheck.effectiveUntilPlaceholder === 'dd/MM/yyyy' &&
      !w20_02_quotaDrawerCheck.effectiveUntilPlaceholder.includes('VD:') &&
      w20_02_quotaDrawerCheck.allNumberInputsSpinnerless &&
      w20_02_quotaDrawerCheck.hasStickyFooter;

    addCheck('FND-W20-02-CLEAN-VN-DATE-PLACEHOLDERS-AND-STICKY-QUOTA-DRAWER', cleanVnDatePlaceholdersVerified, {
      creditTimePlaceholder: w20_01_modal.creditTimePlaceholder,
      ...w20_02_quotaDrawerCheck,
    });

    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/platform/tenants#quota-drawer@1440x900'));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/platform/tenants#quota-drawer@1440x900'));
    await probeShotGeometry(page, 'W20_02B:/platform/tenants#quota-drawer@1440x900');
    const w20_02Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;
    if (captureScreenshots && w20_02Left && w20_02Right) {
      await maybeStitchedShot(
        'W20_02_Platform_Billing_ConsistentLegalName_CleanActionCol_And_StickyQuotaDrawer_1440x900.png',
        w20_02Left,
        w20_02Right,
        {
          route: '/platform/billing#toast + /platform/tenants#quota-drawer',
          viewport: '1440x900 (2-up side-by-side)',
          actor: 'qa_admin',
          checks: { toast: w20_02_toastCheck, quotaDrawer: w20_02_quotaDrawerCheck },
        },
      );
    }

    await page.getByTestId('input-quota-effective-until').fill('02/11/2027');
    await page.getByTestId('btn-close-quota-drawer').click();
    await page.getByTestId('dirty-dismiss-confirm').waitFor({ state: 'visible', timeout: 5000 });
    await page.getByTestId('btn-confirm-discard-quota').click();
    await quotaDrawer.waitFor({ state: 'hidden', timeout: 5000 });

    await context.request.post(`${BASE_API}/api/v1/platform/billing/invoices/${novInvoiceId}/reconcile`, {
      headers: { Authorization: `Bearer ${ownerAuth.token}` },
      data: { resetToPending: true },
    });

    // =========================================================================
    // PORTAL 2: CUSTOMER WMS PORTAL (/*) — STRICTLY tenant_admin (owner@erpkho.vn) ONLY!
    // =========================================================================
    await page.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await page.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await applyAuthTokenInBrowser(page, tenantAdminAuth.token, tenantAdminAuth.user, true);

    // =========================================================================
    // W20_03 & W20_04: /saas-billing — Zero Truncated Titles, Light Placeholders, Invoice Table + Zero-Layout-Shift QR Modal & Info Blue Pending
    // =========================================================================
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE_WEB}/saas-billing`, { waitUntil: 'networkidle' });
    await page.getByTestId('saas-invoice-history-table').waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(300);

    const w20_03_idle = await page.evaluate((legacyAcc) => {
      const qrPanelCount = document.querySelectorAll(
        '[data-testid="vietqr-payment-modal"], [data-testid="vietqr-payment-panel"], [data-testid="saas-vietqr-payment-panel"], [data-testid="vietqr-qr-box"]',
      ).length;
      const bodyText = document.body?.innerText || '';
      const hasAutoCountdown15 = /\b1[45]:\d{2}\b/.test(bodyText);
      const hasGenerateBtn = Boolean(document.querySelector('[data-testid="btn-generate-vietqr"]'));
      const activeUsername = window.localStorage.getItem('username');
      const hasLegacyPlaceholder = bodyText.includes(legacyAcc);

      const invTbl = document.querySelector('[data-testid="saas-invoice-history-table"]');
      const invRect = invTbl?.getBoundingClientRect();
      const firstInvRow = invTbl?.querySelector('tbody tr');
      const firstRowRect = firstInvRow?.getBoundingClientRect();
      const invoiceTableAboveFold = Boolean(
        invRect && invRect.top > 0 && invRect.top < 860 && firstRowRect && firstRowRect.top < 895,
      );

      const legendDotsCount = document.querySelectorAll('.ui-legend-dot').length;
      const quotaUsersText = (document.querySelector('[data-testid="quota-card-users"]')?.textContent || '').trim();
      const hasUnifiedNhanSuQuota =
        quotaUsersText.includes('Tài khoản nhân sự') && bodyText.includes('tài khoản nhân sự');
      const hasCleanTaxpayerLabel =
        bodyText.includes('Doanh nghiệp / Tổ chức (MST 10 hoặc 13 số)') && !/ENTERPRISE\s*[—–-]/.test(bodyText);
      const h1Text = (document.querySelector('main h1')?.textContent || '').trim();

      const thTexts = invTbl ? Array.from(invTbl.querySelectorAll('thead th')).map((th) => (th.textContent || '').trim()) : [];
      const hasDedicatedMstColumn = thTexts.some((t) => t.includes('MST người mua'));
      const taxSubLineEl = invTbl?.querySelector('[data-testid^="invoice-tax-detail-"]');
      const taxSubLineText = (taxSubLineEl?.textContent || '').trim();
      const hasTwoLineAmountCell = taxSubLineText.includes('Trước thuế:') && taxSubLineText.includes('VAT:');
      const invScroll = invTbl?.closest('.ui-table-scroll');
      const invoiceTableZeroHorizontalClip1440 = invScroll
        ? invScroll.scrollWidth <= invScroll.clientWidth + 1
        : true;

      return {
        scrollY: window.scrollY,
        h1Text,
        qrPanelCount,
        hasAutoCountdown15,
        hasGenerateBtn,
        activeUsername,
        hasLegacyPlaceholder,
        invoiceTableTopPx: invRect ? invRect.top : null,
        firstInvoiceRowTopPx: firstRowRect ? Math.round(firstRowRect.top) : null,
        invoiceTableAboveFold,
        invoiceTableScrollWidthPx: invScroll?.scrollWidth || 0,
        invoiceTableClientWidthPx: invScroll?.clientWidth || 0,
        invoiceTableZeroHorizontalClip1440,
        legendDotsCount,
        hasUnifiedNhanSuQuota,
        hasCleanTaxpayerLabel,
        hasDedicatedMstColumn,
        hasTwoLineAmountCell,
        taxSubLineText,
      };
    }, LEGACY_PLACEHOLDER_ACC);

    invoiceHistoryVisibleAboveFold1440x900 = Boolean(w20_03_idle.invoiceTableAboveFold);

    const onDemandIdleAndCleanBillingOk =
      w20_03_idle.qrPanelCount === 0 &&
      !w20_03_idle.hasAutoCountdown15 &&
      w20_03_idle.hasGenerateBtn &&
      w20_03_idle.activeUsername === 'tenant_admin' &&
      !w20_03_idle.hasLegacyPlaceholder &&
      invoiceHistoryVisibleAboveFold1440x900 &&
      w20_03_idle.invoiceTableZeroHorizontalClip1440 &&
      w20_03_idle.legendDotsCount === 5 &&
      w20_03_idle.hasUnifiedNhanSuQuota &&
      w20_03_idle.hasCleanTaxpayerLabel &&
      w20_03_idle.hasDedicatedMstColumn &&
      w20_03_idle.hasTwoLineAmountCell;

    addCheck('FND-W20-04-SAAS-BILLING-IDLE-CLEAN-TITLES-AND-INVOICE-TABLE', onDemandIdleAndCleanBillingOk, w20_03_idle);
    if (!onDemandIdleAndCleanBillingOk) {
      addFinding({
        id: 'QA-W20-SAAS-BILLING-03',
        severity: 'MEDIUM',
        domain: 'SaaS Billing Above-the-Fold, Flat Dot Legend & 2-Line Invoice Cell',
        ownerAgent: 'AGENT-02',
        summary: 'SaaS Billing failed above-the-fold invoice table visibility at 1440x900, flat dot legend, or 2-line invoice amount cell',
        evidence: w20_03_idle,
      });
    }

    contrastMeasurements.push(
      await measureButtonContrast(
        page,
        '[data-testid="btn-generate-vietqr"]',
        '/saas-billing',
        'Customer SaaS Billing Generate VietQR Primary CTA',
      ),
    );
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/saas-billing#idle@1440x900'));
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/saas-billing', '1440x900')));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/saas-billing#idle@1440x900'));
    await probeShotGeometry(page, 'W20_03:/saas-billing#idle@1440x900');
    await maybeShot('W20_03_Customer_SaasBilling_ZeroTruncatedTitles_LightPlaceholders_And_InvoiceTable_1440x900.png', {
      route: '/saas-billing#idle-hidden',
      viewport: '1440x900',
      actor: 'tenant_admin',
      checks: w20_03_idle,
    });

    const beforeQrTopPx = await page.evaluate(() => {
      const invTbl = document.querySelector('[data-testid="saas-invoice-history-table"]');
      return invTbl ? invTbl.getBoundingClientRect().top : -1;
    });
    await page.getByTestId('btn-generate-vietqr').first().click();
    const qrModal = page.getByTestId('vietqr-payment-modal');
    await qrModal.waitFor({ state: 'visible', timeout: 10000 });
    await page.waitForTimeout(220);

    const w20_04_open = await page.evaluate(
      ({ legacyAcc, beforeTop }) => {
        const modal = document.querySelector('[data-testid="vietqr-payment-modal"]');
        const panel = document.querySelector('[data-testid="vietqr-payment-panel"]');
        const invTbl = document.querySelector('[data-testid="saas-invoice-history-table"]');
        const afterTop = invTbl ? invTbl.getBoundingClientRect().top : -999;
        const layoutShiftPx = beforeTop >= 0 && afterTop > -900 ? Math.round(Math.abs(afterTop - beforeTop)) : 999;
        const pollingBanner = document.querySelector('[data-testid="vietqr-auto-polling-indicator"]');
        const pollingCs = pollingBanner ? window.getComputedStyle(pollingBanner) : null;
        const panelText = panel?.innerText || '';
        const bodyText = document.body?.innerText || '';
        const bankDisplay = (document.querySelector('[data-testid="vietqr-bank-display"]')?.textContent || '').trim();
        const accountHolder = (document.querySelector('[data-testid="vietqr-account-holder"]')?.textContent || '').trim();
        const accountNumber = (document.querySelector('[data-testid="vietqr-account-number"]')?.textContent || '').trim();
        return {
          modalRole: modal?.getAttribute('role') || '',
          ariaModal: modal?.getAttribute('aria-modal') || '',
          beforeTopPx: Math.round(beforeTop),
          afterTopPx: Math.round(afterTop),
          qrOpenLayoutShiftPx: layoutShiftPx,
          pollingColor: pollingCs?.color || '',
          pollingBgColor: pollingCs?.backgroundColor || '',
          infoBluePendingBanner:
            pollingCs?.color === 'rgb(3, 105, 161)' && pollingCs?.backgroundColor === 'rgb(240, 249, 255)',
          bankDisplay,
          accountHolder,
          accountNumber,
          hasTpBank: panelText.includes('TPBank'),
          hasBin970423: panelText.includes('970423'),
          hasAccount98418102005: accountNumber.includes('98418102005') && panelText.includes('98418102005'),
          hasHolderHuynhPhiVan: accountHolder === 'HUYNH PHI VAN' && panelText.includes('HUYNH PHI VAN'),
          hasAmount1490k: panelText.includes('1.490.000'),
          hasVatBreakdown: panelText.includes('1.354.545') && panelText.includes('135.455'),
          hasLegacyPlaceholder: bodyText.includes(legacyAcc) || panelText.includes(legacyAcc),
          hasCloseBtn: Boolean(document.querySelector('[data-testid="btn-close-vietqr-panel"]')),
        };
      },
      { legacyAcc: LEGACY_PLACEHOLDER_ACC, beforeTop: beforeQrTopPx },
    );

    qrOpenLayoutShiftPx = w20_04_open.qrOpenLayoutShiftPx;
    vietQrInfoBluePendingVerified = Boolean(
      w20_04_open.modalRole === 'dialog' &&
        w20_04_open.ariaModal === 'true' &&
        w20_04_open.infoBluePendingBanner,
    );

    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/saas-billing#vietqr-modal@1440x900'));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/saas-billing#vietqr-modal@1440x900'));
    await probeShotGeometry(page, 'W20_04:/saas-billing#vietqr-modal@1440x900');
    await maybeShot('W20_04_Customer_SaasBilling_OnDemandQrModal_ZeroLayoutShift_InfoBluePending_1440x900.png', {
      route: '/saas-billing#vietqr-modal',
      viewport: '1440x900',
      actor: 'tenant_admin',
      checks: w20_04_open,
    });

    await page.getByTestId('btn-close-vietqr-panel').first().click();
    await page.waitForTimeout(200);
    const closedQrCount = await page.locator('[data-testid="vietqr-payment-modal"]').count();
    vietQrCanonicalAccount98418102005Verified =
      w20_04_open.hasTpBank &&
      w20_04_open.hasBin970423 &&
      w20_04_open.hasAccount98418102005 &&
      w20_04_open.hasHolderHuynhPhiVan &&
      w20_04_open.hasAmount1490k &&
      w20_04_open.hasVatBreakdown &&
      !w20_04_open.hasLegacyPlaceholder &&
      qrOpenLayoutShiftPx === 0 &&
      vietQrInfoBluePendingVerified &&
      closedQrCount === 0;

    addCheck('FND-W20-04-VIETQR-MODAL-ZERO-SHIFT-INFOBLUE-AND-CANONICAL-98418102005', vietQrCanonicalAccount98418102005Verified, {
      ...w20_04_open,
      closedCleanly: closedQrCount === 0,
    });

    // =========================================================================
    // W20_05: /permissions — 100% Vietnamese Role Tabs, Module Filter Bar, Switch Pills, No Nested Scroll & Admin Revoke Modal
    // =========================================================================
    const permCatalogApiResp = await context.request.get(`${BASE_API}/api/v1/permissions`, {
      headers: { Authorization: `Bearer ${tenantAdminAuth.token}` },
    });
    const permCatalogJson = permCatalogApiResp.status() === 200 ? await permCatalogApiResp.json() : [];
    const permItems = Array.isArray(permCatalogJson) ? permCatalogJson : permCatalogJson.items || [];
    const dupWordRegex = /\b(phê duyệt\s+phê duyệt|kiểm tra\s+kiểm tra|xác nhận\s+xác nhận|tạo\s+tạo|xem\s+xem|xóa\s+xóa|cập nhật\s+cập nhật)\b/i;
    const apiDuplicatePermissionItems = permItems.filter((p) =>
      dupWordRegex.test(`${p.name || ''} ${p.description || ''}`),
    );

    await page.goto(`${BASE_WEB}/permissions`, { waitUntil: 'networkidle' });
    const roleTabsVisible = await page
      .getByTestId('permissions-role-tabs')
      .waitFor({ state: 'visible', timeout: 8000 })
      .then(() => true)
      .catch(() => false);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(220);

    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/permissions@1440x900'));
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/permissions', '1440x900')));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/permissions@1440x900'));
    await probeShotGeometry(page, 'W20_05:/permissions@1440x900');

    const permDefaultCheck = await page.evaluate((tabsVisible) => {
      const chips = Array.from(
        document.querySelectorAll('[data-testid="permissions-role-tabs"] button[role="tab"]'),
      ).map((b) => (b.textContent || '').trim());
      const bodyText = document.body?.innerText || '';
      const alertText = (document.querySelector('[role="alert"]')?.textContent || '').trim();
      const hasPlatformRoleLeaked = ['SuperAdmin', 'BillingAdmin', 'SupportAdmin', 'SecurityAuditor', 'Chủ dự án'].some(
        (pr) => chips.some((c) => c.includes(pr)) || bodyText.includes(pr),
      );
      const expectedViRoleChips = [
        'Quản trị công ty (141)',
        'Quản lý kho (98)',
        'Giám sát & QC (54)',
        'Kế toán kho (42)',
        'Nhân viên kho (36)',
        'Kiểm toán nội bộ (18)',
      ];
      const all6Matched = expectedViRoleChips.every((sub) => chips.some((c) => c.includes(sub)));
      const nestedScrollCount = document.querySelectorAll('div[role="region"][aria-label^="Vùng cuộn danh sách quyền"]').length;
      const hasModuleFilterBar = Boolean(document.querySelector('[data-testid="permissions-module-filter-bar"]'));
      const hasDiffDefaultToggle = Boolean(document.querySelector('[data-testid="btn-toggle-diff-default"]'));
      const switchPillsCount = document.querySelectorAll('table.permissions-table button[role="switch"]').length;
      return {
        roleTabsVisible: tabsVisible,
        chipCount: chips.length,
        chips,
        all6Matched,
        hasPlatformRoleLeaked,
        nestedScrollCount,
        hasModuleFilterBar,
        hasDiffDefaultToggle,
        switchPillsCount,
        alertText,
        topbarText: (document.querySelector('.topbar-context-text')?.textContent || '').trim(),
      };
    }, roleTabsVisible);

    await maybeShot('W20_05_Customer_Permissions_VietnameseRoleTabs_ModuleFilter_NoNestedScroll_1440x900.png', {
      route: '/permissions',
      viewport: '1440x900',
      actor: 'tenant_admin',
      checks: permDefaultCheck,
    });

    const allPill = page.getByTestId('perm-module-pill-ALL');
    if ((await allPill.count()) > 0) {
      await allPill.click();
      await page.waitForTimeout(180);
    }

    const permAllCatalogCheck = await page.evaluate(() => {
      const bodyText = document.body?.innerText || '';
      const dupDomRegex = /\b(phê duyệt\s+phê duyệt|kiểm tra\s+kiểm tra|xác nhận\s+xác nhận|tạo\s+tạo|xem\s+xem|xóa\s+xóa|cập nhật\s+cập nhật)\b/i;
      const domCellsWithDupWords = Array.from(document.querySelectorAll('table.permissions-table tbody td:first-child'))
        .map((s) => (s.textContent || '').trim())
        .filter((txt) => dupDomRegex.test(txt));
      return {
        domCellsWithDupWords,
        hasNormalizedApprovalLabel: bodyText.includes('Phê duyệt phiếu nhập kho'),
      };
    });

    const firstAdminSwitch = page.locator('table.permissions-table button[role="switch"][aria-checked="true"]').first();
    if ((await firstAdminSwitch.count()) > 0) {
      await firstAdminSwitch.click();
      const revokeModal = page.getByTestId('admin-revoke-confirm-modal');
      const opened = await revokeModal.waitFor({ state: 'visible', timeout: 5000 }).then(() => true).catch(() => false);
      if (opened) {
        const modalAttrs = await revokeModal.evaluate((el) => ({
          role: el.getAttribute('role') || '',
          ariaModal: el.getAttribute('aria-modal') || '',
        }));
        await page.keyboard.press('Escape');
        await page.waitForTimeout(180);
        const closed = (await revokeModal.count()) === 0;
        adminRevokeModalVerified = modalAttrs.role === 'dialog' && modalAttrs.ariaModal === 'true' && closed;
      }
    }

    const permCheck = { ...permDefaultCheck, ...permAllCatalogCheck, adminRevokeModalVerified };
    duplicateWordPermissionLabelsCount =
      apiDuplicatePermissionItems.length + permAllCatalogCheck.domCellsWithDupWords.length;

    await page.goto(`${BASE_WEB}/users`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(350);
    const usersDomCheck = await page.evaluate(() => {
      const sel = document.querySelector('select[aria-label="ID người dùng cần kiểm tra bảo mật"]');
      const opts = sel ? Array.from(sel.querySelectorAll('option')).map((o) => (o.textContent || '').trim()) : [];
      const hasTenantAdmin = opts.some((t) => t.includes('tenant_admin'));
      const leakedPlatformUsers = opts.filter((t) =>
        ['qa_admin (', 'qa_billing_admin', 'qa_support_admin', 'qa_security_auditor'].some((p) => t.includes(p)),
      );
      return { optionCount: opts.length, hasTenantAdmin, leakedPlatformUsers };
    });

    customerPermissionsZeroPlatformRoles =
      permCheck.roleTabsVisible &&
      permCheck.chipCount === 6 &&
      permCheck.all6Matched &&
      !permCheck.hasPlatformRoleLeaked &&
      permCheck.nestedScrollCount === 0 &&
      permCheck.hasModuleFilterBar &&
      permCheck.hasDiffDefaultToggle &&
      permCheck.switchPillsCount >= 1 &&
      adminRevokeModalVerified &&
      permCheck.hasNormalizedApprovalLabel &&
      duplicateWordPermissionLabelsCount === 0 &&
      usersDomCheck.hasTenantAdmin &&
      usersDomCheck.leakedPlatformUsers.length === 0;
    addCheck('FND-W20-03-UI-CUSTOMER-PERMISSIONS-VIETNAMESE-TABS-MODULE-FILTER-AND-REVOKE-MODAL', customerPermissionsZeroPlatformRoles, {
      duplicateWordPermissionLabelsCount,
      apiDuplicatePermissionItems,
      permCheck,
      usersDomCheck,
    });

    // Verify /sales-orders, /cost-valuation, and /misa-exports parity checks at 1440x900
    await page.goto(`${BASE_WEB}/sales-orders`, { waitUntil: 'networkidle' });
    await page.getByTestId('sales-orders-table').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(250);
    contrastMeasurements.push(
      await measureButtonContrast(
        page,
        '[data-testid="btn-open-create-so"]',
        '/sales-orders',
        'Sales Orders Create Primary CTA',
      ),
    );
    const soSort1440 = await measureSortHeadersOnPage(page, '/sales-orders', '1440x900');
    sortHeaderMetrics.push(soSort1440);
    const soContentCheck = await page.evaluate(() => {
      const txt = document.body?.innerText || '';
      return {
        hasSo001: txt.includes('SO-202611-001'),
        hasSo002: txt.includes('SO-202611-002'),
      };
    });
    addCheck(
      'FND-W18-05-SALES-ORDERS-SORT-SPACING-AND-SO202611',
      soSort1440.collisionCount === 0 &&
        soSort1440.minPaddingRightPx >= 14 &&
        soSort1440.minGapPx >= 6 &&
        soContentCheck.hasSo001 &&
        soContentCheck.hasSo002,
      { soSort1440, soContentCheck },
    );
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/sales-orders@1440x900'));
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/sales-orders', '1440x900')));
    iconButtonAriaAudits.push(await auditIconOnlyButtons(page, 'SalesOrders@1440x900'));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/sales-orders@1440x900'));

    await page.goto(`${BASE_WEB}/cost-valuation`, { waitUntil: 'networkidle' });
    await page.getByTestId('wac-inventory-kpi-grid').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(250);
    const wacCheck = await page.evaluate(() => {
      const txt = document.body?.innerText || '';
      const firstMetricVal = (
        document.querySelector('[data-testid="wac-inventory-kpi-grid"] .ui-metric-value')?.textContent || ''
      ).trim();
      const secondMetricVal = (
        document.querySelectorAll('[data-testid="wac-inventory-kpi-grid"] .ui-metric-value')?.[1]?.textContent || ''
      ).trim();
      return {
        hasWh01Label: txt.includes('Kho Trung tâm (WH01)'),
        firstMetricVal,
        secondMetricVal,
        hasNonZeroPortfolioValue:
          Boolean(firstMetricVal) &&
          firstMetricVal !== '0 đ' &&
          firstMetricVal !== '0 ₫' &&
          !txt.includes('Chưa có dữ liệu') &&
          /[1-9][\d.]{5,}\s*[đ₫]/.test(firstMetricVal),
        hasPositiveSkuCount: /^[1-9]\d*\s+mặt hàng$/.test(secondMetricVal),
      };
    });
    const wacParityOk =
      wacCheck.hasWh01Label && wacCheck.hasNonZeroPortfolioValue && wacCheck.hasPositiveSkuCount;
    addCheck('FND-W18-05-COST-VALUATION-WH01-NONZERO-WAC-PARITY', wacParityOk, wacCheck);
    if (!wacParityOk) {
      addFinding({
        id: 'QA-W19-COST-DTO-01',
        severity: 'HIGH',
        domain: 'Layer 4 Frontend-Backend JSON Contract Parity (/cost-valuation)',
        ownerAgent: 'AGENT-02',
        summary:
          'CostValuationHelpers.ts:17-27 omits item.movingAverageCost and item.totalValuation returned by GET /api/v1/inventory-reconciliation/valuation, rendering 0 đ / Chưa có dữ liệu instead of live 145.310.000 đ WAC portfolio',
        evidence: wacCheck,
      });
    }
    tableScrollMetrics.push(...(await measureVisibleTablesAtScrollZero(page, '/cost-valuation', '1440x900')));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/cost-valuation@1440x900'));

    await page.goto(`${BASE_WEB}/misa-exports`, { waitUntil: 'networkidle' });
    await page.getByTestId('btn-export-misa-xlsx').waitFor({ state: 'visible', timeout: 15000 });
    contrastMeasurements.push(
      await measureButtonContrast(
        page,
        '[data-testid="btn-export-misa-xlsx"]',
        '/misa-exports',
        'MISA Excel Export Primary CTA',
      ),
    );

    // =========================================================================
    // W20_06: /kit-work-orders — 5-Column BOM Mini-Table, Semantic Badges, Equal-Height KPIs & Reversal Modal
    // =========================================================================
    await page.goto(`${BASE_WEB}/kit-work-orders`, { waitUntil: 'networkidle' });
    await page.getByTestId('kit-work-orders-workbench').waitFor({ state: 'visible', timeout: 15000 });
    await page.getByTestId('kit-work-orders-table').waitFor({ state: 'visible', timeout: 10000 });
    await page.evaluate(() => {
      const sb = document.getElementById('system-sidebar');
      if (sb && sb.classList.contains('is-collapsed')) {
        const toggle = sb.querySelector('.sidebar-collapse-toggle');
        if (toggle instanceof HTMLElement) toggle.click();
      }
      window.scrollTo(0, 0);
    });
    await page.waitForTimeout(200);

    const kitKpiCheck = await page.evaluate(() => {
      const cards = Array.from(document.querySelectorAll('[data-testid="kit-work-orders-workbench"] .ui-metric'));
      const heights = cards.map((c) => Math.round(c.getBoundingClientRect().height));
      const diff = heights.length ? Math.max(...heights) - Math.min(...heights) : 999;
      const texts = cards.map((c) => (c.innerText || '').trim());
      const bomTbl =
        document.querySelector('[data-testid="kit-bom-preview-table"]') ||
        Array.from(document.querySelectorAll('[data-testid="kit-work-orders-workbench"] table')).find((t) =>
          (t.textContent || '').includes('Định mức'),
        );
      const bomHeaders = bomTbl ? Array.from(bomTbl.querySelectorAll('thead th')).map((th) => (th.textContent || '').trim()) : [];
      return {
        cardCount: cards.length,
        heights,
        kpiHeightDiffPx: diff,
        has2LinhKienBom: texts.some((t) => t.includes('2 linh kiện BOM')),
        hasMiniBomTable5Cols: bomHeaders.length === 5,
        bomHeaders,
      };
    });
    kpiHeightDiffPx = kitKpiCheck.kpiHeightDiffPx;

    const kit1440Tables = await measureVisibleTablesAtScrollZero(page, '/kit-work-orders', '1440x900');
    tableScrollMetrics.push(...kit1440Tables);
    const kitWoTable1440 = kit1440Tables.find((t) => t.tableId === 'kit-work-orders-table');
    addCheck(
      'FND-W20-05-KIT-WORK-ORDERS-MINI-BOM-TABLE-EQUAL-KPIS-AND-STATUS-COL2',
      kitKpiCheck.cardCount === 4 &&
        kpiHeightDiffPx === 0 &&
        kitKpiCheck.has2LinhKienBom &&
        kitKpiCheck.hasMiniBomTable5Cols &&
        Boolean(kitWoTable1440 && kitWoTable1440.statusColIndex1Based === 2 && kitWoTable1440.statusVisibleAtScroll0),
      { kitKpiCheck, kitWoTable1440 },
    );
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/kit-work-orders@1440x900'));
    await probeShotGeometry(page, 'W20_06A:/kit-work-orders@1440x900');
    const w20_06Left = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    await page.getByTestId('kit-work-orders-table').scrollIntoViewIfNeeded();
    await page.waitForTimeout(120);
    await page.getByTestId('btn-reverse-kit-KWO-2026-001').click();
    const kitModal = page.getByTestId('modal-confirm-kit-reversal');
    await kitModal.waitFor({ state: 'visible', timeout: 10000 });

    const kitReasonInput = kitModal.locator('textarea, input[type="text"]').first();
    await kitReasonInput.fill('Ngắn');
    const disabledWhenShort = await kitModal.locator('button[type="submit"], [data-testid="btn-confirm-kit-reversal"]').first().isDisabled();
    await kitReasonInput.fill('Điều chỉnh kế hoạch đóng gói bộ sản phẩm KWO-2026-001 theo yêu cầu xuất kho');
    await page.waitForTimeout(150);

    const kitModalCheck = await kitModal.evaluate((modalEl) => {
      const txt = modalEl.innerText || '';
      const ariaLabelledBy = modalEl.getAttribute('aria-labelledby') || '';
      const h2El = ariaLabelledBy ? document.getElementById(ariaLabelledBy) : null;
      const submitBtn = modalEl.querySelector('button[type="submit"], [data-testid="btn-confirm-kit-reversal"]');
      return {
        ariaLabelledBy,
        h2TagName: h2El?.tagName || '',
        hasKwoCode: txt.includes('KWO-2026-001'),
        has20Units: txt.includes('20'),
        has10MValuation: txt.includes('10.000.000'),
        hasMisleadingSlashMinCounter: /\b\d+\/\d+\s*ký tự tối thiểu/i.test(txt),
        enabledWhenReasonValid: submitBtn ? !submitBtn.hasAttribute('disabled') : false,
      };
    });

    kitReversalModalVerified =
      kitModalCheck.ariaLabelledBy === 'kit-reversal-modal-title' &&
      kitModalCheck.h2TagName === 'H2' &&
      kitModalCheck.hasKwoCode &&
      kitModalCheck.has20Units &&
      kitModalCheck.has10MValuation &&
      !kitModalCheck.hasMisleadingSlashMinCounter &&
      disabledWhenShort &&
      kitModalCheck.enabledWhenReasonValid;
    addCheck('FND-W18-03-KIT-REVERSAL-MODAL-OPEN-AND-VALIDATED', kitReversalModalVerified, {
      disabledWhenShort,
      ...kitModalCheck,
    });

    iconButtonAriaAudits.push(await auditIconOnlyButtons(page, 'W20_06:KitReversalModal@1440x900'));
    routeHygieneChecks.push(await collectViewportHygiene(page, '/kit-work-orders#reversal-modal@1440x900'));
    await probeShotGeometry(page, 'W20_06B:/kit-work-orders#reversal-modal@1440x900');
    const w20_06Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;
    if (captureScreenshots && w20_06Left && w20_06Right) {
      await maybeStitchedShot(
        'W20_06_Customer_KitWorkOrders_MiniBomTable_SemanticBadges_And_ReversalModal_1440x900.png',
        w20_06Left,
        w20_06Right,
        {
          route: '/kit-work-orders + /kit-work-orders#modal-confirm-kit-reversal',
          viewport: '1440x900 (2-up side-by-side)',
          actor: 'tenant_admin',
          checks: { kitKpiCheck, disabledWhenShort, ...kitModalCheck },
        },
      );
    }

    await page.keyboard.press('Escape');
    await kitModal.waitFor({ state: 'hidden', timeout: 5000 }).catch(async () => {
      const cancelBtn = kitModal.locator('button', { hasText: 'Hủy' }).first();
      if ((await cancelBtn.count()) > 0) await cancelBtn.click();
    });

    // =========================================================================
    // W20_07: /customer-returns (Uncut Select & Placeholder + Nowrap Codes) + /backorders (Empty Zero <thead>)
    // =========================================================================
    await page.goto(`${BASE_WEB}/customer-returns`, { waitUntil: 'networkidle' });
    await page.locator('table[aria-label="Bảng quản lý hàng hoàn và đổi trả RMA"]').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(250);

    const dispBtn = page.locator('[data-testid="btn-open-disposition-drawer"]:not([disabled])').first();
    if ((await dispBtn.count()) > 0) {
      await dispBtn.click();
      await page.waitForTimeout(250);
      rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/customer-returns#disposition-drawer@1440x900'));
      await page.keyboard.press('Escape');
      await page.waitForTimeout(220);
    }

    await page.evaluate(() => {
      if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
      document.body.focus();
      for (const ch of 'GHN849201199') {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: ch, bubbles: true }));
      }
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      window.scrollTo(0, 0);
    });
    await page.getByTestId('rma-success-banner').waitFor({ state: 'visible', timeout: 5000 });
    await page.waitForTimeout(180);

    const rma1440Tables = await measureVisibleTablesAtScrollZero(page, '/customer-returns', '1440x900');
    tableScrollMetrics.push(...rma1440Tables);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/customer-returns@1440x900'));
    const rmaGeo1440 = await probeShotGeometry(page, 'W20_07A:/customer-returns@1440x900');
    const rmaCheck = await page.evaluate(() => {
      const txt = document.body?.innerText || '';
      const banner = document.querySelector('[data-testid="rma-success-banner"]');
      const codeEls = Array.from(document.querySelectorAll('main table .ui-code'));
      const allCodesNowrap =
        codeEls.length > 0 && codeEls.every((el) => window.getComputedStyle(el).whiteSpace === 'nowrap');
      return {
        hasRmaRows: document.querySelectorAll('tr[data-testid^="rma-row-"]').length > 0,
        hasCostVisible: document.querySelectorAll('[data-testid="rma-cost-visible"]').length > 0,
        hasViQuarantinedBadge: txt.includes('Đang biệt trữ'),
        bannerVisible: Boolean(banner && banner.getBoundingClientRect().height > 20),
        bannerRole: banner?.getAttribute('role') || '',
        bannerAriaLive: banner?.getAttribute('aria-live') || '',
        allCodesNowrap,
      };
    });
    addCheck(
      'FND-W20-01-CUSTOMER-RETURNS-UNCUT-CONTROLS-NOWRAP-CODES-AND-ZERO-OVERFLOW',
      rmaCheck.hasRmaRows &&
        rmaCheck.hasCostVisible &&
        rmaCheck.hasViQuarantinedBadge &&
        rmaCheck.bannerVisible &&
        rmaCheck.bannerRole === 'status' &&
        rmaCheck.bannerAriaLive === 'polite' &&
        rmaCheck.allCodesNowrap &&
        rmaGeo1440.truncatedElementsCount === 0 &&
        rma1440Tables.every((t) => !t.hasOverflow && t.statusVisibleAtScroll0),
      { rmaCheck, rmaTruncatedCount: rmaGeo1440.truncatedElementsCount, rma1440Tables },
    );
    routeHygieneChecks.push(await collectViewportHygiene(page, '/customer-returns@1440x900'));
    const w20_07Left = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    await page.goto(`${BASE_WEB}/backorders`, { waitUntil: 'networkidle' });
    await page.getByTestId('backorders-kpi-grid').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(200);
    const boCheck = await page.evaluate(() => ({
      hasEmpty: Boolean(document.querySelector('main .ui-empty')),
      theadsInMain: document.querySelectorAll('main thead').length,
    }));
    emptyBackordersZeroThead = Boolean(boCheck.hasEmpty && boCheck.theadsInMain === 0);
    addCheck('FND-W19-04-BACKORDERS-EMPTY-NO-BLANK-THEAD', emptyBackordersZeroThead, boCheck);
    routeHygieneChecks.push(await collectViewportHygiene(page, '/backorders@1440x900'));
    await probeShotGeometry(page, 'W20_07B:/backorders@1440x900');
    const w20_07Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;
    if (captureScreenshots && w20_07Left && w20_07Right) {
      await maybeStitchedShot(
        'W20_07_Customer_ReturnsRma_UncutSelectAndPlaceholder_NowrapCodes_1440x900.png',
        w20_07Left,
        w20_07Right,
        {
          route: '/customer-returns + /backorders',
          viewport: '1440x900 (2-up side-by-side)',
          actor: 'tenant_admin',
          checks: { rmaCheck, boCheck },
        },
      );
    }

    // =========================================================================
    // W20_08: Tablet 768x1024 — /import-receipts & /kit-work-orders Responsive Card View, 2x2 KPIs & Zero Overlap
    // =========================================================================
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.goto(`${BASE_WEB}/import-receipts`, { waitUntil: 'networkidle' });
    await page
      .locator('.ui-mobile-card-list, [data-testid="import-receipts-mobile-cards"]')
      .first()
      .waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(220);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/import-receipts@768x1024'));
    const imp768Geo = await probeShotGeometry(page, 'W20_08A:/import-receipts@768x1024');
    const imp768CardCheck = await page.evaluate(() => {
      const desktopTbl = document.querySelector('table[aria-label="Danh sách phiếu nhập"]');
      const tblRect = desktopTbl?.getBoundingClientRect();
      const desktopTableHidden = !desktopTbl || !tblRect || tblRect.width === 0 || tblRect.height === 0;
      const cards = Array.from(document.querySelectorAll('[data-testid="import-receipt-mobile-card"]'));
      const metrics = Array.from(document.querySelectorAll('main .ui-metric')).map((m) =>
        Math.round(m.getBoundingClientRect().top),
      );
      const is2x2Grid =
        metrics.length >= 4 &&
        Math.abs(metrics[0] - metrics[1]) <= 4 &&
        Math.abs(metrics[2] - metrics[3]) <= 4 &&
        metrics[2] > metrics[0] + 20;
      const docEl = document.documentElement;
      return {
        desktopTableHidden,
        cardCount: cards.length,
        metricsCount: metrics.length,
        is2x2Grid,
        hasPnk202611001: (document.body?.innerText || '').includes('PNK-202611-001'),
        horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      };
    });
    routeHygieneChecks.push(await collectViewportHygiene(page, '/import-receipts@768x1024'));
    const w20_08Left = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    await page.goto(`${BASE_WEB}/kit-work-orders`, { waitUntil: 'networkidle' });
    await page.getByTestId('kit-work-orders-workbench').waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(220);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/kit-work-orders@768x1024'));
    const kit768Geo = await probeShotGeometry(page, 'W20_08B:/kit-work-orders@768x1024');
    const kit768CardCheck = await page.evaluate(() => {
      const desktopTbl = document.querySelector('[data-testid="kit-work-orders-table"]');
      const tblRect = desktopTbl?.getBoundingClientRect();
      const desktopTableHidden = !desktopTbl || !tblRect || tblRect.width === 0 || tblRect.height === 0;
      const cardList = document.querySelector('[data-testid="kit-work-orders-workbench"] .ui-mobile-card-list');
      const cardListRect = cardList?.getBoundingClientRect();
      const cardViewVisible = Boolean(cardList && cardListRect && cardListRect.height > 40);
      const metrics = Array.from(
        document.querySelectorAll('[data-testid="kit-work-orders-workbench"] .ui-metric'),
      ).map((m) => Math.round(m.getBoundingClientRect().top));
      const is2x2Grid =
        metrics.length === 4 &&
        Math.abs(metrics[0] - metrics[1]) <= 4 &&
        Math.abs(metrics[2] - metrics[3]) <= 4 &&
        metrics[2] > metrics[0] + 20;
      const concatenatedFields = Array.from(document.querySelectorAll('.ui-mobile-card-field')).filter((f) => {
        const lbl = f.querySelector('.ui-mobile-card-label');
        const val = f.querySelector('.ui-mobile-card-value');
        if (!lbl || !val) return false;
        const lr = lbl.getBoundingClientRect();
        const vr = val.getBoundingClientRect();
        if (lr.width <= 0 || vr.width <= 0) return false;
        return Math.abs(lr.top - vr.top) < 6 && vr.left - lr.right < 4 && !(lbl.textContent || '').trim().endsWith(':');
      });
      const docEl = document.documentElement;
      return {
        desktopTableHidden,
        cardViewVisible,
        metricsCount: metrics.length,
        is2x2Grid,
        concatenatedFieldLabelsCount: concatenatedFields.length,
        hasKwo2026001: (document.body?.innerText || '').includes('KWO-2026-001'),
        horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      };
    });
    routeHygieneChecks.push(await collectViewportHygiene(page, '/kit-work-orders@768x1024'));
    const w20_08Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    const tablet768Ok =
      imp768CardCheck.desktopTableHidden &&
      imp768CardCheck.cardCount >= 1 &&
      (imp768CardCheck.metricsCount === 0 || imp768CardCheck.is2x2Grid) &&
      imp768CardCheck.hasPnk202611001 &&
      imp768CardCheck.horizontalOverflowPx === 0 &&
      imp768Geo.truncatedElementsCount === 0 &&
      kit768CardCheck.desktopTableHidden &&
      kit768CardCheck.cardViewVisible &&
      kit768CardCheck.is2x2Grid &&
      kit768CardCheck.concatenatedFieldLabelsCount === 0 &&
      kit768CardCheck.hasKwo2026001 &&
      kit768CardCheck.horizontalOverflowPx === 0 &&
      kit768Geo.truncatedElementsCount === 0;
    addCheck('FND-W20-01-TABLET-768-CARD-VIEW-2X2-KPIS-AND-ZERO-OVERLAP', tablet768Ok, {
      imp768CardCheck,
      imp768TruncatedItems: imp768Geo.truncatedItems,
      kit768CardCheck,
      kit768TruncatedItems: kit768Geo.truncatedItems,
    });
    if (!tablet768Ok) {
      addFinding({
        id: 'QA-W20-TABLET-768-CARD-AND-TOPBAR',
        severity: 'MEDIUM',
        domain: 'Wave-20 Tablet 768x1024 Card Layout, Topbar Bleed & Field Spacing',
        ownerAgent: 'AGENT-02',
        summary:
          'At 768x1024, topbar button bleeds past viewport right edge (MainLayoutParts.tsx:179-260), /kit-work-orders SKU <select> clips option text, or .ui-mobile-card-field concatenates label+value with 0px gap (KitWorkOrderResponsiveCards.tsx:124-136)',
        evidence: {
          imp768CardCheck,
          imp768TruncatedItems: imp768Geo.truncatedItems,
          kit768CardCheck,
          kit768TruncatedItems: kit768Geo.truncatedItems,
        },
      });
    }

    if (captureScreenshots && w20_08Left && w20_08Right) {
      await maybeStitchedShot(
        'W20_08_Tablet_768x1024_ImportReceipts_And_KitWorkOrders_CardView_2x2Kpis_ZeroOverlap_768x1024.png',
        w20_08Left,
        w20_08Right,
        {
          route: '/import-receipts + /kit-work-orders',
          viewport: '768x1024 (2-up side-by-side)',
          actor: 'tenant_admin',
          checks: { imp768CardCheck, kit768CardCheck },
        },
      );
    }

    // =========================================================================
    // W20_09: Mobile 375x812 — /import-receipts Collapsed Filter, First Card Above Fold (<= 230px) & Single-Row Actions
    // =========================================================================
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`${BASE_WEB}/import-receipts`, { waitUntil: 'networkidle' });
    const mobileCardsList = page.locator('.ui-mobile-card-list, [data-testid="import-receipts-mobile-cards"]').first();
    await mobileCardsList.waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(220);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/import-receipts@375x812'));
    await probeShotGeometry(page, 'W20_09:/import-receipts@375x812');

    const imp375Check = await page.evaluate(() => {
      const listEl = document.querySelector('.ui-mobile-card-list, [data-testid="import-receipts-mobile-cards"]');
      const cs = listEl ? window.getComputedStyle(listEl) : null;
      const cards = Array.from(document.querySelectorAll('[data-testid="import-receipt-mobile-card"]'));
      const firstCardRect = cards[0]?.getBoundingClientRect();
      const firstCardTopPx = firstCardRect ? Math.round(firstCardRect.top) : 999;
      const firstPrimaryBtn = cards[0]?.querySelector('.ui-btn-primary, .import-row-actions-mobile button');
      const firstMoreBtn = cards[0]?.querySelector('summary, [aria-label*="Thao tác"], [aria-label*="thao tác"]');
      const pRect = firstPrimaryBtn?.getBoundingClientRect();
      const mRect = firstMoreBtn?.getBoundingClientRect();
      const singleRowActions = Boolean(pRect && mRect && Math.abs(pRect.top - mRect.top) <= 4 && mRect.right <= 375);
      const listText = listEl?.innerText || '';
      const maxPrimaryBtnsPerCard = cards.length
        ? Math.max(...cards.map((c) => c.querySelectorAll('.ui-btn-primary').length))
        : 99;
      const actionBtns = listEl ? Array.from(listEl.querySelectorAll('button, summary')) : [];
      const minBtnHeightPx = actionBtns.length
        ? Math.min(...actionBtns.map((b) => Math.round(b.getBoundingClientRect().height)))
        : 0;
      const docEl = document.documentElement;
      return {
        scrollY: window.scrollY,
        listDisplay: cs?.display || 'none',
        cardCount: cards.length,
        firstCardTopPx,
        singleRowActions,
        maxPrimaryBtnsPerCard,
        hasExpressPost: /Express\s+Post/i.test(listText),
        hasDuLieuCu: /dữ\s+liệu\s+cũ/i.test(listText),
        hasPnk202611001: listText.includes('PNK-202611-001'),
        hasWarehouseWh01: listText.includes('WH01'),
        hasTotal5500k: listText.includes('5.500.000'),
        minActionButtonHeightPx: minBtnHeightPx,
        horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      };
    });

    mobileFirstCardTopPx375 = imp375Check.firstCardTopPx;

    importReceipts375CardListVerified =
      imp375Check.listDisplay !== 'none' &&
      imp375Check.cardCount >= 1 &&
      imp375Check.firstCardTopPx <= 230 &&
      imp375Check.singleRowActions &&
      imp375Check.hasPnk202611001 &&
      imp375Check.hasWarehouseWh01 &&
      imp375Check.hasTotal5500k &&
      imp375Check.minActionButtonHeightPx >= 44 &&
      imp375Check.horizontalOverflowPx === 0;

    importReceiptsSinglePrimaryCtaMobile =
      importReceipts375CardListVerified &&
      imp375Check.maxPrimaryBtnsPerCard <= 1 &&
      !imp375Check.hasExpressPost &&
      !imp375Check.hasDuLieuCu;

    addCheck(
      'FND-W20-06-MOBILE-375-IMPORT-RECEIPTS-FIRST-CARD-ABOVE-230PX-SINGLE-ROW-CTA',
      importReceiptsSinglePrimaryCtaMobile,
      imp375Check,
    );
    routeHygieneChecks.push(await collectViewportHygiene(page, '/import-receipts@375x812'));
    await maybeShot('W20_09_Mobile_375x812_ImportReceipts_CollapsedFilter_FirstCardAboveFold_SingleRowActions_375x812.png', {
      route: '/import-receipts#mobile-cards',
      viewport: '375x812',
      actor: 'tenant_admin',
      checks: imp375Check,
    });

    // Verify keyboard flyout on 1024x768 + execute real hasTouch: true tap() flyout & capture W20_10
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto(`${BASE_WEB}/sales-orders`, { waitUntil: 'networkidle' });
    await page.getByTestId('sales-orders-table').waitFor({ state: 'visible', timeout: 15000 });
    iconButtonAriaAudits.push(await auditIconOnlyButtons(page, 'CollapsedIconRail@1024x768'));
    const nhapKhoBtn = page.getByTestId('nav-group-nhap-kho');
    await nhapKhoBtn.focus();
    await page.keyboard.press('Enter');
    await page.getByTestId('wms-secondary-flyout').waitFor({ state: 'visible', timeout: 5000 });
    await page.keyboard.press('ArrowDown');
    await page.waitForTimeout(150);
    const kbCheck = await page.evaluate(() => {
      const flyout = document.querySelector('[data-testid="wms-secondary-flyout"]');
      const active = document.activeElement;
      return {
        flyoutOpen: Boolean(flyout && flyout.classList.contains('is-open')),
        activeInsideFlyout: Boolean(flyout && active && flyout.contains(active)),
        matchesFocusVisible: active ? active.matches(':focus-visible') : false,
      };
    });
    addCheck('A11Y-KEYBOARD-SIDEBAR-FLYOUT-1024', kbCheck.flyoutOpen && kbCheck.activeInsideFlyout && kbCheck.matchesFocusVisible, kbCheck);
    await page.keyboard.press('Escape');

    touchFlyoutSummary = await runTouchFlyoutVerificationAndCaptureW2010(
      browser,
      tenantAdminAuth,
      captureScreenshots,
      screenshotsManifest,
      sortHeaderMetrics,
      routeHygieneChecks,
      screenshotGeometryProbes,
      addFinding,
      addCheck,
    );

    // =========================================================================
    // W20_11: Mobile 360x800 (/picking-tasks Compact 3-Col KPI & Scanner) + 390x844 (/import-receipts First Card Above Fold)
    // =========================================================================
    const pickMobileChecks = [];
    let w20_11Left = null;
    for (const mvp of [
      { width: 360, height: 800, captureLeft: true },
      { width: 375, height: 812, captureLeft: false },
      { width: 390, height: 844, captureLeft: false },
    ]) {
      await page.setViewportSize({ width: mvp.width, height: mvp.height });
      await page.goto(`${BASE_WEB}/picking-tasks`, { waitUntil: 'networkidle' });
      await page.getByTestId('scanner-control-panel').waitFor({ state: 'visible', timeout: 15000 });
      await page.getByTestId('pda-bottom-nav').waitFor({ state: 'visible', timeout: 10000 });
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.waitForTimeout(220);

      const mCheck = await page.evaluate(({ width: w, height: h }) => {
        const docEl = document.documentElement;
        const confirmBtn = document.querySelector('[data-testid="scanner-confirm-btn"]');
        const undoBtn = document.querySelector('[data-testid="scanner-undo-last-btn"]');
        const cam = document.querySelector('[data-testid="scanner-camera-viewport"]');
        const bottomNav = document.querySelector('[data-testid="pda-bottom-nav"]');
        const navTabsCount = bottomNav ? bottomNav.querySelectorAll('a, button').length : 0;
        const headerWrap = document.querySelector('.picking-page-header-wrap');
        const headerHidden = !headerWrap || window.getComputedStyle(headerWrap).display === 'none';
        const kpiStrip = document.querySelector('[data-testid="picking-handheld-kpi-strip"]');
        const kpiCells = kpiStrip ? Array.from(kpiStrip.children) : [];
        const compact3ColKpi =
          kpiCells.length === 3 &&
          Math.abs(kpiCells[0].getBoundingClientRect().top - kpiCells[2].getBoundingClientRect().top) <= 4;
        const cRect = confirmBtn?.getBoundingClientRect();
        const uRect = undoBtn?.getBoundingClientRect();
        const camRect = cam?.getBoundingClientRect();
        return {
          viewport: `${w}x${h}`,
          scrollWidth: docEl.scrollWidth,
          horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
          confirmUndoSameRow: Boolean(cRect && uRect && Math.abs(cRect.top - uRect.top) <= 4 && uRect.right <= w),
          scannerAboveFold: Boolean(camRect && camRect.bottom <= h),
          headerHidden,
          compact3ColKpi,
          navTabsCount,
        };
      }, mvp);
      pickMobileChecks.push(mCheck);
      rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, `/picking-tasks@${mvp.width}x${mvp.height}`));
      routeHygieneChecks.push(await collectViewportHygiene(page, `/picking-tasks@${mvp.width}x${mvp.height}`));
      if (mvp.captureLeft) {
        await probeShotGeometry(page, 'W20_11A:/picking-tasks@360x800');
        if (captureScreenshots) {
          w20_11Left = (await capturePureCdpBuffer(page)).buf;
        }
      }
    }

    const mobilePickingAllOk = pickMobileChecks.every(
      (c) =>
        c.horizontalOverflowPx === 0 &&
        c.confirmUndoSameRow &&
        c.scannerAboveFold &&
        c.headerHidden &&
        c.compact3ColKpi &&
        c.navTabsCount === 5,
    );
    addCheck('FND-W20-06-MOBILE-360-375-390-PICKING-COMPACT-KPI-AND-SCANNER', mobilePickingAllOk, { pickMobileChecks });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${BASE_WEB}/import-receipts`, { waitUntil: 'networkidle' });
    await page.locator('.ui-mobile-card-list, [data-testid="import-receipts-mobile-cards"]').first().waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(220);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/import-receipts@390x844'));
    await probeShotGeometry(page, 'W20_11B:/import-receipts@390x844');
    const imp390Check = await page.evaluate(() => {
      const cards = Array.from(document.querySelectorAll('[data-testid="import-receipt-mobile-card"]'));
      const firstCardRect = cards[0]?.getBoundingClientRect();
      const firstCardTopPx = firstCardRect ? Math.round(firstCardRect.top) : 999;
      const firstPrimaryBtn = cards[0]?.querySelector('.ui-btn-primary, .import-row-actions-mobile button');
      const firstMoreBtn = cards[0]?.querySelector('summary, [aria-label*="Thao tác"], [aria-label*="thao tác"]');
      const pRect = firstPrimaryBtn?.getBoundingClientRect();
      const mRect = firstMoreBtn?.getBoundingClientRect();
      const singleRowActions = Boolean(pRect && mRect && Math.abs(pRect.top - mRect.top) <= 4 && mRect.right <= 390);
      const maxPrimaryBtnsPerCard = cards.length
        ? Math.max(...cards.map((c) => c.querySelectorAll('.ui-btn-primary').length))
        : 99;
      const docEl = document.documentElement;
      return {
        scrollY: window.scrollY,
        cardCount: cards.length,
        firstCardTopPx,
        singleRowActions,
        maxPrimaryBtnsPerCard,
        horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      };
    });
    mobileFirstCardTopPx390 = imp390Check.firstCardTopPx;
    mobileSingleRowActionsVerified = Boolean(imp375Check.singleRowActions && imp390Check.singleRowActions);
    importReceiptsSinglePrimaryCtaMobile = Boolean(
      importReceiptsSinglePrimaryCtaMobile &&
        imp390Check.cardCount >= 1 &&
        imp390Check.firstCardTopPx <= 230 &&
        imp390Check.singleRowActions &&
        imp390Check.maxPrimaryBtnsPerCard <= 1 &&
        imp390Check.horizontalOverflowPx === 0,
    );
    routeHygieneChecks.push(await collectViewportHygiene(page, '/import-receipts@390x844'));
    const w20_11Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    if (captureScreenshots && w20_11Left && w20_11Right) {
      await maybeStitchedShot(
        'W20_11_Mobile_360x800_PickingScanner_Compact3ColKpi_And_390x844_ImportCards.png',
        w20_11Left,
        w20_11Right,
        {
          route: '/picking-tasks@360x800 + /import-receipts@390x844',
          viewport: '360x800 + 390x844 (2-up side-by-side)',
          actor: 'tenant_admin',
          checks: { pick360: pickMobileChecks[0], imp390Check },
        },
      );
    }

    // =========================================================================
    // W20_12: Widescreen 1280x800 (tenant_admin cost visible) & 1920x1080 (qa_staff_wh01 max-width 1480px & compact cost mask badge)
    // =========================================================================
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`${BASE_WEB}/customer-returns`, { waitUntil: 'networkidle' });
    const rmaTbl1280 = page.locator('table[aria-label="Bảng quản lý hàng hoàn và đổi trả RMA"]');
    await rmaTbl1280.waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(200);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/customer-returns@1280x800'));
    const rma1280Tables = await measureVisibleTablesAtScrollZero(page, '/customer-returns', '1280x800');
    tableScrollMetrics.push(...rma1280Tables);
    await probeShotGeometry(page, 'W20_12A:/customer-returns@1280x800');
    const cost1280Check = await page.evaluate(() => {
      const vis = Array.from(document.querySelectorAll('[data-testid="rma-cost-visible"]')).map((e) => (e.textContent || '').trim());
      const red = document.querySelectorAll('[data-testid="rma-cost-redacted"]').length;
      const docEl = document.documentElement;
      return {
        actor: window.localStorage.getItem('username'),
        visibleCostCount: vis.length,
        sampleVisibleCost: vis[0] || '',
        redactedCount: red,
        horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      };
    });
    routeHygieneChecks.push(await collectViewportHygiene(page, '/customer-returns@1280x800'));
    const w20_12Left = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto(`${BASE_WEB}/login`, { waitUntil: 'networkidle' });
    await page.evaluate(() => {
      window.localStorage.clear();
      window.sessionStorage.clear();
    });
    await applyAuthTokenInBrowser(page, staffAuth.token, staffAuth.user, true);
    await page.goto(`${BASE_WEB}/customer-returns`, { waitUntil: 'networkidle' });
    const rmaTbl1920 = page.locator('table[aria-label="Bảng quản lý hàng hoàn và đổi trả RMA"]');
    await rmaTbl1920.waitFor({ state: 'visible', timeout: 15000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(200);
    rawEnumScanResults.push(await scanDomForRawEnglishEnums(page, '/customer-returns#staff-masked@1920x1080'));
    const rma1920Tables = await measureVisibleTablesAtScrollZero(page, '/customer-returns', '1920x1080');
    tableScrollMetrics.push(...rma1920Tables);
    await probeShotGeometry(page, 'W20_12B:/customer-returns#staff-masked@1920x1080');
    const cost1920Check = await page.evaluate(() => {
      const vis = document.querySelectorAll('[data-testid="rma-cost-visible"]').length;
      const red = Array.from(document.querySelectorAll('[data-testid="rma-cost-redacted"]')).map((e) => (e.textContent || '').trim());
      const mainInner = document.querySelector('.main-content-inner');
      const mainInnerWidthPx = mainInner ? Math.round(mainInner.getBoundingClientRect().width) : 9999;
      const docEl = document.documentElement;
      return {
        actor: window.localStorage.getItem('username'),
        visibleCostCount: vis,
        redactedCount: red.length,
        sampleRedactedText: red[0] || '',
        mainInnerWidthPx,
        horizontalOverflowPx: Math.max(0, docEl.scrollWidth - docEl.clientWidth),
      };
    });
    routeHygieneChecks.push(await collectViewportHygiene(page, '/customer-returns#staff-masked@1920x1080'));
    const w20_12Right = captureScreenshots ? (await capturePureCdpBuffer(page)).buf : null;

    const multiRoleCostUiOk =
      cost1280Check.actor === 'tenant_admin' &&
      cost1280Check.visibleCostCount >= 1 &&
      cost1280Check.redactedCount === 0 &&
      cost1280Check.horizontalOverflowPx === 0 &&
      rma1280Tables.every((t) => !t.hasOverflow) &&
      cost1920Check.actor === 'qa_staff_wh01' &&
      cost1920Check.visibleCostCount === 0 &&
      cost1920Check.redactedCount >= 1 &&
      cost1920Check.sampleRedactedText === 'Ẩn theo quyền' &&
      cost1920Check.mainInnerWidthPx <= 1480 &&
      cost1920Check.horizontalOverflowPx === 0 &&
      rma1920Tables.every((t) => !t.hasOverflow);
    addCheck('FND-W20-05-WIDESCREEN-1280-AND-1920-MAXWIDTH-1480-AND-COMPACT-COST-MASK', multiRoleCostUiOk, {
      cost1280Check,
      rma1280Tables,
      cost1920Check,
      rma1920Tables,
    });

    if (captureScreenshots && w20_12Left && w20_12Right) {
      await maybeStitchedShot(
        'W20_12_Widescreen_1280x800_And_1920x1080_MaxWidthContainer_CompactCostMaskBadge.png',
        w20_12Left,
        w20_12Right,
        {
          route: '/customer-returns (tenant_admin@1280x800 vs qa_staff_wh01@1920x1080)',
          viewport: '1280x800 + 1920x1080 (2-up side-by-side)',
          actor: 'tenant_admin + qa_staff_wh01',
          checks: { cost1280Check, cost1920Check },
        },
      );
    }
  } finally {
    await restoreCleanBaselineViaApi(context.request).catch(() => {});
    postAuditSqlBaseline = queryErpKhoReadOnlySqlJson();
    await context.close().catch(() => {});
    await browser.close().catch(() => {});
  }

  const rawEnglishEnumBadgesCount = rawEnumScanResults.reduce((s, r) => s + r.hitCount, 0);
  addCheck('FND-W19-02-ZERO-RAW-ENGLISH-ENUMS-ACROSS-LIVE-DOM', rawEnglishEnumBadgesCount === 0, {
    surfacesScannedCount: rawEnumScanResults.length,
    rawEnglishEnumBadgesCount,
    hits: rawEnumScanResults.filter((r) => r.hitCount > 0),
  });
  if (rawEnglishEnumBadgesCount > 0) {
    addFinding({
      id: 'QA-W19-RAW-ENGLISH-ENUMS',
      severity: 'MEDIUM',
      domain: '100% Vietnamese Enum Mapping (FND-W19-02 / N-10 / UIX-11)',
      ownerAgent: 'AGENT-02',
      summary: `Detected ${rawEnglishEnumBadgesCount} raw English enum/copy token(s) in live rendered DOM`,
      evidence: rawEnumScanResults.filter((r) => r.hitCount > 0),
    });
  }

  const maxHorizontalOverflowPx = Math.max(
    0,
    eightViewportMatrix.maxMatrixOverflowPx,
    ...routeHygieneChecks.map((h) => h.horizontalOverflowPx),
  );
  const stickyActionRightOverflowPx = Math.max(
    0,
    ...tableScrollMetrics.map((m) => m.stickyActionRightOverflowPx || 0),
  );
  const totalEmojiCount = routeHygieneChecks.reduce((s, h) => s + h.emojiCount, 0);
  const totalBrokenImages = routeHygieneChecks.reduce((s, h) => s + h.brokenImageCount, 0);
  const hasReplacementOrNaN = routeHygieneChecks.some((h) => h.hasReplacementChar || h.hasUndefinedOrNaN);
  const internalTableOverflowUnindicatedCount = tableScrollMetrics.filter((m) => m.unindicatedOverflow).length;
  const missingAriaLabelIconButtonsCount = iconButtonAriaAudits.reduce((s, a) => s + a.missingAriaLabelCount, 0);
  const minPrimaryContrast = contrastMeasurements.length
    ? Math.min(...contrastMeasurements.map((c) => c.contrastRatio))
    : 0;
  const all8ViewportsZeroOverflow = Boolean(eightViewportMatrix.all8ViewportsZeroOverflow && maxHorizontalOverflowPx === 0);
  const axeViolationCount = axeAuditSummary.axeViolationCount;
  const allTextContrastPairsPassAA = Boolean(axeAuditSummary.allTextContrastPairsPassAA && minPrimaryContrast >= 6.4);
  const touchFlyoutVerified = Boolean(touchFlyoutSummary.touchFlyoutVerified);

  const statusColumnVisibleAtScrollLeft0 = Boolean(eightViewportMatrix.statusColumnVisibleAtScrollLeft0);
  const stickyRightWidthRatio = Number(eightViewportMatrix.stickyRightWidthRatio || 0);
  const clippedWarehouseTextCount =
    Number(eightViewportMatrix.clippedWarehouseTextCount || 0) +
    tableScrollMetrics.reduce((s, m) => s + (m.clippedWarehouseCellsCount || 0), 0);
  const customerReturnsZeroHorizontalScrollDesktop = Boolean(
    eightViewportMatrix.customerReturnsZeroHorizontalScrollDesktop,
  );
  const minTouchTargetHeightPx = Number(eightViewportMatrix.minTouchTargetHeightPx || 0);
  const skipLinkWorksOnFirstTab = Boolean(wave19A11yMotion.skipLinkWorksOnFirstTab);
  const modalFocusTrapAndEscapeRestore = Boolean(wave19A11yMotion.modalFocusTrapAndEscapeRestore);
  const prefersReducedMotionRespected = Boolean(wave19A11yMotion.prefersReducedMotionRespected);

  addCheck('RUNTIME-ZERO-CONSOLE-AND-PAGE-ERRORS', consoleErrors.length === 0 && pageErrors.length === 0, {
    consoleErrorCount: consoleErrors.length,
    pageErrorCount: pageErrors.length,
  });
  addCheck('RUNTIME-ZERO-UNEXPECTED-HTTP-ERRORS', unexpected5xxErrors.length === 0 && unexpected4xxErrors.length === 0, {
    unexpected5xxCount: unexpected5xxErrors.length,
    unexpected4xxCount: unexpected4xxErrors.length,
  });
  addCheck(
    'WCAG-HYGIENE-AND-CONTRAST',
    maxHorizontalOverflowPx === 0 &&
      stickyActionRightOverflowPx === 0 &&
      axeViolationCount === 0 &&
      allTextContrastPairsPassAA &&
      totalEmojiCount === 0 &&
      totalBrokenImages === 0 &&
      !hasReplacementOrNaN &&
      minPrimaryContrast >= 6.4 &&
      missingAriaLabelIconButtonsCount === 0 &&
      internalTableOverflowUnindicatedCount === 0,
    {
      maxHorizontalOverflowPx,
      stickyActionRightOverflowPx,
      axeViolationCount,
      allTextContrastPairsPassAA,
      totalEmojiCount,
      totalBrokenImages,
      hasReplacementOrNaN,
      minPrimaryContrast,
      missingAriaLabelIconButtonsCount,
      internalTableOverflowUnindicatedCount,
    },
  );

  if (postAuditSqlBaseline) {
    const baselineClean =
      String(postAuditSqlBaseline.tenant1Status || '').toUpperCase() === 'ACTIVE' &&
      Number(postAuditSqlBaseline.tenant1MaxWarehouses) === 3 &&
      String(postAuditSqlBaseline.invoice202611Status || '').toUpperCase() === 'UNPAID' &&
      Boolean(postAuditSqlBaseline.wh01IsActive) &&
      String(postAuditSqlBaseline.kwo1Status || '') === 'Completed';
    addCheck('SQL-ERP-KHO-BASELINE-RESTORED', baselineClean, postAuditSqlBaseline);
    if (!baselineClean) {
      addFinding({
        id: 'QA-W19-DB-BASELINE',
        severity: 'HIGH',
        domain: 'ERP_KHO Baseline Restoration',
        ownerAgent: 'AGENT-03',
        summary: 'Post-audit ERP_KHO SQL baseline did not match TENANT-001=ACTIVE, INV-SAAS-202611-002=UNPAID, WH01 IsActive=1, KWO-2026-001=Completed',
        evidence: postAuditSqlBaseline,
      });
    }
  }

  screenshotsManifest.sort((a, b) => a.fileName.localeCompare(b.fileName));

  const diskPngFiles = fs.existsSync(SCREENSHOT_DIR)
    ? fs.readdirSync(SCREENSHOT_DIR).filter((f) => f.toLowerCase().endsWith('.png'))
    : [];
  const w20PngCount = diskPngFiles.filter((f) => f.startsWith('W20_')).length;
  const nonW20PngCount = diskPngFiles.filter((f) => !f.startsWith('W20_')).length;
  if (captureScreenshots) {
    const shotsClean =
      screenshotsManifest.length === 12 &&
      screenshotsManifest.every((s) => s.fileName.startsWith('W20_')) &&
      w20PngCount === 12 &&
      nonW20PngCount === 0;
    addCheck('W20-SCREENSHOT-HYGIENE-12-W20-ZERO-LEGACY', shotsClean, {
      manifestCount: screenshotsManifest.length,
      w20PngCount,
      nonW20PngCount,
      files: diskPngFiles,
    });
    if (!shotsClean) {
      addFinding({
        id: 'QA-W20-SCREENSHOT-HYGIENE',
        severity: 'HIGH',
        domain: 'Wave-20 Pure-CDP Screenshot Evidence Hygiene',
        ownerAgent: 'AGENT-03',
        summary: `Expected 12 W20_*.png and 0 legacy PNGs; found W20=${w20PngCount}, nonW20=${nonW20PngCount}`,
        evidence: { manifestCount: screenshotsManifest.length, w20PngCount, nonW20PngCount, diskPngFiles },
      });
    }
  }

  const allExtraGeoProbes = [
    ...(eightViewportMatrix.geometryMatrixProbes || []),
    ...screenshotGeometryProbes,
  ];
  const extraTruncated = allExtraGeoProbes.reduce((s, g) => s + (g.truncatedElementsCount || 0), 0);
  const extraCrushed = allExtraGeoProbes.reduce((s, g) => s + (g.crushedTableHeaderCount || 0), 0);
  const extraStickyOverlap = allExtraGeoProbes.reduce((s, g) => s + (g.stickyColumnOverlapCount || 0), 0);
  const extraRawTech = allExtraGeoProbes.reduce((s, g) => s + (g.rawTechnicalStringsCount || 0), 0);

  const truncatedElementsCount = Number(sweep54Summary.sweepTruncatedCount || 0) + extraTruncated;
  const crushedTableHeaderCount = Number(sweep54Summary.sweepCrushedHeaderCount || 0) + extraCrushed;
  const stickyColumnOverlapCount = Number(sweep54Summary.sweepStickyOverlapCount || 0) + extraStickyOverlap;
  const rawTechnicalStringsCount = Number(sweep54Summary.sweepRawTechCount || 0) + extraRawTech;
  const mobileFirstCardTopPx = Math.max(mobileFirstCardTopPx375, mobileFirstCardTopPx390);

  const sweep54CompleteAndClean =
    sweep54Summary.sweptRoutesTotal === 54 &&
    sweep54Summary.sweptCustomerRoutesCount === 46 &&
    sweep54Summary.sweptPlatformRoutesCount === 8 &&
    sweep54Summary.sweepMaxOverflowPx === 0 &&
    sweep54Summary.sweepConsoleErrorsCount === 0 &&
    sweep54Summary.sweepPageErrorsCount === 0 &&
    sweep54Summary.sweepHttpErrorsCount === 0;
  addCheck('W20-54-ROUTE-SWEEP-COMPLETE-ZERO-RUNTIME-ERRORS', sweep54CompleteAndClean, {
    sweptRoutesTotal: sweep54Summary.sweptRoutesTotal,
    sweptCustomerRoutesCount: sweep54Summary.sweptCustomerRoutesCount,
    sweptPlatformRoutesCount: sweep54Summary.sweptPlatformRoutesCount,
    sweepMaxOverflowPx: sweep54Summary.sweepMaxOverflowPx,
    sweepConsoleErrorsCount: sweep54Summary.sweepConsoleErrorsCount,
    sweepPageErrorsCount: sweep54Summary.sweepPageErrorsCount,
    sweepHttpErrorsCount: sweep54Summary.sweepHttpErrorsCount,
    failingRoutes: sweep54Summary.failingRoutes,
  });
  if (!sweep54CompleteAndClean) {
    addFinding({
      id: 'QA-W20-54ROUTE-SWEEP-RUNTIME',
      severity: 'HIGH',
      domain: 'Wave-20 Full 54-Route Sweep Runtime & Overflow Gate',
      ownerAgent: 'AGENT-02',
      summary: '54-route sweep detected runtime console/page/HTTP errors or horizontal overflow',
      evidence: sweep54Summary.failingRoutes,
    });
  }

  addCheck('W20-ASSERT1-ZERO-TRUNCATED-ELEMENTS-SELECTS-AND-PLACEHOLDERS', truncatedElementsCount === 0, {
    truncatedElementsCount,
    sweepTruncatedCount: sweep54Summary.sweepTruncatedCount,
    extraTruncated,
    sweepTruncatedSamples: sweep54Summary.failingRoutes
      .filter((r) => r.truncatedElementsCount > 0)
      .map((r) => ({ route: r.route, truncatedItems: r.truncatedItems })),
    extraTruncatedSamples: allExtraGeoProbes
      .filter((g) => g.truncatedElementsCount > 0)
      .map((g) => ({ context: g.context, truncatedItems: g.truncatedItems })),
  });
  if (truncatedElementsCount > 0) {
    addFinding({
      id: 'QA-W20-ASSERT1-TRUNCATION',
      severity: 'MEDIUM',
      domain: 'Wave-20 Assertion 1: Zero Truncated Titles, Selects & Input Placeholders',
      ownerAgent: 'AGENT-02',
      summary: `True pixel & canvas measurement detected ${truncatedElementsCount} truncated/clipped element(s), <select> option(s), or <input placeholder>(s)`,
      evidence: {
        sweepRoutes: sweep54Summary.failingRoutes.filter((r) => r.truncatedElementsCount > 0),
        viewportAndShotProbes: allExtraGeoProbes.filter((g) => g.truncatedElementsCount > 0),
      },
    });
  }

  addCheck(
    'W20-ASSERT2-ZERO-STICKY-OVERLAP-AND-CRUSHED-HEADERS',
    stickyColumnOverlapCount === 0 && crushedTableHeaderCount === 0,
    {
      stickyColumnOverlapCount,
      crushedTableHeaderCount,
      sweepStickyOverlapCount: sweep54Summary.sweepStickyOverlapCount,
      sweepCrushedHeaderCount: sweep54Summary.sweepCrushedHeaderCount,
      extraStickyOverlap,
      extraCrushed,
      failingSweepRoutes: sweep54Summary.failingRoutes.filter(
        (r) => r.stickyColumnOverlapCount > 0 || r.crushedTableHeaderCount > 0,
      ),
      failingProbes: allExtraGeoProbes.filter(
        (g) => g.stickyColumnOverlapCount > 0 || g.crushedTableHeaderCount > 0,
      ),
    },
  );
  if (stickyColumnOverlapCount > 0 || crushedTableHeaderCount > 0) {
    addFinding({
      id: 'QA-W20-ASSERT2-TABLE-GEOMETRY',
      severity: 'HIGH',
      domain: 'Wave-20 Assertion 2: Zero Sticky Column Overlap & Zero Crushed Table Headers',
      ownerAgent: 'AGENT-02',
      summary: `Table bounding-box audit detected ${stickyColumnOverlapCount} sticky column overlap(s) and ${crushedTableHeaderCount} crushed header(s)`,
      evidence: {
        sweepRoutes: sweep54Summary.failingRoutes.filter(
          (r) => r.stickyColumnOverlapCount > 0 || r.crushedTableHeaderCount > 0,
        ),
        viewportAndShotProbes: allExtraGeoProbes.filter(
          (g) => g.stickyColumnOverlapCount > 0 || g.crushedTableHeaderCount > 0,
        ),
      },
    });
  }

  addCheck('W20-ASSERT3-ZERO-RAW-TECHNICAL-STRINGS-ACROSS-54-ROUTES', rawTechnicalStringsCount === 0, {
    rawTechnicalStringsCount,
    sweepRawTechCount: sweep54Summary.sweepRawTechCount,
    extraRawTech,
    sweepRawTechRoutes: sweep54Summary.failingRoutes
      .filter((r) => r.rawTechnicalStringsCount > 0)
      .map((r) => ({ route: r.route, rawTechMatches: r.rawTechMatches })),
    extraRawTechProbes: allExtraGeoProbes
      .filter((g) => g.rawTechnicalStringsCount > 0)
      .map((g) => ({ context: g.context, rawTechMatches: g.rawTechMatches })),
  });
  if (rawTechnicalStringsCount > 0) {
    addFinding({
      id: 'QA-W20-ASSERT3-RAW-TECH-STRINGS',
      severity: 'MEDIUM',
      domain: 'Wave-20 Assertion 3: Zero Raw Technical Strings, UOMs & ERP_KHO Company Leaks',
      ownerAgent: 'AGENT-01/AGENT-02',
      summary: `Detected ${rawTechnicalStringsCount} raw technical string/enum/UOM/company leak(s) across 54 routes & 12 states`,
      evidence: {
        sweepRoutes: sweep54Summary.failingRoutes.filter((r) => r.rawTechnicalStringsCount > 0),
        viewportAndShotProbes: allExtraGeoProbes.filter((g) => g.rawTechnicalStringsCount > 0),
      },
    });
  }

  addCheck(
    'W20-ASSERT4-ZERO-QR-MODAL-LAYOUT-SHIFT-AND-INFOBLUE',
    qrOpenLayoutShiftPx === 0 && vietQrInfoBluePendingVerified,
    { qrOpenLayoutShiftPx, vietQrInfoBluePendingVerified },
  );
  if (qrOpenLayoutShiftPx !== 0 || !vietQrInfoBluePendingVerified) {
    addFinding({
      id: 'QA-W20-ASSERT4-QR-MODAL-SHIFT',
      severity: 'MEDIUM',
      domain: 'Wave-20 Assertion 4: Zero-Layout-Shift VietQR Modal & Info Blue Pending Status',
      ownerAgent: 'AGENT-02',
      summary: `Opening VietQR caused ${qrOpenLayoutShiftPx}px layout shift or failed Info Blue pending banner check`,
      evidence: { qrOpenLayoutShiftPx, vietQrInfoBluePendingVerified },
    });
  }

  addCheck(
    'W20-ASSERT5-MOBILE-FIRST-CARD-ABOVE-230PX-AND-SINGLE-ROW-ACTIONS',
    mobileFirstCardTopPx <= 230 && mobileSingleRowActionsVerified,
    { mobileFirstCardTopPx, mobileFirstCardTopPx375, mobileFirstCardTopPx390, mobileSingleRowActionsVerified },
  );
  if (mobileFirstCardTopPx > 230 || !mobileSingleRowActionsVerified) {
    addFinding({
      id: 'QA-W20-ASSERT5-MOBILE-ABOVE-FOLD',
      severity: 'MEDIUM',
      domain: 'Wave-20 Assertion 5: Mobile First Operational Card Above Fold (<= 230px) & Single-Row Actions',
      ownerAgent: 'AGENT-02',
      summary: `Mobile /import-receipts first card top=${mobileFirstCardTopPx}px (budget <= 230px) or singleRowActions=${mobileSingleRowActionsVerified}`,
      evidence: { mobileFirstCardTopPx375, mobileFirstCardTopPx390, mobileSingleRowActionsVerified },
    });
  }

  const wave20Flags = {
    sweptRoutesTotal: sweep54Summary.sweptRoutesTotal,
    sweptCustomerRoutesCount: sweep54Summary.sweptCustomerRoutesCount,
    sweptPlatformRoutesCount: sweep54Summary.sweptPlatformRoutesCount,
    truncatedElementsCount,
    crushedTableHeaderCount,
    stickyColumnOverlapCount,
    rawTechnicalStringsCount,
    qrOpenLayoutShiftPx,
    vietQrInfoBluePendingVerified,
    mobileFirstCardTopPx,
    mobileFirstCardTopPx375,
    mobileFirstCardTopPx390,
    mobileSingleRowActionsVerified,
    adminRevokeModalVerified,
    w20PngCount,
    nonW20PngCount,
  };

  const wave19Flags = {
    statusColumnVisibleAtScrollLeft0,
    stickyRightWidthRatio,
    clippedWarehouseTextCount,
    customerReturnsZeroHorizontalScrollDesktop,
    rawEnglishEnumBadgesCount,
    duplicateWordPermissionLabelsCount,
    importReceiptsSinglePrimaryCtaMobile,
    invoiceHistoryVisibleAboveFold1440x900,
    kpiHeightDiffPx,
    minTouchTargetHeightPx,
    cleanVnDatePlaceholdersVerified,
    emptyBackordersZeroThead,
    skipLinkWorksOnFirstTab,
    modalFocusTrapAndEscapeRestore,
    prefersReducedMotionRespected,
    allTextContrastPairsPassAA,
    axeViolationCount,
    all8ViewportsZeroOverflow,
  };

  const wave18Flags = {
    all8ViewportsZeroOverflow,
    axeViolationCount,
    touchFlyoutVerified,
    importReceipts375CardListVerified,
    stickyActionRightOverflowPx,
    kitReversalModalVerified,
    customerPermissionsZeroPlatformRoles,
    vietQrCanonicalAccount98418102005Verified,
  };

  return {
    headSha: getGitHeadSha(),
    capturedAtUtc: new Date().toISOString(),
    findings,
    checks,
    wave20Flags,
    wave19Flags,
    wave18Flags,
    metrics: {
      consoleErrorCount: consoleErrors.length,
      pageErrorCount: pageErrors.length,
      unexpected5xxCount: unexpected5xxErrors.length,
      unexpectedHttp4xx: unexpected4xxErrors.length,
      emojiCount: totalEmojiCount,
      brokenImageCount: totalBrokenImages,
      maxHorizontalOverflowPx,
      stickyActionRightOverflowPx,
      ...wave20Flags,
      ...wave19Flags,
      touchFlyoutVerified,
      importReceipts375CardListVerified,
      kitReversalModalVerified,
      customerPermissionsZeroPlatformRoles,
      vietQrCanonicalAccount98418102005Verified,
      hasReplacementOrNaN,
      internalTableOverflowUnindicatedCount,
      minPrimaryButtonContrastRatio: minPrimaryContrast,
      missingAriaLabelIconButtonsCount,
    },
    diagnostics: {
      consoleErrors,
      pageErrors,
      unexpected5xxErrors,
      unexpected4xxErrors,
      expectedNegativePathResponses,
    },
    sweep54Summary,
    screenshotGeometryProbes,
    axeAuditSummary,
    eightViewportMatrix,
    wave19A11yMotion,
    rawEnumScanResults,
    touchFlyoutSummary,
    sortHeaderMetrics,
    contrastMeasurements,
    tableScrollMetrics,
    iconButtonAriaAudits,
    routeHygieneChecks,
    roleMatrixAndSecurity,
    postAuditSqlBaseline,
    screenshotsManifest,
  };
}

async function main() {
  const opts = parseCliArgs(process.argv.slice(2));
  if (opts.help) {
    console.log(
      [
        'Usage: node scripts/run-adversarial-qa-audit.mjs --round=<1|3|5> [--self-check]',
        '  --round=1     Execute Adversarial Round 1 + W20_01..12.png -> qa-round1-findings.json',
        '  --round=3     Execute Adversarial Round 3 Rebuttal Review -> qa-round3-rebuttal-review.json',
        '  --round=5     Execute Final Verification + W20_01..12.png -> qa-round5-verification.json & live-audit-summary.json',
        '  --self-check  Validate write-boundary guards, directory layout, and in-memory %PDF-1.4 fixture without starting browsers',
      ].join('\n'),
    );
    return;
  }

  if (opts.selfCheck) {
    fs.mkdirSync(QA_OUT_DIR, { recursive: true });
    fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
    const pdf = buildValidPdf14Buffer();
    let blockedSecDirWrite = false;
    try {
      assertAllowedWritePath(path.join(SEC_AUDIT_DIR_READONLY, 'probe.json'));
    } catch {
      blockedSecDirWrite = true;
    }
    let blockedEngDirWrite = false;
    try {
      assertAllowedWritePath(path.join(ENG_RESP_DIR_READONLY, 'probe.json'));
    } catch {
      blockedEngDirWrite = true;
    }
    let blockedMobbinDirWrite = false;
    try {
      assertAllowedWritePath(path.join(MOBBIN_DIR_READONLY, 'probe.json'));
    } catch {
      blockedMobbinDirWrite = true;
    }
    if (
      !blockedSecDirWrite ||
      !blockedEngDirWrite ||
      !blockedMobbinDirWrite ||
      !pdf.subarray(0, 8).toString('ascii').startsWith('%PDF-1.4')
    ) {
      throw new Error('Self-check failed boundary or PDF-1.4 validation.');
    }
    console.log(
      JSON.stringify({
        selfCheck: 'PASS',
        directiveId: DIRECTIVE_ID,
        waveId: WAVE_ID,
        qaOutDir: QA_OUT_DIR,
        screenshotDir: SCREENSHOT_DIR,
        blockedSecDirWrite,
        blockedEngDirWrite,
        blockedMobbinDirWrite,
        inMemoryPdfBytes: pdf.length,
      }),
    );
    return;
  }

  if (!opts.round) {
    throw new Error('Specify --round=1, --round=3, --round=5, or --self-check.');
  }

  // Read-only cross-references from AGENT 4 (security-audit/), AGENT 2 (ux-mobbin-research/), and AGENT MAIN (engineering-response/)
  const secBaseline = safeReadJsonOptional(path.join(SEC_AUDIT_DIR_READONLY, 'sec-baseline-audit.json'));
  const secRegister = safeReadJsonOptional(path.join(SEC_AUDIT_DIR_READONLY, 'sec-findings-register.json'));
  const secRemediation = safeReadJsonOptional(path.join(SEC_AUDIT_DIR_READONLY, 'sec-remediation-review.json'));
  const engRound2 = safeReadJsonOptional(path.join(ENG_RESP_DIR_READONLY, 'eng-round2-response.json'));
  const engRound4 = safeReadJsonOptional(path.join(ENG_RESP_DIR_READONLY, 'eng-round4-final-rebuttal.json'));
  const engWave18 = safeReadJsonOptional(path.join(ENG_RESP_DIR_READONLY, 'eng-wave18-remediation.json'));
  const engWave19 = safeReadJsonOptional(path.join(ENG_RESP_DIR_READONLY, 'eng-wave19-remediation.json'));
  const engWave20 = safeReadJsonOptional(path.join(ENG_RESP_DIR_READONLY, 'eng-wave20-remediation.json'));
  const mobbinResearch = safeReadJsonOptional(path.join(MOBBIN_DIR_READONLY, 'mobbin-design-research.json'));

  const captureScreenshots = [1, 3, 5].includes(opts.round);
  const auditResult = await executeLiveChromeAudit({ round: opts.round, captureScreenshots });
  const failedChecks = auditResult.checks.filter((c) => !c.passed);

  if (opts.round === 1) {
    const round1Payload = {
      protocol: 'CA-QA-ADVERSARIAL-001',
      round: 1,
      role: 'AGENT-03 — Independent Adversarial QA Lead',
      directiveId: DIRECTIVE_ID,
      waveId: WAVE_ID,
      auditedSha: auditResult.headSha,
      generatedAtUtc: auditResult.capturedAtUtc,
      ...auditResult.wave20Flags,
      ...auditResult.wave19Flags,
      ...auditResult.wave18Flags,
      agent4SecurityArtifactsObserved: {
        secBaselinePresent: Boolean(secBaseline && !secBaseline._readError),
        secFindingsRegisterPresent: Boolean(secRegister && !secRegister._readError),
        secRemediationReviewPresent: Boolean(secRemediation && !secRemediation._readError),
      },
      engineeringWave20RemediationObserved: Boolean(engWave20 && !engWave20._readError),
      summary: {
        totalChecksExecuted: auditResult.checks.length,
        passedChecksCount: auditResult.checks.length - failedChecks.length,
        failedChecksCount: failedChecks.length,
        openFindingsCount: auditResult.findings.length,
        screenshotsCount: auditResult.screenshotsManifest.length,
        ...auditResult.wave20Flags,
        ...auditResult.wave19Flags,
        ...auditResult.wave18Flags,
        verdict: auditResult.findings.length === 0 && failedChecks.length === 0 ? 'ZERO_DEFECTS_ROUND1' : 'FINDINGS_ISSUED_FOR_ROUND2',
      },
      screenshots: auditResult.screenshotsManifest,
      findings: auditResult.findings,
      failedChecks,
      checks: auditResult.checks,
      metrics: auditResult.metrics,
      sweep54Summary: auditResult.sweep54Summary,
      screenshotGeometryProbes: auditResult.screenshotGeometryProbes,
      axeAuditSummary: auditResult.axeAuditSummary,
      eightViewportMatrix: auditResult.eightViewportMatrix,
      wave19A11yMotion: auditResult.wave19A11yMotion,
      touchFlyoutSummary: auditResult.touchFlyoutSummary,
      sortHeaderMetrics: auditResult.sortHeaderMetrics,
      roleMatrixAndSecurity: auditResult.roleMatrixAndSecurity,
      postAuditSqlBaseline: auditResult.postAuditSqlBaseline,
    };
    safeWriteJson(path.join(QA_OUT_DIR, 'qa-round1-findings.json'), round1Payload);
    console.log(
      `ROUND 1 COMPLETE: findings=${auditResult.findings.length}, failedChecks=${failedChecks.length}, screenshots=${auditResult.screenshotsManifest.length}/12`,
    );
    return;
  }

  if (opts.round === 3) {
    const priorRound1 = safeReadJsonOptional(path.join(QA_OUT_DIR, 'qa-round1-findings.json'));
    const priorFindings = priorRound1?.findings || [];
    const currentFindingIds = new Set(auditResult.findings.map((f) => f.id));
    const rebuttalReviews = priorFindings.map((pf) => ({
      findingId: pf.id,
      previousSeverity: pf.severity,
      engineeringClaimPresent: Boolean(
        (engWave20 && !engWave20._readError) ||
          (engWave19 && !engWave19._readError) ||
          (engWave18 && !engWave18._readError) ||
          (engRound2 && !engRound2._readError),
      ),
      independentReverificationStatus: currentFindingIds.has(pf.id)
        ? 'REJECTED_REBUTTAL_STILL_FAILING'
        : 'VERIFIED_FIXED',
    }));

    const round3Verdict =
      auditResult.findings.length === 0 && failedChecks.length === 0
        ? 'READY_FOR_ROUND5_FINAL_VERIFICATION'
        : 'REQUIRES_ROUND4_ENGINEERING_REMEDIATION';

    const round3Payload = {
      protocol: 'CA-QA-ADVERSARIAL-001',
      round: 3,
      role: 'AGENT-03 — Independent Adversarial QA Lead',
      directiveId: DIRECTIVE_ID,
      waveId: WAVE_ID,
      auditedSha: auditResult.headSha,
      generatedAtUtc: auditResult.capturedAtUtc,
      ...auditResult.wave20Flags,
      ...auditResult.wave19Flags,
      ...auditResult.wave18Flags,
      engineeringRound2ResponsePresent: Boolean(engRound2 && !engRound2._readError),
      engineeringWave20RemediationPresent: Boolean(engWave20 && !engWave20._readError),
      summary: {
        totalChecksExecuted: auditResult.checks.length,
        passedChecksCount: auditResult.checks.length - failedChecks.length,
        failedChecksCount: failedChecks.length,
        remainingFindingsCount: auditResult.findings.length,
        screenshotsCount: auditResult.screenshotsManifest.length,
        ...auditResult.wave20Flags,
        ...auditResult.wave19Flags,
        ...auditResult.wave18Flags,
        verdict: round3Verdict,
      },
      screenshots: auditResult.screenshotsManifest,
      rebuttalReviews,
      newOrRemainingFindings: auditResult.findings,
      failedChecks,
      checks: auditResult.checks,
      metrics: auditResult.metrics,
      sweep54Summary: auditResult.sweep54Summary,
      screenshotGeometryProbes: auditResult.screenshotGeometryProbes,
      axeAuditSummary: auditResult.axeAuditSummary,
      eightViewportMatrix: auditResult.eightViewportMatrix,
      wave19A11yMotion: auditResult.wave19A11yMotion,
      touchFlyoutSummary: auditResult.touchFlyoutSummary,
      sortHeaderMetrics: auditResult.sortHeaderMetrics,
      roleMatrixAndSecurity: auditResult.roleMatrixAndSecurity,
      postAuditSqlBaseline: auditResult.postAuditSqlBaseline,
      verdict: round3Verdict,
    };
    safeWriteJson(path.join(QA_OUT_DIR, 'qa-round3-rebuttal-review.json'), round3Payload);
    console.log(`ROUND 3 COMPLETE: remainingFindings=${auditResult.findings.length}, failedChecks=${failedChecks.length}`);
    return;
  }

  if (opts.round === 5) {
    const secRecordsVerified =
      Boolean(secBaseline && !secBaseline._readError) &&
      Boolean(secRegister && !secRegister._readError) &&
      Boolean(secRemediation && !secRemediation._readError);
    const wave20CompanionRecordsVerified =
      Boolean(engWave20 && !engWave20._readError) &&
      Boolean(mobbinResearch && !mobbinResearch._readError);

    const f20 = auditResult.wave20Flags;
    const f19 = auditResult.wave19Flags;
    const f18 = auditResult.wave18Flags;
    const allScreenshotsCaptured =
      auditResult.screenshotsManifest.length === 12 &&
      auditResult.screenshotsManifest.every((s) => s.fileName.startsWith('W20_')) &&
      f20.w20PngCount === 12 &&
      f20.nonW20PngCount === 0;

    const wave20FlagsAllValid =
      f20.sweptRoutesTotal === 54 &&
      f20.truncatedElementsCount === 0 &&
      f20.crushedTableHeaderCount === 0 &&
      f20.stickyColumnOverlapCount === 0 &&
      f20.rawTechnicalStringsCount === 0 &&
      f20.qrOpenLayoutShiftPx === 0 &&
      f20.vietQrInfoBluePendingVerified === true &&
      f20.mobileFirstCardTopPx <= 230 &&
      f20.mobileSingleRowActionsVerified === true &&
      f20.adminRevokeModalVerified === true &&
      f19.statusColumnVisibleAtScrollLeft0 === true &&
      f19.stickyRightWidthRatio <= 0.25 &&
      f19.clippedWarehouseTextCount === 0 &&
      f19.customerReturnsZeroHorizontalScrollDesktop === true &&
      f19.rawEnglishEnumBadgesCount === 0 &&
      f19.duplicateWordPermissionLabelsCount === 0 &&
      f19.importReceiptsSinglePrimaryCtaMobile === true &&
      f19.invoiceHistoryVisibleAboveFold1440x900 === true &&
      f19.kpiHeightDiffPx === 0 &&
      f19.minTouchTargetHeightPx >= 44 &&
      f19.cleanVnDatePlaceholdersVerified === true &&
      f19.emptyBackordersZeroThead === true &&
      f19.skipLinkWorksOnFirstTab === true &&
      f19.modalFocusTrapAndEscapeRestore === true &&
      f19.prefersReducedMotionRespected === true &&
      f19.allTextContrastPairsPassAA === true &&
      f19.axeViolationCount === 0 &&
      f19.all8ViewportsZeroOverflow === true &&
      f18.touchFlyoutVerified === true &&
      f18.importReceipts375CardListVerified === true &&
      f18.stickyActionRightOverflowPx === 0 &&
      f18.kitReversalModalVerified === true &&
      f18.customerPermissionsZeroPlatformRoles === true &&
      f18.vietQrCanonicalAccount98418102005Verified === true;

    const allPassed =
      auditResult.findings.length === 0 &&
      failedChecks.length === 0 &&
      allScreenshotsCaptured &&
      secRecordsVerified &&
      wave20CompanionRecordsVerified &&
      wave20FlagsAllValid;

    const round5Payload = {
      protocol: 'CA-QA-ADVERSARIAL-001',
      round: 5,
      role: 'AGENT-03 — Independent Adversarial QA & Final Verification Lead',
      directiveId: DIRECTIVE_ID,
      waveId: WAVE_ID,
      auditedSha: auditResult.headSha,
      generatedAtUtc: auditResult.capturedAtUtc,
      ...auditResult.wave20Flags,
      ...auditResult.wave19Flags,
      ...auditResult.wave18Flags,
      summary: {
        totalChecksExecuted: auditResult.checks.length,
        passedChecksCount: auditResult.checks.length - failedChecks.length,
        failedChecksCount: failedChecks.length,
        openFindingsCount: auditResult.findings.length,
        screenshotsCount: auditResult.screenshotsManifest.length,
        ...auditResult.wave20Flags,
        ...auditResult.wave19Flags,
        ...auditResult.wave18Flags,
        finalVerdict: allPassed ? 'PASS' : 'FAIL',
      },
      securityAndGovernanceRecordsCrossChecked: {
        secBaselinePresent: Boolean(secBaseline && !secBaseline._readError),
        secFindingsRegisterPresent: Boolean(secRegister && !secRegister._readError),
        secRemediationReviewPresent: Boolean(secRemediation && !secRemediation._readError),
        engineeringRound2Present: Boolean(engRound2 && !engRound2._readError),
        engineeringRound4Present: Boolean(engRound4 && !engRound4._readError),
        engineeringWave18RemediationPresent: Boolean(engWave18 && !engWave18._readError),
        engineeringWave19RemediationPresent: Boolean(engWave19 && !engWave19._readError),
        engineeringWave20RemediationPresent: Boolean(engWave20 && !engWave20._readError),
        mobbinDesignResearchPresent: Boolean(mobbinResearch && !mobbinResearch._readError),
      },
      screenshotsCount: auditResult.screenshotsManifest.length,
      screenshots: auditResult.screenshotsManifest,
      openFindingsCount: auditResult.findings.length,
      openFindings: auditResult.findings,
      failedChecksCount: failedChecks.length,
      failedChecks,
      checks: auditResult.checks,
      metrics: auditResult.metrics,
      sweep54Summary: auditResult.sweep54Summary,
      screenshotGeometryProbes: auditResult.screenshotGeometryProbes,
      axeAuditSummary: auditResult.axeAuditSummary,
      eightViewportMatrix: auditResult.eightViewportMatrix,
      wave19A11yMotion: auditResult.wave19A11yMotion,
      touchFlyoutSummary: auditResult.touchFlyoutSummary,
      sortHeaderMetrics: auditResult.sortHeaderMetrics,
      roleMatrixAndSecurity: auditResult.roleMatrixAndSecurity,
      postAuditSqlBaseline: auditResult.postAuditSqlBaseline,
      finalVerdict: allPassed ? 'PASS' : 'FAIL',
    };

    const liveSummaryPayload = {
      directiveId: DIRECTIVE_ID,
      waveId: WAVE_ID,
      auditedSha: auditResult.headSha,
      capturedAtUtc: auditResult.capturedAtUtc,
      browserChannel: 'chrome',
      zeroDomMutationsInjected: true,
      allPassed,
      ...auditResult.wave20Flags,
      ...auditResult.wave19Flags,
      ...auditResult.wave18Flags,
      metrics: auditResult.metrics,
      screenshotsCaptured: auditResult.screenshotsManifest.map((s) => s.fileName),
      screenshotsManifest: auditResult.screenshotsManifest,
      checks: auditResult.checks,
      sweep54Summary: auditResult.sweep54Summary,
      screenshotGeometryProbes: auditResult.screenshotGeometryProbes,
      axeAuditSummary: auditResult.axeAuditSummary,
      eightViewportMatrix: auditResult.eightViewportMatrix,
      wave19A11yMotion: auditResult.wave19A11yMotion,
      rawEnumScanResults: auditResult.rawEnumScanResults,
      touchFlyoutSummary: auditResult.touchFlyoutSummary,
      sortHeaderMetrics: auditResult.sortHeaderMetrics,
      contrastMeasurements: auditResult.contrastMeasurements,
      tableScrollMetrics: auditResult.tableScrollMetrics,
      iconButtonAriaAudits: auditResult.iconButtonAriaAudits,
      routeHygieneChecks: auditResult.routeHygieneChecks,
      roleMatrixAndSecurity: auditResult.roleMatrixAndSecurity,
      postAuditSqlBaseline: auditResult.postAuditSqlBaseline,
      diagnostics: auditResult.diagnostics,
    };

    safeWriteJson(path.join(QA_OUT_DIR, 'qa-round5-verification.json'), round5Payload);
    safeWriteJson(path.join(QA_OUT_DIR, 'live-audit-summary.json'), liveSummaryPayload);

    if (!allPassed && opts.strictExit) {
      throw new Error(
        `ROUND 5 VERIFICATION FAILED: findings=${auditResult.findings.length}, failedChecks=${failedChecks.length}, screenshots=${auditResult.screenshotsManifest.length}/12, secRecordsVerified=${secRecordsVerified}, wave20CompanionRecordsVerified=${wave20CompanionRecordsVerified}, wave20FlagsAllValid=${wave20FlagsAllValid}`,
      );
    }
    console.log(`ROUND 5 COMPLETE: finalVerdict=${round5Payload.finalVerdict}, screenshots=${auditResult.screenshotsManifest.length}/12`);
  }
}

main().catch((err) => {
  console.error('ADVERSARIAL QA AUDIT RUNNER ERROR:', err?.stack || err?.message || err);
  process.exit(1);
});
