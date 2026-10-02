import type { BlueprintCapability, BlueprintModule, BlueprintStatus } from './erpWmsBlueprint';
import { canonicalSpecTitles } from './documentationRegister';

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

const waveOverrideCatalog = `OV-02	4\nOV-05	6\nOV-06	4\nOUT-04	4\nOUT-10	4\nQR-05	4\nRP-01	1\nRP-02	1\nRP-05	3\nRP-06	1\nRP-07	4\nMO-06	3\nMO-07	3\nMO-08	3\nMO-09	3\nAX-01	4\nAX-02	4\nAX-04	4\nAX-08	4\nAX-05	5\nAX-09	5\nAX-10	5\nAX-11	4\nAX-12	4\nAX-13	5\nAX-14	4\nAX-15	4\nAX-16	4\nAX-17	4\nAX-18	4\nAX-19	4\nAX-20	4\nAX-21	5\nAX-22	5`;

const waveOverrides: Record<string, ReleaseWave> = Object.fromEntries(
  waveOverrideCatalog.split('\n').map((row) => {
    const [capabilityId, waveValue] = row.split('\t');
    const wave = Number(waveValue);
    if (!capabilityId || !Number.isInteger(wave) || wave < 0 || wave > 6) {
      throw new Error('Invalid capability release-wave override.');
    }
    return [capabilityId, wave as ReleaseWave] as const;
  }),
);

const featureEnabledIds = new Set(`MD-06 OUT-10 HU-04 DY-01 DY-02 DY-03 DY-04 IG-06 AD-08 AX-08 RP-10 AD-12 AD-13 AD-14 MD-09 IG-09`.split(' '));

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

const inventoryEffectCatalog = `IN-04	Ledger-neutral receiving capture; no warehouse OnHand change before Receipt POST.\nIN-06	QC/disposition is ledger-neutral before Receipt POST; determines posting bucket.\nIN-07	Inventory posting boundary: accepted/damaged quantities enter canonical inventory exactly once.\nIN-08	Internal location movement after Receipt POST; warehouse total remains unchanged.\nOUT-02	Reservation projection only: Available decreases; warehouse OnHand unchanged.\nOUT-03	Allocation projection only: binds reserved quantity to eligible stock/location; OnHand unchanged.\nOUT-05	Physical pick execution; warehouse OnHand remains until shipment dispatch.\nOUT-06	Pack/consolidate inventory into HU/carton; no warehouse-level OnHand deduction.\nOUT-08	Shipment dispatch is the outbound physical deduction boundary; OnHand decreases exactly once.\nOUT-10	Tracking/POD is ledger-neutral; failed-delivery return requires explicit return/receive posting.\nINV-02	Immutable signed ledger is transactional truth for inventory movements.\nINV-03	Read/projection layer rebuilt from immutable ledger; never owns transactional truth.\nINV-04	Read/projection calculation over eligible status, locks, reservations and allocations.\nINV-05	Status transition controls eligibility; any quantity movement remains ledger-driven.\nINV-08	Same-warehouse location transfer; source decreases, destination increases, warehouse total conserved.\nINV-09	Reversal/corrective transaction; original posted ledger row remains immutable.\nINV-11	Integrity/reconciliation reads ledger and projection; rebuild does not rewrite historical ledger.\nTR-02	Read model for in-transit quantity and source/destination/reference dimensions.\nTR-03	Transfer dispatch moves quantity source → transit exactly once.\nTR-04	Transfer receive moves quantity transit → destination exactly once.\nTR-05	Internal replenish movement between reserve/pick locations; warehouse total conserved.\nCT-07	Adjustment posting boundary after review/approval; creates explicit signed ledger delta.\nQR-02	Status transition to/from QC_HOLD/QUARANTINE controls eligibility.\nQR-03	Damage finding records status/disposition; stock effect occurs only through approved canonical posting.\nQR-04	Customer return adds stock only at Return Receipt POST after inspection/disposition.\nQR-05	Recall blocks eligible stock by status/policy; does not silently rewrite ledger.\nQR-06	Scrap posting reduces inventory through approved explicit ledger transaction.\nHU-01	HU containment/location state changes must preserve underlying inventory truth.\nDY-04	Cross-dock may bypass storage, but receipt/dispatch posting boundaries remain canonical.\nRP-06	Read-only reconciliation/reporting over ledger-derived vs operational balances.\nAX-09	Kitting/de-kitting uses explicit transformation posting; components/output remain traceable.\nRP-09	Read-only analytical KPI derived from governed reporting semantics; no transactional inventory mutation.\nRP-10	Analytical export is read-only and must preserve lineage back to OLTP/ledger truth.\nAD-12	Presentation-only localization; canonical codes/quantities remain unchanged.\nAD-13	Legal hold preserves evidence/retention and must not rewrite inventory or audit history.\nAD-14	Privacy handling may redact/export allowed personal data but must not corrupt business/audit/legal truth.\nOP-08	Telemetry observes workflow friction/latency only; it is never a business inventory KPI source of truth.\nOP-09	Offboarding is ledger-neutral until canonical inventory/transfer workflows bring balances to zero before closure.\nAX-11	Cartonization is a recommendation/packing optimization; it cannot post inventory or mark shipment dispatched.\nAX-12	Load planning is operational optimization; loading/dispatch inventory boundaries remain canonical.\nAX-13	Disposition execution uses explicit return/status/reclassification/scrap postings; no silent balance rewrite.\nAX-14	Safety policy gates task eligibility/execution; it does not directly mutate inventory quantity.\nAX-15	Automation/device jobs never own inventory truth; resulting WMS commands use canonical posting contracts.\nAX-16	RFID/IoT capture is observational until validated WMS commands apply canonical state/inventory changes.\nAX-17	Voice/light assistance guides task execution but does not bypass canonical command/idempotency rules.\nAX-18	Hazmat policy constrains storage/eligibility; any movement/status change remains canonical and auditable.\nAX-19	Temperature excursions create evidence/hold/disposition; quantity changes require explicit canonical posting.\nAX-20	Dual-UOM quantities are persisted with canonical conversion/tolerance and immutable ledger traceability.\nAX-21	Owner changes are explicit inventory reclassification transactions; physical quantity is conserved.\nAX-22	Rating/billing consumes operational evidence as read models and does not own inventory truth.\nAX-23	ATP/CTP/promise calculation is read-only planning output; reservation requires an explicit canonical command.\nAX-24	Balancing produces transfer proposals only; inventory moves through canonical Transfer Dispatch/Receive.\nAX-25	Inter-warehouse replenishment planning is recommendation-only; execution uses canonical transfer/replenishment commands.\nAX-26	Routing selects fulfillment nodes but does not mutate stock; downstream reservation/allocation remains authoritative.\nAX-27	Forecast/demand signals are planning inputs only and never inventory truth.\nAX-28	Safety-stock/reorder policies affect planning thresholds and projections, not physical quantity directly.\nAX-29	ABC/XYZ classification is analytical metadata and does not mutate inventory.\nAX-30	Optimization policy generates recommendations; accepted execution still uses canonical business commands.\nAX-31	Simulation runs on immutable snapshots and cannot mutate production master, documents or ledger.\nAX-32	Forecast accuracy/bias is analytical measurement only.\nAX-33	Replenishment exceptions are workflow/read-model state; resolution routes through canonical replenish/transfer actions.\nAX-34	Procurement suggestion does not create inventory or PO truth until approved integration to ERP/Procurement.\nAX-35	Risk score is explainable/read-only decision support and cannot create hidden business mutation.\nAX-36	Lead-time intelligence is analytical and does not mutate PO/receipt/inventory truth.\nAX-37	Expedite/defer is a recommendation; ERP/Procurement remains commercial supply truth.\nAX-38	Demand anomaly detection is advisory and must not auto-mutate replenishment/inventory without governed policy.\nAX-39	Fairness policy produces deterministic allocation proposals; actual allocation uses canonical atomic allocation command.\nAX-40	Service-level segmentation is governed policy metadata and does not directly mutate inventory.\nAX-41	What-if scenarios are isolated from production transaction/master/ledger data.\nAX-42	Decision policy registry governs/version-controls recommendations; execution remains human/contract gated where required.\nMD-09	SLA contract is policy/semantic metadata; it does not mutate documents or inventory directly.\nWH-07	Calendar exception controls execution eligibility/time semantics and has no direct inventory quantity effect.\nIG-09	WMS exports quantity/movement/valuation events but does not calculate accounting cost/tax/multi-currency truth.\nIG-10	Reconciliation dashboard is read/control plane; retries must remain idempotent and cannot invent business mutations.\nOP-10	Posted business errors require reversal + corrected transaction; controlled repair must never rewrite immutable ledger history.\nOP-11	Support tools call Application Layer contracts and preserve permission/audit/inventory safeguards; no routine direct DB mutation.\nOV-08	Activity feed is a read model over governed events/audit/notifications and cannot become transactional truth.\nMO-11	Product lookup is read-only exact search within security scope.\nMO-12	Exception handling changes exception/task workflow only; any inventory effect must route through the owning canonical command.`;

const inventoryEffects: Record<string, string> = Object.fromEntries(
  inventoryEffectCatalog.split('\n').map((row) => {
    const separator = row.indexOf('\t');
    if (separator <= 0) throw new Error('Invalid inventory-effect catalog row.');
    return [row.slice(0, separator), row.slice(separator + 1)] as const;
  }),
);

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

const platformStandardCatalog = `251	Data Platform	Platform guardrail	\n252	Engineering	Platform guardrail	\n253	Engineering	Platform guardrail	\n254	UX / Product	Capability-backed	OP-08\n255	Engineering	Platform guardrail	\n256	UX / Product	Capability-backed	AD-12\n257	Security / Compliance	Capability-backed	AD-13\n258	Security / Compliance	Capability-backed	AD-14\n259	Engineering	Platform guardrail	\n260	Security / Compliance	Platform guardrail	\n261	Security / Compliance	Platform guardrail	\n262	Engineering	Platform guardrail	\n263	Engineering	Platform guardrail	\n264	Operations / Governance	Runbook-backed	OP-06\n265	Operations / Governance	Runbook-backed	OP-09\n266	UX / Product	Platform guardrail	\n267	UX / Product	Platform guardrail	\n268	UX / Product	Platform guardrail	\n269	UX / Product	Platform guardrail	\n270	UX / Product	Platform guardrail	\n271	UX / Product	Capability-backed	AD-07\n272	UX / Product	Capability-backed	MO-10,OP-05\n273	UX / Product	Platform guardrail	\n274	Operations / Governance	Platform guardrail	\n275	Operations / Governance	Platform guardrail	\n276	UX / Product	Capability-backed	AD-05,AD-10\n277	Operations / Governance	Runbook-backed	OP-06\n278	Operations / Governance	Platform guardrail	\n279	Security / Compliance	Capability-backed	AD-08\n280	Security / Compliance	Capability-backed	RP-08,AD-13\n281	Operations / Governance	Runbook-backed	OP-07\n282	UX / Product	Platform guardrail	INV-08,OUT-05,OUT-06,TR-01`;

export const platformStandards: PlatformStandard[] = platformStandardCatalog.split('\n').map((row) => {
  const [specValue, category, representation, mappedValue = ''] = row.split('\t');
  const spec = Number(specValue);
  const title = canonicalSpecTitles[spec];
  if (!Number.isInteger(spec) || !title || !category || !representation) {
    throw new Error('Invalid platform-standard catalog row.');
  }

  const mappedCapabilityIds = mappedValue ? mappedValue.split(',') : undefined;
  return {
    spec,
    title,
    category: category as PlatformStandard['category'],
    representation: representation as PlatformStandardRepresentation,
    ...(mappedCapabilityIds ? { mappedCapabilityIds } : {}),
  };
});

export const evidenceStatusLabels: Record<EvidenceStatus, string> = {
  covered: 'Covered',
  partial: 'Partial',
  'spec-only': 'Spec only',
  missing: 'Missing',
};
