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
    { label: 'Chức năng', value: capability.id },
    { label: 'Phân hệ', value: moduleName },
    { label: 'Phạm vi áp dụng', value: 'Tùy chọn / bật theo chức năng' },
    { label: 'Đặc tả', value: capability.spec },
  ];

  if (capability.id === 'OUT-04') {
    return {
      title: 'Lập kế hoạch đợt / lô / cụm',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Nhóm đơn hàng', 'Gom nhóm', 'Phát hành đợt', 'Thực hiện lấy hàng'],
      fields: [...commonFields, { label: 'Đơn hàng ứng viên', value: '84' }, { label: 'Đợt đề xuất', value: '4' }],
      commands: ['BUILD_WAVE', 'REBALANCE_WAVE', 'RELEASE_WAVE'],
      exceptionTitle: 'WAVE_CAPACITY_CONFLICT',
      exceptionDetail: 'Đợt vượt năng lực nhân lực/thiết bị/vị trí hoặc chứa đơn hàng không còn đủ điều kiện.',
    };
  }

  if (capability.id === 'HU-04') {
    return {
      title: 'Phòng thử nghiệm nhãn SSCC / logistics',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Tạo SSCC', 'Xem trước nhãn', 'In nhãn', 'Xác minh quét'],
      fields: [...commonFields, { label: 'SSCC', value: '089385001234567890' }, { label: 'HU', value: 'PLT-2026-00418' }],
      commands: ['GENERATE_SSCC', 'PRINT_LOGISTICS_LABEL', 'VERIFY_SSCC'],
      exceptionTitle: 'SSCC_DUPLICATE',
      exceptionDetail: 'Định danh logistics phải duy nhất và không được tái sử dụng cho HU khác.',
    };
  }

  if (capability.id === 'DY-03') {
    return {
      title: 'Bảng điều hành sân bãi',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Đến cổng', 'Vị trí sân bãi', 'Hàng đợi cửa kho', 'Rời khỏi kho'],
      fields: [...commonFields, { label: 'Phương tiện trong sân', value: '12' }, { label: 'Chờ > 45 phút', value: '3' }],
      commands: ['ASSIGN_YARD_POSITION', 'MOVE_TO_DOCK_QUEUE', 'CHECK_OUT_VEHICLE'],
      exceptionTitle: 'YARD_POSITION_OCCUPIED',
      exceptionDetail: 'Rơ-moóc/phương tiện không thể được gán vào vị trí sân bãi đang bị giữ.',
    };
  }

  if (capability.id === 'DY-04') {
    return {
      title: 'Bảng ghép nối chuyển thẳng',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Hàng nhập đủ điều kiện', 'Ghép nhu cầu xuất', 'Đưa vào khu chuyển thẳng', 'Xếp hàng xuất'],
      fields: [...commonFields, { label: 'HU hàng nhập', value: 'PLT-IN-0441' }, { label: 'Lô giao hàng đã ghép', value: 'SHP-2026-5108' }],
      commands: ['MATCH_CROSS_DOCK', 'STAGE_CROSS_DOCK', 'RELEASE_TO_LOAD'],
      exceptionTitle: 'CROSS_DOCK_MATCH_INVALID',
      exceptionDetail: 'Sản phẩm/lô/trạng thái/số lượng hoặc nhu cầu xuất không còn phù hợp để bỏ qua lưu kho.',
    };
  }

  if (capability.id === 'IG-06') {
    return {
      title: 'Phòng thử nghiệm tích hợp Đơn vị vận chuyển / Thương mại điện tử / TMS',
      subtitle: capability.goal,
      mode: 'trace',
      stages: ['Gửi yêu cầu', 'Hệ thống ngoài chấp nhận', 'Cập nhật theo dõi', 'Đã đối chiếu'],
      fields: [...commonFields, { label: 'Bộ kết nối', value: 'TMS-CARRIER-V2' }, { label: 'Mã tương quan', value: 'CORR-TMS-88102' }],
      commands: ['SEND_REQUEST', 'INGEST_UPDATE', 'RETRY_DELIVERY'],
      exceptionTitle: 'EXTERNAL_CONNECTOR_DEGRADED',
      exceptionDetail: 'API bên ngoài không khả dụng/bị giới hạn tốc độ; thử lại phải có tính idempotent và không tạo trùng trạng thái nghiệp vụ.',
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
      title: name + ' • Môi trường thử nghiệm công nghệ',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Thiết bị/bộ chuyển đổi trực tuyến', 'Ghi nhận tín hiệu', 'Xác minh sự kiện', 'Tạo hành động WMS', 'Theo dõi tình trạng'],
      fields: [...commonFields, { label: 'Thiết bị / bộ chuyển đổi', value: 'SIM-DEVICE-01' }, { label: 'Tình trạng', value: 'ONLINE' }],
      commands: ['CAPTURE_SIGNAL', 'VALIDATE_DEVICE_EVENT', 'CREATE_WMS_TASK'],
      exceptionTitle: 'DEVICE_OR_ADAPTER_UNAVAILABLE',
      exceptionDetail: 'Lỗi tự động hóa/thiết bị phải an toàn khi thất bại và giữ trạng thái WMS chuẩn ở lớp ứng dụng.',
    };
  }

  if (regulated) {
    return {
      title: name + ' • Môi trường thử nghiệm tuân thủ',
      subtitle: capability.goal,
      mode: 'wizard',
      stages: ['Ghi nhận điều kiện', 'Kiểm tra chính sách', 'Giữ/hạn chế', 'Quyết định xử lý', 'Bằng chứng'],
      fields: [...commonFields, { label: 'Hồ sơ chính sách', value: 'SPECIAL-STORAGE-v2' }, { label: 'Bằng chứng', value: 'Cảm biến / danh sách kiểm / lý do' }],
      commands: ['CHECK_SPECIAL_POLICY', 'PLACE_RESTRICTION', 'RELEASE_OR_DISPOSE'],
      exceptionTitle: 'SPECIAL_INVENTORY_POLICY_VIOLATION',
      exceptionDetail: 'Tồn kho không đạt điều kiện chuyên biệt phải bị giữ/hạn chế và lưu bằng chứng.',
    };
  }

  if (ownership) {
    return {
      title: name + ' • Môi trường thử nghiệm sở hữu & tính phí',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Chủ sở hữu/hợp đồng', 'Sự kiện vận hành', 'Quy tắc phí/sở hữu', 'Đối chiếu'],
      fields: [...commonFields, { label: 'Chủ sở hữu', value: 'OWNER-3PL-01' }, { label: 'Hợp đồng', value: 'CTR-2026-PLATINUM' }],
      commands: ['APPLY_OWNER_RULE', 'CALCULATE_CHARGE_EVENT', 'RECONCILE_OWNER_BALANCE'],
      exceptionTitle: 'OWNER_OR_CONTRACT_MISMATCH',
      exceptionDetail: 'Kho, chủ sở hữu và hợp đồng thương mại là các chiều độc lập và phải được đối chiếu rõ ràng.',
    };
  }

  if (execution) {
    return {
      title: name + ' • Môi trường thử nghiệm thực thi nâng cao',
      subtitle: capability.goal,
      mode: 'workbench',
      stages: ['Lập kế hoạch công việc', 'Phân bổ đầu vào', 'Thực thi', 'Xác minh đầu ra', 'Hoàn tất'],
      fields: [...commonFields, { label: 'Lệnh công việc', value: 'ADV-2026-0042' }, { label: 'Trạng thái', value: 'PLANNED' }],
      commands: ['CREATE_ADVANCED_TASK', 'START_EXECUTION', 'CONFIRM_OUTPUT'],
      exceptionTitle: 'ADVANCED_EXECUTION_MISMATCH',
      exceptionDetail: 'Số lượng đầu vào/đầu ra, HU, chủ sở hữu hoặc trạng thái không khớp kế hoạch thực thi.',
    };
  }

  if (decision) {
    return {
      title: name + ' • Môi trường thử nghiệm hỗ trợ quyết định',
      subtitle: capability.goal,
      mode: 'control',
      stages: ['Đầu vào', 'Phân tích', 'Khuyến nghị', 'Rà soát của người dùng', 'Áp dụng / xuất dữ liệu'],
      fields: [...commonFields, { label: 'Kịch bản', value: 'BASELINE-2026-W40' }, { label: 'Chế độ quyết định', value: 'Có người tham gia phê duyệt' }],
      commands: ['RUN_ANALYSIS', 'COMPARE_SCENARIO', 'APPROVE_RECOMMENDATION'],
      exceptionTitle: 'DECISION_INPUT_OR_POLICY_CONFLICT',
      exceptionDetail: 'Khuyến nghị thiếu dữ liệu/chính sách hợp lệ phải bị chặn hoặc yêu cầu người dùng rà soát.',
    };
  }

  return {
    title: name + ' • Môi trường thử nghiệm chức năng tùy chọn',
    subtitle: capability.goal,
    mode: 'workbench',
    stages: ['Cấu hình', 'Mô phỏng', 'Rà soát', 'Áp dụng'],
    fields: commonFields,
    commands: ['CONFIGURE_OPTIONAL_FEATURE', 'RUN_SIMULATION', 'REVIEW_RESULT'],
    exceptionTitle: 'OPTIONAL_FEATURE_NOT_ENABLED',
    exceptionDetail: 'Chức năng chỉ hoạt động khi tính năng/phạm vi áp dụng tương ứng được bật và cấu hình đầy đủ.',
  };
};


const implementedCapabilityDemos = implementedCapabilityDemoCatalog as Record<string, CapabilityDemoDefinition>;

const getImplementedCapabilityDemo = (
  capability: BlueprintCapability,
): CapabilityDemoDefinition => {
  const exact = implementedCapabilityDemos[capability.id];
  if (exact) return exact;

  return {
    title: capability.name + ' • Xem trước chức năng đã triển khai',
    subtitle: capability.goal,
    mode: capability.surfaces.includes('Mobile') ? 'scan' : 'workbench',
    stages: ['Mở', 'Xác minh', 'Thực thi', 'Kiểm tra kết quả'],
    fields: [
      { label: 'Chức năng', value: capability.id },
      { label: 'Mức triển khai hệ thống thật', value: capability.status === 'live' ? 'HOÀN THIỆN' : 'NỀN TẢNG' },
      { label: 'Đặc tả', value: capability.spec },
    ],
    commands: ['OPEN_REAL_SCREEN', 'VALIDATE_STATE', 'VERIFY_RESULT'],
    exceptionTitle: 'IMPLEMENTED_FLOW_EXCEPTION',
    exceptionDetail: 'Mô phỏng hướng phục hồi cho chức năng hệ thống thật/nền tảng hiện có.',
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
    : ['Danh sách', 'Chi tiết', 'Xác minh', 'Thực thi', 'Hoàn tất'];

  return {
    title: capability.name + ' • Bản thiết kế tương tác',
    subtitle: capability.goal,
    mode: capability.surfaces.includes('Mobile') ? 'scan' : 'workbench',
    stages,
    fields: [
      { label: 'Chức năng', value: capability.id },
      { label: 'Phân hệ', value: moduleName },
      { label: 'Kênh sử dụng', value: capability.surfaces.join(' / ') },
      { label: 'Đặc tả', value: capability.spec },
      { label: 'Phạm vi mô phỏng', value: 'Chỉ mô phỏng • không gọi API hệ thống thật' },
    ],
    commands: ['OPEN_DETAIL', 'VALIDATE', 'SIMULATE_ACTION'],
    exceptionTitle: 'MOCK_VALIDATION_EXCEPTION',
    exceptionDetail: 'Mô phỏng xung đột kiểm tra dữ liệu/quyền/trạng thái để kiểm tra hướng phục hồi của chức năng.',
  };
};

export const coreInteractiveDemoIds = [...Object.keys(coreCapabilityDemos), ...Object.keys(plannedCapabilityDemos)];
