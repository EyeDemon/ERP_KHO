import type { BlueprintCapability } from './erpWmsBlueprint';
import capabilityDemoCatalog from './capabilityDemoCatalog.json';
import implementedCapabilityDemoCatalog from './implementedCapabilityDemoCatalog.json';

export interface CapabilityDemoField {
  label: string;
  value: string;
  helper?: string;
}

export interface CapabilityDemoQuantity {
  label: string;
  operationUom: string;
  baseUom: string;
  factor: number;
  initial: number;
}

export interface CapabilityDemoDefinition {
  title: string;
  subtitle: string;
  mode: 'workbench' | 'wizard' | 'control' | 'scan' | 'trace' | 'configuration';
  stages: string[];
  fields: CapabilityDemoField[];
  commands: string[];
  exceptionTitle: string;
  exceptionDetail: string;
  quantity?: CapabilityDemoQuantity;
}

const coreCapabilityDemos = capabilityDemoCatalog as Record<string, CapabilityDemoDefinition>;
const plannedCapabilityDemos: Record<string, CapabilityDemoDefinition> = {};

const getOptionalCapabilityDemo = (
  capability: BlueprintCapability,
  moduleName: string,
): CapabilityDemoDefinition => {
  const commonFields: CapabilityDemoField[] = [
    { label: 'Capability', value: capability.id },
    { label: 'Module', value: moduleName },
    { label: 'Applicability', value: 'Optional / feature-enabled' },
    { label: 'Spec', value: capability.spec },
  ];

  if (capability.id === 'OUT-04') {
    return {
      title: 'Wave / Batch / Cluster Planning',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Order pool', 'Grouping', 'Release wave', 'Pick execution'],
      fields: [...commonFields, { label: 'Candidate orders', value: '84' }, { label: 'Suggested waves', value: '4' }],
      commands: ['BUILD_WAVE', 'REBALANCE_WAVE', 'RELEASE_WAVE'],
      exceptionTitle: 'WAVE_CAPACITY_CONFLICT',
      exceptionDetail: 'Wave vượt labor/equipment/location capacity hoặc chứa order không còn eligible.',
    };
  }

  if (capability.id === 'HU-04') {
    return {
      title: 'SSCC / Logistics Label Lab',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Generate SSCC', 'Preview label', 'Print', 'Verify scan'],
      fields: [...commonFields, { label: 'SSCC', value: '089385001234567890' }, { label: 'HU', value: 'PLT-2026-00418' }],
      commands: ['GENERATE_SSCC', 'PRINT_LOGISTICS_LABEL', 'VERIFY_SSCC'],
      exceptionTitle: 'SSCC_DUPLICATE',
      exceptionDetail: 'Logistics identifier phải unique và không được tái sử dụng cho HU khác.',
    };
  }

  if (capability.id === 'DY-03') {
    return {
      title: 'Yard Management Board',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Gate arrival', 'Yard position', 'Dock queue', 'Exit'],
      fields: [...commonFields, { label: 'Vehicles in yard', value: '12' }, { label: 'Waiting > 45m', value: '3' }],
      commands: ['ASSIGN_YARD_POSITION', 'MOVE_TO_DOCK_QUEUE', 'CHECK_OUT_VEHICLE'],
      exceptionTitle: 'YARD_POSITION_OCCUPIED',
      exceptionDetail: 'Trailer/vehicle không thể được gán vào yard position đang bị giữ.',
    };
  }

  if (capability.id === 'DY-04') {
    return {
      title: 'Cross-Docking Match Board',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Inbound eligible', 'Match outbound demand', 'Stage cross-dock', 'Load outbound'],
      fields: [...commonFields, { label: 'Inbound HU', value: 'PLT-IN-0441' }, { label: 'Matched shipment', value: 'SHP-2026-5108' }],
      commands: ['MATCH_CROSS_DOCK', 'STAGE_CROSS_DOCK', 'RELEASE_TO_LOAD'],
      exceptionTitle: 'CROSS_DOCK_MATCH_INVALID',
      exceptionDetail: 'Product/lot/status/quantity hoặc outbound demand không còn phù hợp để bypass storage.',
    };
  }

  if (capability.id === 'IG-06') {
    return {
      title: 'Carrier / E-commerce / TMS Integration Lab',
      subtitle: capability.goal,
      mode: 'trace',
      stages: ['Request', 'External accepted', 'Tracking updates', 'Reconciled'],
      fields: [...commonFields, { label: 'Connector', value: 'TMS-CARRIER-V2' }, { label: 'Correlation', value: 'CORR-TMS-88102' }],
      commands: ['SEND_REQUEST', 'INGEST_UPDATE', 'RETRY_DELIVERY'],
      exceptionTitle: 'EXTERNAL_CONNECTOR_DEGRADED',
      exceptionDetail: 'External API down/rate-limited; retry phải idempotent và không duplicate business state.',
    };
  }

  const name = capability.name;
  const technology = /RFID|IoT|WCS|WES|Robotics|Voice|Pick-to-Light|Equipment/i.test(name);
  const regulated = /Hazard|Cold Chain|Temperature|Catch Weight|Safety|Ergonomics/i.test(name);
  const ownership = /3PL|Consignment|Vendor-Owned|Customer-Owned|Billing|Contract/i.test(name);
  const execution = /Kitting|Bundling|Assembly|VAS|Cartonization|Cubing|Load Planning|Reverse Logistics|Refurbishment|RTV/i.test(name);
  const decision = /Planning|Forecast|Optimization|Balancing|Promise|Routing|Safety Stock|ABC|XYZ|Risk|Lead-Time|Expedite|Demand Spike|Fairness|Service-Level|Scenario|Decision Policy|Procurement Suggestion|Replenishment Exception/i.test(name);

  if (technology) {
    return {
      title: name + ' • Technology Sandbox',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Device/adapter online', 'Capture signal', 'Validate event', 'Create WMS action', 'Observe health'],
      fields: [...commonFields, { label: 'Device / adapter', value: 'SIM-DEVICE-01' }, { label: 'Health', value: 'ONLINE' }],
      commands: ['CAPTURE_SIGNAL', 'VALIDATE_DEVICE_EVENT', 'CREATE_WMS_TASK'],
      exceptionTitle: 'DEVICE_OR_ADAPTER_UNAVAILABLE',
      exceptionDetail: 'Automation/device failure phải fail safe và giữ canonical WMS state ở application layer.',
    };
  }

  if (regulated) {
    return {
      title: name + ' • Compliance Sandbox',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['Capture condition', 'Policy check', 'Hold/restrict', 'Disposition', 'Evidence'],
      fields: [...commonFields, { label: 'Policy profile', value: 'SPECIAL-STORAGE-v2' }, { label: 'Evidence', value: 'Sensor / checklist / reason' }],
      commands: ['CHECK_SPECIAL_POLICY', 'PLACE_RESTRICTION', 'RELEASE_OR_DISPOSE'],
      exceptionTitle: 'SPECIAL_INVENTORY_POLICY_VIOLATION',
      exceptionDetail: 'Inventory không đạt điều kiện chuyên biệt phải bị hold/restrict và lưu evidence.',
    };
  }

  if (ownership) {
    return {
      title: name + ' • Ownership & Billing Sandbox',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Owner/contract', 'Operational event', 'Charge/ownership rule', 'Reconcile'],
      fields: [...commonFields, { label: 'Owner', value: 'OWNER-3PL-01' }, { label: 'Contract', value: 'CTR-2026-PLATINUM' }],
      commands: ['APPLY_OWNER_RULE', 'CALCULATE_CHARGE_EVENT', 'RECONCILE_OWNER_BALANCE'],
      exceptionTitle: 'OWNER_OR_CONTRACT_MISMATCH',
      exceptionDetail: 'Warehouse, owner và commercial contract là dimensions độc lập và phải reconcile rõ ràng.',
    };
  }

  if (execution) {
    return {
      title: name + ' • Advanced Execution Sandbox',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Plan work', 'Allocate inputs', 'Execute', 'Validate output', 'Complete'],
      fields: [...commonFields, { label: 'Work order', value: 'ADV-2026-0042' }, { label: 'Status', value: 'PLANNED' }],
      commands: ['CREATE_ADVANCED_TASK', 'START_EXECUTION', 'CONFIRM_OUTPUT'],
      exceptionTitle: 'ADVANCED_EXECUTION_MISMATCH',
      exceptionDetail: 'Input/output quantity, HU, owner hoặc status không reconcile với execution plan.',
    };
  }

  if (decision) {
    return {
      title: name + ' • Decision Support Sandbox',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Inputs', 'Analyze', 'Recommendation', 'Human review', 'Apply / export'],
      fields: [...commonFields, { label: 'Scenario', value: 'BASELINE-2026-W40' }, { label: 'Decision mode', value: 'Human-in-the-loop' }],
      commands: ['RUN_ANALYSIS', 'COMPARE_SCENARIO', 'APPROVE_RECOMMENDATION'],
      exceptionTitle: 'DECISION_INPUT_OR_POLICY_CONFLICT',
      exceptionDetail: 'Recommendation thiếu dữ liệu/policy hợp lệ phải được chặn hoặc yêu cầu human review.',
    };
  }

  return {
    title: name + ' • Optional Feature Sandbox',
    subtitle: capability.goal,
    mode: 'workbench',
    stages: ['Configure', 'Simulate', 'Review', 'Apply'],
    fields: commonFields,
    commands: ['CONFIGURE_OPTIONAL_FEATURE', 'RUN_SIMULATION', 'REVIEW_RESULT'],
    exceptionTitle: 'OPTIONAL_FEATURE_NOT_ENABLED',
    exceptionDetail: 'Capability chỉ hoạt động khi feature/applicability tương ứng được bật và cấu hình đủ.',
  };
};


const implementedCapabilityDemos = implementedCapabilityDemoCatalog as Record<string, CapabilityDemoDefinition>;

const getImplementedCapabilityDemo = (
  capability: BlueprintCapability,
): CapabilityDemoDefinition => {
  const exact = implementedCapabilityDemos[capability.id];
  if (exact) return exact;

  return {
    title: capability.name + ' • Implemented Capability Preview',
    subtitle: capability.goal,
    mode: capability.surfaces.includes('Mobile') ? 'scan' : 'workbench',
    stages: ['Open', 'Validate', 'Execute', 'Verify'],
    fields: [
      { label: 'Capability', value: capability.id },
      { label: 'Production maturity', value: capability.status === 'live' ? 'LIVE' : 'FOUNDATION' },
      { label: 'Spec', value: capability.spec },
    ],
    commands: ['OPEN_REAL_SCREEN', 'VALIDATE_STATE', 'VERIFY_RESULT'],
    exceptionTitle: 'IMPLEMENTED_FLOW_EXCEPTION',
    exceptionDetail: 'Mô phỏng recovery path cho capability production/foundation hiện có.',
  };
};

export const getCapabilityDemoDefinition = (
  capability: BlueprintCapability,
  moduleName: string,
  moduleFlow?: string[],
): CapabilityDemoDefinition => {
  const specialized = coreCapabilityDemos[capability.id] ?? plannedCapabilityDemos[capability.id];
  if (capability.status === 'live' || capability.status === 'foundation') {
    const specializedIsImplemented = specialized?.fields.some((field) => field.label === 'Production maturity') ?? false;
    return specializedIsImplemented ? specialized : getImplementedCapabilityDemo(capability);
  }
  if (specialized) return specialized;
  if (capability.status === 'optional') return getOptionalCapabilityDemo(capability, moduleName);

  const stages = moduleFlow && moduleFlow.length >= 2
    ? moduleFlow
    : ['Danh sách', 'Chi tiết', 'Validate', 'Thực thi', 'Hoàn tất'];

  return {
    title: capability.name + ' • Interactive Blueprint',
    subtitle: capability.goal,
    mode: capability.surfaces.includes('Mobile') ? 'scan' : 'workbench',
    stages,
    fields: [
      { label: 'Capability', value: capability.id },
      { label: 'Module', value: moduleName },
      { label: 'Surface', value: capability.surfaces.join(' / ') },
      { label: 'Spec', value: capability.spec },
      { label: 'Demo scope', value: 'Mock-only • không gọi API production' },
    ],
    commands: ['OPEN_DETAIL', 'VALIDATE', 'SIMULATE_ACTION'],
    exceptionTitle: 'MOCK_VALIDATION_EXCEPTION',
    exceptionDetail: 'Mô phỏng validation/permission/state conflict để kiểm tra recovery path của capability.',
  };
};

export const coreInteractiveDemoIds = [...Object.keys(coreCapabilityDemos), ...Object.keys(plannedCapabilityDemos)];
