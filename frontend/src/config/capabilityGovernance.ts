import type { BlueprintCapability, BlueprintModule, BlueprintStatus } from './erpWmsBlueprint';

export type ReleaseWave = 0 | 1 | 2 | 3 | 4 | 5 | 6;
export type Applicability =
  | 'REQUIRED_CORE'
  | 'REQUIRED_WHEN_FEATURE_ENABLED'
  | 'INDUSTRY_OPTIONAL'
  | 'IMPLEMENTATION_SPECIFIC';
export type CapabilityMaturity = 'M0' | 'M1' | 'M2' | 'M3' | 'M4' | 'M5';
export type EvidenceStatus = 'covered' | 'partial' | 'spec-only' | 'missing';
export type PlatformStandardRepresentation = 'Capability-backed' | 'Platform guardrail' | 'Runbook-backed';

export interface PlatformStandard {
  spec: number;
  title: string;
  category: 'Data Platform' | 'Engineering' | 'Security / Compliance' | 'UX / Product' | 'Operations / Governance';
  representation: PlatformStandardRepresentation;
  mappedCapabilityIds?: string[];
}

export interface CapabilityEvidenceItem {
  key: string;
  label: string;
  status: EvidenceStatus;
  note: string;
}

export interface CapabilityGovernanceProfile {
  releaseWave: ReleaseWave;
  applicability: Applicability;
  maturity: CapabilityMaturity;
  ownerModule: string;
  referencedSpecs: number[];
  permissionModel: string;
  commandApiModel: string;
  stateModel: string;
  inventoryEffect: string;
  operationalOwner: string;
  reviewStatus: 'Designed' | 'Review Required';
  screenReference?: string;
  reviewFinding?: string;
  evidence: CapabilityEvidenceItem[];
}

const moduleWaveDefaults: Record<string, ReleaseWave> = {
  overview: 0,
  'master-data': 0,
  'warehouse-structure': 0,
  inbound: 2,
  outbound: 2,
  'inventory-control': 1,
  'transfer-replenishment': 3,
  'count-adjustment': 3,
  'quality-returns': 3,
  'handling-packaging': 4,
  'dock-yard-crossdock': 4,
  'reports-analytics': 6,
  administration: 0,
  integration: 0,
  mobile: 2,
  'operations-resilience': 0,
  'advanced-planning': 6,
};

const waveOverrides: Record<string, ReleaseWave> = {
  'OV-02': 4,
  'OV-05': 6,
  'OV-06': 4,
  'OUT-04': 4,
  'OUT-10': 4,
  'QR-05': 4,
  'RP-01': 1,
  'RP-02': 1,
  'RP-05': 3,
  'RP-06': 1,
  'RP-07': 4,
  'MO-06': 3,
  'MO-07': 3,
  'MO-08': 3,
  'MO-09': 3,
  'AX-01': 4,
  'AX-02': 4,
  'AX-04': 4,
  'AX-08': 4,
  'AX-05': 5,
  'AX-09': 5,
  'AX-10': 5,
  'AX-11': 4,
  'AX-12': 4,
  'AX-13': 5,
  'AX-14': 4,
  'AX-15': 4,
  'AX-16': 4,
  'AX-17': 4,
  'AX-18': 4,
  'AX-19': 4,
  'AX-20': 4,
  'AX-21': 5,
  'AX-22': 5,
};

const featureEnabledIds = new Set([
  'MD-06',
  'OUT-10',
  'HU-04',
  'DY-01',
  'DY-02',
  'DY-03',
  'DY-04',
  'IG-06',
  'AD-08',
  'AX-08',
  'RP-10',
  'AD-12',
  'AD-13',
  'AD-14',
  'MD-09',
  'IG-09',
]);

const implementationSpecificModules = new Set(['operations-resilience']);

const reviewRequired: Record<string, { screenReference: string; finding: string }> = {
  'INV-08': {
    screenReference: 'UNMAPPED — Chuyển vị trí (Location Transfer)',
    finding: 'PASS content / Review Required traceability — Screen Matrix 229, 2026-09-20',
  },
  'OUT-05': {
    screenReference: 'UNMAPPED — Lấy hàng(Picking)',
    finding: 'PASS content / Review Required traceability — Screen Matrix 229, 2026-09-20',
  },
  'OUT-06': {
    screenReference: 'UNMAPPED — Đóng gói (Packing)',
    finding: 'PASS content / Review Required traceability — Screen Matrix 229, 2026-09-20',
  },
  'TR-01': {
    screenReference: 'UNMAPPED — Tạo phiếu chuyển kho',
    finding: 'PASS content / Review Required traceability — Screen Matrix 229, 2026-09-20',
  },
};

const maturityByStatus: Record<BlueprintStatus, CapabilityMaturity> = {
  live: 'M2',
  foundation: 'M1',
  planned: 'M0',
  optional: 'M0',
};

const implementationEvidence = (status: BlueprintStatus): EvidenceStatus => {
  if (status === 'live') return 'covered';
  if (status === 'foundation') return 'partial';
  return 'spec-only';
};

const inventoryEffects: Record<string, string> = {
  'IN-04': 'Ledger-neutral receiving capture; no warehouse OnHand change before Receipt POST.',
  'IN-06': 'QC/disposition is ledger-neutral before Receipt POST; determines posting bucket.',
  'IN-07': 'Inventory posting boundary: accepted/damaged quantities enter canonical inventory exactly once.',
  'IN-08': 'Internal location movement after Receipt POST; warehouse total remains unchanged.',
  'OUT-02': 'Reservation projection only: Available decreases; warehouse OnHand unchanged.',
  'OUT-03': 'Allocation projection only: binds reserved quantity to eligible stock/location; OnHand unchanged.',
  'OUT-05': 'Physical pick execution; warehouse OnHand remains until shipment dispatch.',
  'OUT-06': 'Pack/consolidate inventory into HU/carton; no warehouse-level OnHand deduction.',
  'OUT-08': 'Shipment dispatch is the outbound physical deduction boundary; OnHand decreases exactly once.',
  'OUT-10': 'Tracking/POD is ledger-neutral; failed-delivery return requires explicit return/receive posting.',
  'INV-02': 'Immutable signed ledger is transactional truth for inventory movements.',
  'INV-03': 'Read/projection layer rebuilt from immutable ledger; never owns transactional truth.',
  'INV-04': 'Read/projection calculation over eligible status, locks, reservations and allocations.',
  'INV-05': 'Status transition controls eligibility; any quantity movement remains ledger-driven.',
  'INV-08': 'Same-warehouse location transfer; source decreases, destination increases, warehouse total conserved.',
  'INV-09': 'Reversal/corrective transaction; original posted ledger row remains immutable.',
  'INV-11': 'Integrity/reconciliation reads ledger and projection; rebuild does not rewrite historical ledger.',
  'TR-02': 'Read model for in-transit quantity and source/destination/reference dimensions.',
  'TR-03': 'Transfer dispatch moves quantity source → transit exactly once.',
  'TR-04': 'Transfer receive moves quantity transit → destination exactly once.',
  'TR-05': 'Internal replenish movement between reserve/pick locations; warehouse total conserved.',
  'CT-07': 'Adjustment posting boundary after review/approval; creates explicit signed ledger delta.',
  'QR-02': 'Status transition to/from QC_HOLD/QUARANTINE controls eligibility.',
  'QR-03': 'Damage finding records status/disposition; stock effect occurs only through approved canonical posting.',
  'QR-04': 'Customer return adds stock only at Return Receipt POST after inspection/disposition.',
  'QR-05': 'Recall blocks eligible stock by status/policy; does not silently rewrite ledger.',
  'QR-06': 'Scrap posting reduces inventory through approved explicit ledger transaction.',
  'HU-01': 'HU containment/location state changes must preserve underlying inventory truth.',
  'DY-04': 'Cross-dock may bypass storage, but receipt/dispatch posting boundaries remain canonical.',
  'RP-06': 'Read-only reconciliation/reporting over ledger-derived vs operational balances.',
  'AX-09': 'Kitting/de-kitting uses explicit transformation posting; components/output remain traceable.',
  'RP-09': 'Read-only analytical KPI derived from governed reporting semantics; no transactional inventory mutation.',
  'RP-10': 'Analytical export is read-only and must preserve lineage back to OLTP/ledger truth.',
  'AD-12': 'Presentation-only localization; canonical codes/quantities remain unchanged.',
  'AD-13': 'Legal hold preserves evidence/retention and must not rewrite inventory or audit history.',
  'AD-14': 'Privacy handling may redact/export allowed personal data but must not corrupt business/audit/legal truth.',
  'OP-08': 'Telemetry observes workflow friction/latency only; it is never a business inventory KPI source of truth.',
  'OP-09': 'Offboarding is ledger-neutral until canonical inventory/transfer workflows bring balances to zero before closure.',
  'AX-11': 'Cartonization is a recommendation/packing optimization; it cannot post inventory or mark shipment dispatched.',
  'AX-12': 'Load planning is operational optimization; loading/dispatch inventory boundaries remain canonical.',
  'AX-13': 'Disposition execution uses explicit return/status/reclassification/scrap postings; no silent balance rewrite.',
  'AX-14': 'Safety policy gates task eligibility/execution; it does not directly mutate inventory quantity.',
  'AX-15': 'Automation/device jobs never own inventory truth; resulting WMS commands use canonical posting contracts.',
  'AX-16': 'RFID/IoT capture is observational until validated WMS commands apply canonical state/inventory changes.',
  'AX-17': 'Voice/light assistance guides task execution but does not bypass canonical command/idempotency rules.',
  'AX-18': 'Hazmat policy constrains storage/eligibility; any movement/status change remains canonical and auditable.',
  'AX-19': 'Temperature excursions create evidence/hold/disposition; quantity changes require explicit canonical posting.',
  'AX-20': 'Dual-UOM quantities are persisted with canonical conversion/tolerance and immutable ledger traceability.',
  'AX-21': 'Owner changes are explicit inventory reclassification transactions; physical quantity is conserved.',
  'AX-22': 'Rating/billing consumes operational evidence as read models and does not own inventory truth.',
  'AX-23': 'ATP/CTP/promise calculation is read-only planning output; reservation requires an explicit canonical command.',
  'AX-24': 'Balancing produces transfer proposals only; inventory moves through canonical Transfer Dispatch/Receive.',
  'AX-25': 'Inter-warehouse replenishment planning is recommendation-only; execution uses canonical transfer/replenishment commands.',
  'AX-26': 'Routing selects fulfillment nodes but does not mutate stock; downstream reservation/allocation remains authoritative.',
  'AX-27': 'Forecast/demand signals are planning inputs only and never inventory truth.',
  'AX-28': 'Safety-stock/reorder policies affect planning thresholds and projections, not physical quantity directly.',
  'AX-29': 'ABC/XYZ classification is analytical metadata and does not mutate inventory.',
  'AX-30': 'Optimization policy generates recommendations; accepted execution still uses canonical business commands.',
  'AX-31': 'Simulation runs on immutable snapshots and cannot mutate production master, documents or ledger.',
  'AX-32': 'Forecast accuracy/bias is analytical measurement only.',
  'AX-33': 'Replenishment exceptions are workflow/read-model state; resolution routes through canonical replenish/transfer actions.',
  'AX-34': 'Procurement suggestion does not create inventory or PO truth until approved integration to ERP/Procurement.',
  'AX-35': 'Risk score is explainable/read-only decision support and cannot create hidden business mutation.',
  'AX-36': 'Lead-time intelligence is analytical and does not mutate PO/receipt/inventory truth.',
  'AX-37': 'Expedite/defer is a recommendation; ERP/Procurement remains commercial supply truth.',
  'AX-38': 'Demand anomaly detection is advisory and must not auto-mutate replenishment/inventory without governed policy.',
  'AX-39': 'Fairness policy produces deterministic allocation proposals; actual allocation uses canonical atomic allocation command.',
  'AX-40': 'Service-level segmentation is governed policy metadata and does not directly mutate inventory.',
  'AX-41': 'What-if scenarios are isolated from production transaction/master/ledger data.',
  'AX-42': 'Decision policy registry governs/version-controls recommendations; execution remains human/contract gated where required.',
  'MD-09': 'SLA contract is policy/semantic metadata; it does not mutate documents or inventory directly.',
  'WH-07': 'Calendar exception controls execution eligibility/time semantics and has no direct inventory quantity effect.',
  'IG-09': 'WMS exports quantity/movement/valuation events but does not calculate accounting cost/tax/multi-currency truth.',
  'IG-10': 'Reconciliation dashboard is read/control plane; retries must remain idempotent and cannot invent business mutations.',
  'OP-10': 'Posted business errors require reversal + corrected transaction; controlled repair must never rewrite immutable ledger history.',
  'OP-11': 'Support tools call Application Layer contracts and preserve permission/audit/inventory safeguards; no routine direct DB mutation.',
  'OV-08': 'Activity feed is a read model over governed events/audit/notifications and cannot become transactional truth.',
  'MO-11': 'Product lookup is read-only exact search within security scope.',
  'MO-12': 'Exception handling changes exception/task workflow only; any inventory effect must route through the owning canonical command.',
};

const parseSpecNumbers = (spec: string) =>
  Array.from(new Set((spec.match(/\d+/g) ?? []).map(Number))).sort((a, b) => a - b);

const releaseWaveFor = (moduleKey: string, capabilityId: string): ReleaseWave =>
  waveOverrides[capabilityId] ?? moduleWaveDefaults[moduleKey] ?? 0;

const applicabilityFor = (moduleKey: string, capability: BlueprintCapability): Applicability => {
  if (capability.status === 'optional') return 'INDUSTRY_OPTIONAL';
  if (featureEnabledIds.has(capability.id)) return 'REQUIRED_WHEN_FEATURE_ENABLED';
  if (implementationSpecificModules.has(moduleKey)) return 'IMPLEMENTATION_SPECIFIC';
  return 'REQUIRED_CORE';
};

const permissionModelFor = (capability: BlueprintCapability) =>
  capability.status === 'live'
    ? 'Permission Registry 17 + warehouse scope; production route exists and server remains authoritative.'
    : 'Permission Registry 17 + warehouse scope required; exact implementation code must be mapped before production enablement.';

const commandApiModelFor = (capability: BlueprintCapability) => {
  if (!capability.surfaces.includes('API')) return 'No direct API surface required by this capability preview.';
  if (capability.status === 'live') return 'Existing API/route evidence in branch; command semantics remain governed by referenced canonical specs.';
  if (capability.status === 'foundation') return 'Partial API foundation exists; command/state/error contract still requires completion evidence.';
  return 'Canonical spec defines required command/API behavior; implementation mapping is intentionally not claimed by the blueprint.';
};

const evidenceFor = (capability: BlueprintCapability, inventoryEffect: string): CapabilityEvidenceItem[] => {
  const implementation = implementationEvidence(capability.status);
  return [
    { key: 'business', label: 'Business Rule', status: 'covered', note: 'Referenced canonical specification exists.' },
    { key: 'data', label: 'Data Model', status: capability.status === 'live' ? 'covered' : implementation, note: 'Production evidence follows implementation maturity; blueprint does not fabricate tables.' },
    { key: 'api', label: 'Command / API', status: implementation, note: commandApiModelFor(capability) },
    { key: 'permission', label: 'Permission', status: capability.status === 'live' ? 'partial' : 'spec-only', note: permissionModelFor(capability) },
    { key: 'ux', label: 'UX / Screen', status: 'covered', note: 'Dedicated capability preview + module work center exists.' },
    { key: 'inventory', label: 'Inventory Effect', status: 'covered', note: inventoryEffect },
    { key: 'event', label: 'Event / Error', status: capability.status === 'live' ? 'partial' : 'spec-only', note: 'Must remain aligned with canonical event/error contracts and idempotency policy.' },
    { key: 'test', label: 'Test Evidence', status: capability.status === 'live' ? 'partial' : 'spec-only', note: 'Blueprint/mock tests exist; production successor/integration evidence is separate.' },
    { key: 'operations', label: 'Operational Ownership', status: 'covered', note: 'Owning module is explicit; runbook/release evidence follows implementation maturity.' },
  ];
};

export const getCapabilityGovernanceProfile = (
  module: BlueprintModule,
  capability: BlueprintCapability,
): CapabilityGovernanceProfile => {
  const inventoryEffect = inventoryEffects[capability.id]
    ?? 'No ad-hoc balance mutation. Follow the referenced canonical spec; reporting/read-model capabilities remain ledger-neutral.';
  const review = reviewRequired[capability.id];

  return {
    releaseWave: releaseWaveFor(module.key, capability.id),
    applicability: applicabilityFor(module.key, capability),
    maturity: maturityByStatus[capability.status],
    ownerModule: module.name,
    referencedSpecs: parseSpecNumbers(capability.spec),
    permissionModel: permissionModelFor(capability),
    commandApiModel: commandApiModelFor(capability),
    stateModel: module.flow?.join(' → ') ?? 'State transition contract is defined by the capability specification.',
    inventoryEffect,
    operationalOwner: module.name,
    reviewStatus: review ? 'Review Required' : 'Designed',
    screenReference: review?.screenReference,
    reviewFinding: review?.finding,
    evidence: evidenceFor(capability, inventoryEffect),
  };
};

export const getBlueprintGovernanceRows = (modules: BlueprintModule[]) =>
  modules.flatMap((module) =>
    module.capabilities.map((capability) => ({
      module,
      capability,
      profile: getCapabilityGovernanceProfile(module, capability),
    })),
  );

export const platformStandards: PlatformStandard[] = [
  { spec: 251, title: 'CDC, Data Replication & Read Replica Specification', category: 'Data Platform', representation: 'Platform guardrail' },
  { spec: 252, title: 'Schema Migration, Zero-Downtime Compatibility & Database Evolution Standard', category: 'Engineering', representation: 'Platform guardrail' },
  { spec: 253, title: 'API SDK, Client Contract & Frontend Data Access Standard', category: 'Engineering', representation: 'Platform guardrail' },
  { spec: 254, title: 'Feature Telemetry, Product Analytics & UX Measurement Specification', category: 'UX / Product', representation: 'Capability-backed', mappedCapabilityIds: ['OP-08'] },
  { spec: 255, title: 'Browser, Device, Scanner & Peripheral Compatibility Matrix', category: 'Engineering', representation: 'Platform guardrail' },
  { spec: 256, title: 'Localization Pack, Units, Locale & Regional Format Governance', category: 'UX / Product', representation: 'Capability-backed', mappedCapabilityIds: ['AD-12'] },
  { spec: 257, title: 'Legal Hold, eDiscovery & Regulatory Evidence Handling Specification', category: 'Security / Compliance', representation: 'Capability-backed', mappedCapabilityIds: ['AD-13'] },
  { spec: 258, title: 'Privacy Request & Data Subject Handling Boundary Specification', category: 'Security / Compliance', representation: 'Capability-backed', mappedCapabilityIds: ['AD-14'] },
  { spec: 259, title: 'Vendor, Dependency & Open-Source Governance Specification', category: 'Engineering', representation: 'Platform guardrail' },
  { spec: 260, title: 'SBOM, Software Supply Chain & Artifact Provenance Specification', category: 'Security / Compliance', representation: 'Platform guardrail' },
  { spec: 261, title: 'Penetration Testing, Vulnerability Management & Security Verification Standard', category: 'Security / Compliance', representation: 'Platform guardrail' },
  { spec: 262, title: 'Chaos Engineering, Resilience Verification & Failure Injection Standard', category: 'Engineering', representation: 'Platform guardrail' },
  { spec: 263, title: 'Performance Budget by Screen, API & Mobile Workflow Specification', category: 'Engineering', representation: 'Platform guardrail' },
  { spec: 264, title: 'Tenant Provisioning, Company Onboarding & Warehouse Setup Runbook', category: 'Operations / Governance', representation: 'Runbook-backed', mappedCapabilityIds: ['OP-06'] },
  { spec: 265, title: 'Warehouse Decommissioning, Tenant Offboarding & Data Exit Runbook', category: 'Operations / Governance', representation: 'Runbook-backed', mappedCapabilityIds: ['OP-09'] },
  { spec: 266, title: 'Product Requirements Document (PRD) Standard & Feature Discovery Template', category: 'UX / Product', representation: 'Platform guardrail' },
  { spec: 267, title: 'Persona, Role, Job-to-be-Done & Operational Context Catalog', category: 'UX / Product', representation: 'Platform guardrail' },
  { spec: 268, title: 'User Journey, Service Blueprint & Cross-Channel Experience Specification', category: 'UX / Product', representation: 'Platform guardrail' },
  { spec: 269, title: 'UX Research, Usability Testing & Design Validation Standard', category: 'UX / Product', representation: 'Platform guardrail' },
  { spec: 270, title: 'Design Token, Component API & Storybook Governance Specification', category: 'UX / Product', representation: 'Platform guardrail' },
  { spec: 271, title: 'Notification Center, Inbox & Actionable Alert UX Specification', category: 'UX / Product', representation: 'Capability-backed', mappedCapabilityIds: ['AD-07'] },
  { spec: 272, title: 'Offline UX Conflict Resolution & Deferred Command Experience Specification', category: 'UX / Product', representation: 'Capability-backed', mappedCapabilityIds: ['MO-10', 'OP-05'] },
  { spec: 273, title: 'Accessibility Conformance & Assistive Technology Test Catalog', category: 'UX / Product', representation: 'Platform guardrail' },
  { spec: 274, title: 'Enterprise Audit of Screen-to-API-to-Permission-to-State Coverage Matrix', category: 'Operations / Governance', representation: 'Platform guardrail' },
  { spec: 275, title: 'Final Documentation Coverage Audit & Completeness Register', category: 'Operations / Governance', representation: 'Platform guardrail' },
  { spec: 276, title: 'Administrative Console & Configuration UX Specification', category: 'UX / Product', representation: 'Capability-backed', mappedCapabilityIds: ['AD-05', 'AD-10'] },
  { spec: 277, title: 'Warehouse Site Commissioning & Go-Live Readiness Specification', category: 'Operations / Governance', representation: 'Runbook-backed', mappedCapabilityIds: ['OP-06'] },
  { spec: 278, title: 'Release Communication, Change Adoption & Operator Enablement Specification', category: 'Operations / Governance', representation: 'Platform guardrail' },
  { spec: 279, title: 'Identity Federation, SSO & Enterprise Access Integration Specification', category: 'Security / Compliance', representation: 'Capability-backed', mappedCapabilityIds: ['AD-08'] },
  { spec: 280, title: 'Evidence Export, Audit Package & Compliance Reporting Specification', category: 'Security / Compliance', representation: 'Capability-backed', mappedCapabilityIds: ['RP-08', 'AD-13'] },
  { spec: 281, title: 'Manual Contingency Forms & Post-Outage Reconciliation Procedure', category: 'Operations / Governance', representation: 'Runbook-backed', mappedCapabilityIds: ['OP-07'] },
  { spec: 282, title: 'UX Governance & Screen Matrix 229 Master Handoff Specification', category: 'UX / Product', representation: 'Platform guardrail', mappedCapabilityIds: ['INV-08', 'OUT-05', 'OUT-06', 'TR-01'] },
];

export const evidenceStatusLabels: Record<EvidenceStatus, string> = {
  covered: 'Covered',
  partial: 'Partial',
  'spec-only': 'Spec only',
  missing: 'Missing',
};
