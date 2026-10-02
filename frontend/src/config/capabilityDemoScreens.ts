import type { BlueprintCapability } from './erpWmsBlueprint';
import capabilityDemoCatalog from './capabilityDemoCatalog.json';

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


const getImplementedCapabilityDemo = (
  capability: BlueprintCapability,
  moduleName: string,
): CapabilityDemoDefinition => {
  const commonFields: CapabilityDemoField[] = [
    { label: 'Capability', value: capability.id },
    { label: 'Module', value: moduleName },
    { label: 'Production maturity', value: capability.status === 'live' ? 'LIVE' : 'FOUNDATION' },
    { label: 'Spec', value: capability.spec },
  ];

  const definitions: Record<string, CapabilityDemoDefinition> = {
    'OV-01': {
      title: 'Operational Dashboard',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Overview', 'Drill-down', 'Exception focus', 'Open work center'],
      fields: [...commonFields, { label: 'Open tasks', value: '47' }, { label: 'Critical exceptions', value: '3' }],
      commands: ['REFRESH_KPI', 'OPEN_EXCEPTION', 'OPEN_WORK_CENTER'],
      exceptionTitle: 'DASHBOARD_DATA_STALE',
      exceptionDetail: 'KPI snapshot cũ hơn operational read model; cần hiển thị freshness thay vì giả định realtime.',
    },
    'OV-03': {
      title: 'Approval Center',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['PENDING', 'REVIEWING', 'APPROVED', 'EXECUTED'],
      fields: [...commonFields, { label: 'Request', value: 'APR-2026-0442' }, { label: 'Type', value: 'Inventory Adjustment' }],
      commands: ['OPEN_REQUEST', 'APPROVE', 'REJECT'],
      exceptionTitle: 'SOD_APPROVER_CONFLICT',
      exceptionDetail: 'Approver không được trùng creator/counter khi segregation-of-duties bật.',
    },
    'MD-01': {
      title: 'Product / SKU Master',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['List', 'Detail', 'Validate', 'Active'],
      fields: [...commonFields, { label: 'SKU', value: 'SKU-1001' }, { label: 'Base UOM', value: 'Cái' }],
      commands: ['CREATE_PRODUCT', 'UPDATE_PRODUCT', 'DEACTIVATE_PRODUCT'],
      exceptionTitle: 'SKU_CODE_DUPLICATE',
      exceptionDetail: 'SKU code phải unique case-insensitively và identity ổn định.',
    },
    'MD-02': {
      title: 'Product Category Manager',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['List', 'Create/Rename', 'Assign product', 'Active'],
      fields: [...commonFields, { label: 'Category', value: 'CAT-FOOD' }, { label: 'Products', value: '128' }],
      commands: ['CREATE_CATEGORY', 'RENAME_CATEGORY', 'ASSIGN_PRODUCT'],
      exceptionTitle: 'CATEGORY_CODE_IMMUTABLE',
      exceptionDetail: 'Category code đã tạo không được đổi để giữ reference ổn định.',
    },
    'MD-03': {
      title: 'Barcode Registry',
      subtitle: capability.goal,
      mode: 'scan',
      stages: ['Scan', 'Exact match', 'Assign', 'Verify'],
      fields: [...commonFields, { label: 'Barcode', value: '8938500123456' }, { label: 'Product', value: 'SKU-1001' }],
      commands: ['LOOKUP_BARCODE', 'ADD_BARCODE', 'REMOVE_BARCODE'],
      exceptionTitle: 'BARCODE_DUPLICATE',
      exceptionDetail: 'Một barcode không được map đồng thời tới nhiều SKU.',
    },
    'MD-04': {
      title: 'UOM & Conversion',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['UOM', 'Conversion', 'Preview', 'Active'],
      fields: [...commonFields, { label: 'Operation UOM', value: 'Thùng' }, { label: 'Base UOM', value: 'Cái' }, { label: 'Factor', value: '12' }],
      commands: ['CREATE_UOM', 'SET_CONVERSION', 'PREVIEW_CONVERSION'],
      exceptionTitle: 'UOM_CONVERSION_INVALID',
      exceptionDetail: 'Conversion phải dương, nhất quán và không tạo cycle mơ hồ.',
      quantity: { label: 'Operation quantity', operationUom: 'Thùng', baseUom: 'Cái', factor: 12, initial: 5 },
    },
    'MD-05': {
      title: 'Business Partner Master',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['List', 'Detail', 'Roles', 'Active'],
      fields: [...commonFields, { label: 'Partner', value: 'SUP-0008' }, { label: 'Roles', value: 'Supplier + Customer' }],
      commands: ['CREATE_PARTNER', 'UPDATE_PARTNER', 'SET_ROLES'],
      exceptionTitle: 'PARTNER_CODE_IMMUTABLE',
      exceptionDetail: 'Partner code đã tạo phải immutable; role/active state thay đổi có validation.',
    },
    'WH-01': {
      title: 'Warehouse Master',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['List', 'Detail', 'Scope', 'Operational state'],
      fields: [...commonFields, { label: 'Warehouse', value: 'WH-HCM-01' }, { label: 'Timezone', value: 'Asia/Ho_Chi_Minh' }],
      commands: ['CREATE_WAREHOUSE', 'UPDATE_WAREHOUSE', 'CHANGE_OPERATIONAL_STATE'],
      exceptionTitle: 'WAREHOUSE_SCOPE_CONFLICT',
      exceptionDetail: 'Warehouse identity/scope không được thay đổi làm phá membership hoặc document history.',
    },
    'IN-03': {
      title: 'Goods Receipt Work Center',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['DRAFT', 'RECEIVING', 'QC_PENDING', 'READY_TO_POST', 'POSTED'],
      fields: [...commonFields, { label: 'Receipt', value: 'GR-2026-1045' }, { label: 'Warehouse', value: 'WH-HCM-01' }],
      commands: ['CREATE_RECEIPT', 'OPEN_RECEIVING', 'OPEN_POSTING'],
      exceptionTitle: 'RECEIPT_VERSION_CONFLICT',
      exceptionDetail: 'Receipt state/version thay đổi giữa lúc user đang thao tác.',
    },
    'IN-04': {
      title: 'Receiving Workbench',
      subtitle: capability.goal,
      mode: 'scan',
      stages: ['Scan product', 'Capture tracking', 'Enter quantity', 'Save line'],
      fields: [...commonFields, { label: 'Receipt', value: 'GR-2026-1045' }, { label: 'SKU', value: 'SKU-1001' }],
      commands: ['SCAN_PRODUCT', 'CAPTURE_LOT_SERIAL', 'SAVE_RECEIVING_LINE'],
      exceptionTitle: 'RECEIVING_LINE_INVALID',
      exceptionDetail: 'Quantity/UOM/lot/serial/expiry không hợp lệ hoặc vượt tolerance.',
      quantity: { label: 'Receive quantity', operationUom: 'Thùng', baseUom: 'Cái', factor: 12, initial: 8 },
    },
    'IN-07': {
      title: 'Receipt Posting',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['READY_TO_POST', 'VALIDATING', 'POSTING', 'POSTED'],
      fields: [...commonFields, { label: 'Receipt', value: 'GR-2026-1045' }, { label: 'Idempotency key', value: 'post-GR-1045-v3' }],
      commands: ['VALIDATE_POST', 'POST_RECEIPT', 'OPEN_LEDGER_ENTRY'],
      exceptionTitle: 'RECEIPT_POST_CONFLICT',
      exceptionDetail: 'Posting phải fail closed khi state/version/inventory precondition thay đổi.',
      quantity: { label: 'Posted quantity', operationUom: 'Cái', baseUom: 'Cái', factor: 1, initial: 96 },
    },
    'IN-08': {
      title: 'Putaway Task Execution',
      subtitle: capability.goal,
      mode: 'scan',
      stages: ['ASSIGNED', 'IN_PROGRESS', 'MOVED', 'COMPLETED'],
      fields: [...commonFields, { label: 'Task', value: 'PUT-2026-3321' }, { label: 'Destination', value: 'A01-R02-L03-B04' }],
      commands: ['START_TASK', 'CONFIRM_MOVE', 'COMPLETE_TASK'],
      exceptionTitle: 'PUTAWAY_TASK_BLOCKED',
      exceptionDetail: 'Destination/status/capacity thay đổi làm task cần exception/resume path.',
    },
    'OUT-01': {
      title: 'Sales / Export Order',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['DRAFT', 'RELEASED', 'FULFILLING', 'COMPLETED'],
      fields: [...commonFields, { label: 'Order', value: 'SO-2026-5108' }, { label: 'Customer', value: 'CUS-0042' }],
      commands: ['CREATE_ORDER', 'RELEASE_ORDER', 'CANCEL_ORDER'],
      exceptionTitle: 'ORDER_RELEASE_BLOCKED',
      exceptionDetail: 'Order không đủ điều kiện release do validation, permission hoặc state.',
    },
    'OUT-02': {
      title: 'Reservation Workbench',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Demand', 'Check availability', 'Reserved', 'Consumed/Released'],
      fields: [...commonFields, { label: 'Order', value: 'SO-2026-5108' }, { label: 'Reserved', value: '120 Cái' }],
      commands: ['CREATE_RESERVATION', 'RELEASE_RESERVATION', 'REALLOCATE_DEMAND'],
      exceptionTitle: 'RESERVATION_INSUFFICIENT_AVAILABLE',
      exceptionDetail: 'Available quantity không đủ sau status/lock/existing reservation.',
    },
    'INV-01': {
      title: 'Inventory Browser',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Search', 'Balance', 'Status split', 'Drill-down'],
      fields: [...commonFields, { label: 'SKU', value: 'SKU-1001' }, { label: 'On Hand', value: '1,350 Cái' }],
      commands: ['SEARCH_INVENTORY', 'OPEN_STATUS', 'OPEN_LEDGER'],
      exceptionTitle: 'INVENTORY_SCOPE_FORBIDDEN',
      exceptionDetail: 'Inventory ngoài warehouse/security scope không được trả về.',
    },
    'INV-02': {
      title: 'Immutable Inventory Ledger',
      subtitle: capability.goal,
      mode: 'trace',
      stages: ['Filter', 'Entry detail', 'Source correlation', 'Reversal link'],
      fields: [...commonFields, { label: 'Entry', value: 'LED-2026-882177' }, { label: 'Delta', value: '+96 Cái' }],
      commands: ['FILTER_LEDGER', 'OPEN_SOURCE', 'OPEN_REVERSAL'],
      exceptionTitle: 'LEDGER_MUTATION_FORBIDDEN',
      exceptionDetail: 'Posted ledger entry không được edit/delete; sửa sai bằng reversal/corrective transaction.',
    },
    'INV-03': {
      title: 'Inventory Balance Projection',
      subtitle: capability.goal,
      mode: 'trace',
      stages: ['Ledger input', 'Projection', 'Compare', 'Rebuild'],
      fields: [...commonFields, { label: 'Projection key', value: 'WH-HCM-01/SKU-1001/A01-R02-L03-B04' }, { label: 'Balance', value: '420 Cái' }],
      commands: ['REBUILD_PROJECTION', 'COMPARE_LEDGER', 'VERIFY_BALANCE'],
      exceptionTitle: 'PROJECTION_MISMATCH',
      exceptionDetail: 'Projection không khớp immutable ledger và phải rebuild/reconcile.',
    },
    'INV-04': {
      title: 'Inventory Availability Engine',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['On Hand', 'Subtract holds', 'Subtract reservation/allocation', 'Eligible'],
      fields: [...commonFields, { label: 'On Hand', value: '1,350 Cái' }, { label: 'Available', value: '1,120 Cái' }],
      commands: ['CALCULATE_AVAILABLE', 'EXPLAIN_ELIGIBILITY', 'OPEN_BLOCKERS'],
      exceptionTitle: 'AVAILABILITY_INCONSISTENT',
      exceptionDetail: 'Status/lock/reservation/allocation snapshot không nhất quán.',
    },
    'TR-01': {
      title: 'Warehouse Transfer',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['DRAFT', 'APPROVED', 'DISPATCHED', 'IN_TRANSIT', 'RECEIVED', 'CLOSED'],
      fields: [...commonFields, { label: 'Transfer', value: 'TRF-2026-0018' }, { label: 'Route', value: 'WH-HCM-01 → WH-DN-01' }],
      commands: ['CREATE_TRANSFER', 'APPROVE_TRANSFER', 'OPEN_DISPATCH'],
      exceptionTitle: 'TRANSFER_STATE_CONFLICT',
      exceptionDetail: 'Transfer state/version thay đổi hoặc source/destination scope không hợp lệ.',
    },
    'TR-03': {
      title: 'Transfer Dispatch',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['APPROVED', 'VALIDATING', 'DISPATCHED', 'IN_TRANSIT'],
      fields: [...commonFields, { label: 'Transfer', value: 'TRF-2026-0018' }, { label: 'Source', value: 'WH-HCM-01' }],
      commands: ['VALIDATE_DISPATCH', 'DISPATCH_TRANSFER', 'OPEN_TRANSIT'],
      exceptionTitle: 'TRANSFER_DISPATCH_CONFLICT',
      exceptionDetail: 'Source stock/version thay đổi; dispatch không được double-deduct.',
    },
    'TR-04': {
      title: 'Transfer Receive',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['IN_TRANSIT', 'ARRIVED', 'RECEIVING', 'RECEIVED'],
      fields: [...commonFields, { label: 'Transfer', value: 'TRF-2026-0018' }, { label: 'Destination', value: 'WH-DN-01' }],
      commands: ['MARK_ARRIVED', 'RECEIVE_TRANSFER', 'RECONCILE_VARIANCE'],
      exceptionTitle: 'TRANSFER_RECEIVE_VARIANCE',
      exceptionDetail: 'Received quantity khác transit quantity và cần reconciliation rõ ràng.',
    },
    'CT-01': {
      title: 'Full Count',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['PLANNED', 'RELEASED', 'COUNTING', 'REVIEW', 'COMPLETED'],
      fields: [...commonFields, { label: 'Count', value: 'CNT-2026-0098' }, { label: 'Scope', value: 'WH-HCM-01' }],
      commands: ['PLAN_COUNT', 'RELEASE_COUNT', 'COMPLETE_COUNT'],
      exceptionTitle: 'COUNT_NOT_RECONCILED',
      exceptionDetail: 'Count còn variance/recount/adjustment chưa resolve nên không thể complete.',
    },
    'CT-02': {
      title: 'Cycle Count',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Rule', 'Generate plan', 'Release', 'Measure accuracy'],
      fields: [...commonFields, { label: 'Rule', value: 'ABC-A weekly' }, { label: 'Locations selected', value: '48' }],
      commands: ['GENERATE_CYCLE_COUNT', 'RELEASE_COUNT', 'OPEN_ACCURACY'],
      exceptionTitle: 'CYCLE_COUNT_SCOPE_EMPTY',
      exceptionDetail: 'Rule không tạo ra eligible scope hoặc trùng count đang mở.',
    },
    'RP-01': {
      title: 'Inventory Snapshot Report',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Filter', 'Snapshot', 'Drill-down', 'Export'],
      fields: [...commonFields, { label: 'Warehouse', value: 'WH-HCM-01' }, { label: 'Rows', value: '2,441' }],
      commands: ['RUN_REPORT', 'DRILL_DOWN', 'EXPORT_REPORT'],
      exceptionTitle: 'REPORT_SNAPSHOT_STALE',
      exceptionDetail: 'Read model freshness phải hiển thị rõ khi không cùng thời điểm với transaction truth.',
    },
    'RP-02': {
      title: 'Xuất - Nhập - Tồn',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Period', 'Opening', 'In/Out', 'Closing', 'Export'],
      fields: [...commonFields, { label: 'Period', value: '01–02/10/2026' }, { label: 'Closing qty', value: '128,442 Cái' }],
      commands: ['RUN_IN_OUT_STOCK', 'OPEN_MOVEMENTS', 'EXPORT_REPORT'],
      exceptionTitle: 'REPORT_PERIOD_INVALID',
      exceptionDetail: 'Period/timezone không hợp lệ hoặc read model chưa đủ dữ liệu.',
    },
    'AD-01': {
      title: 'User Administration',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['User', 'Membership', 'Role', 'Enabled/Locked'],
      fields: [...commonFields, { label: 'User', value: 'U-HCM-OP-01' }, { label: 'Warehouse membership', value: 'WH-HCM-01' }],
      commands: ['CREATE_USER', 'SET_MEMBERSHIP', 'LOCK_USER'],
      exceptionTitle: 'USER_MEMBERSHIP_CONFLICT',
      exceptionDetail: 'Membership/role thay đổi phải giữ scope, SoD và session security.',
    },
    'AD-02': {
      title: 'Role & Permission Matrix',
      subtitle: capability.goal,
      mode: 'configuration',
      stages: ['Role', 'Permissions', 'Scope', 'Effective'],
      fields: [...commonFields, { label: 'Role', value: 'WAREHOUSE_SUPERVISOR' }, { label: 'Permissions', value: '38 codes' }],
      commands: ['CREATE_ROLE', 'ASSIGN_PERMISSION', 'REMOVE_PERMISSION'],
      exceptionTitle: 'PERMISSION_POLICY_CONFLICT',
      exceptionDetail: 'Permission thay đổi không được tạo privilege escalation ngoài governance.',
    },
    'AD-03': {
      title: 'Warehouse Scope Enforcement',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Identity', 'Membership', 'Requested warehouse', 'ALLOW/DENY'],
      fields: [...commonFields, { label: 'User', value: 'U-DN-MGR' }, { label: 'Allowed', value: 'WH-DN-01' }],
      commands: ['CHECK_SCOPE', 'SIMULATE_CROSS_WAREHOUSE', 'OPEN_MEMBERSHIP'],
      exceptionTitle: 'WAREHOUSE_SCOPE_DENIED',
      exceptionDetail: 'Biết record ID không được phép bypass warehouse membership.',
    },
    'AD-04': {
      title: 'Approval Workflow',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['REQUESTED', 'PENDING_APPROVAL', 'APPROVED', 'EXECUTED'],
      fields: [...commonFields, { label: 'Workflow', value: 'Inventory Adjustment > threshold' }, { label: 'SLA', value: '30 phút' }],
      commands: ['SUBMIT_APPROVAL', 'APPROVE', 'ESCALATE'],
      exceptionTitle: 'APPROVAL_SOD_CONFLICT',
      exceptionDetail: 'Requester không được tự approve khi workflow yêu cầu segregation-of-duties.',
    },
    'AD-06': {
      title: 'Audit Trail Explorer',
      subtitle: capability.goal,
      mode: 'trace',
      stages: ['Search', 'Event detail', 'Before/After', 'Correlation'],
      fields: [...commonFields, { label: 'Actor', value: 'U-HCM-SUP-01' }, { label: 'Entity', value: 'GR-2026-1045' }],
      commands: ['SEARCH_AUDIT', 'OPEN_EVENT', 'TRACE_CORRELATION'],
      exceptionTitle: 'AUDIT_EVENT_REDACTED',
      exceptionDetail: 'Sensitive fields phải redact theo permission/classification nhưng event không được mất.',
    },
    'IG-04': {
      title: 'Transactional Outbox',
      subtitle: capability.goal,
      mode: 'trace',
      stages: ['Business transaction', 'Outbox pending', 'Published', 'Acknowledged'],
      fields: [...commonFields, { label: 'Event', value: 'GoodsReceiptPosted' }, { label: 'Outbox ID', value: 'OBX-2026-88421' }],
      commands: ['PUBLISH_PENDING', 'RETRY_EVENT', 'OPEN_CORRELATION'],
      exceptionTitle: 'OUTBOX_DELIVERY_FAILED',
      exceptionDetail: 'Business commit giữ nguyên; event delivery retry idempotently, không rollback inventory tự động.',
    },
    'OP-01': {
      title: 'Observability & Monitoring',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Metrics', 'Logs', 'Trace', 'Alert'],
      fields: [...commonFields, { label: 'API p95', value: '240 ms' }, { label: 'Error rate', value: '0.18%' }],
      commands: ['OPEN_METRIC', 'OPEN_TRACE', 'ACK_ALERT'],
      exceptionTitle: 'OBSERVABILITY_SIGNAL_GAP',
      exceptionDetail: 'Thiếu metrics/logs/traces phải được coi là coverage gap, không phải hệ thống khỏe.',
    },
    'OP-03': {
      title: 'Backup / Restore Drill',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['Backup selected', 'Restore isolated', 'Integrity check', 'Drill passed'],
      fields: [...commonFields, { label: 'Backup', value: '2026-10-02T01:00Z' }, { label: 'Target', value: 'Isolated restore environment' }],
      commands: ['START_RESTORE_DRILL', 'VERIFY_INTEGRITY', 'CLOSE_DRILL'],
      exceptionTitle: 'RESTORE_DRILL_FAILED',
      exceptionDetail: 'Backup không được coi là usable cho tới khi restore và integrity verification PASS.',
    },
  };

  const exact = definitions[capability.id];
  if (exact) return exact;

  return {
    title: capability.name + ' • Implemented Capability Preview',
    subtitle: capability.goal,
    mode: capability.surfaces.includes('Mobile') ? 'scan' : 'workbench',
    stages: ['Open', 'Validate', 'Execute', 'Verify'],
    fields: commonFields,
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
  if (specialized) return specialized;
  if (capability.status === 'optional') return getOptionalCapabilityDemo(capability, moduleName);
  if (capability.status === 'live' || capability.status === 'foundation') return getImplementedCapabilityDemo(capability, moduleName);

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
