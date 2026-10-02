import type { BlueprintCapability, BlueprintModule, BlueprintStatus } from './erpWmsBlueprint';

export type ReleaseWave = 0 | 1 | 2 | 3 | 4 | 5 | 6;
export type Applicability =
  | 'REQUIRED_CORE'
  | 'REQUIRED_WHEN_FEATURE_ENABLED'
  | 'INDUSTRY_OPTIONAL'
  | 'IMPLEMENTATION_SPECIFIC';
export type CapabilityMaturity = 'M0' | 'M1' | 'M2' | 'M3' | 'M4' | 'M5';
export type EvidenceStatus = 'covered' | 'partial' | 'spec-only' | 'missing';

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

export const evidenceStatusLabels: Record<EvidenceStatus, string> = {
  covered: 'Covered',
  partial: 'Partial',
  'spec-only': 'Spec only',
  missing: 'Missing',
};
