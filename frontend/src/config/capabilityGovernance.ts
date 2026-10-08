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
  reviewStatus: 'Designed' | 'Review Required' | 'Traceability Closed';
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

const screenTraceability: Record<string, { screenReference: string; finding: string }> = {
  'INV-08': {
    screenReference: 'BẢN THIẾT KẾ • /system-blueprint/inventory-control/INV-08 • Bàn làm việc di chuyển vị trí nội bộ',
    finding: 'ĐẠT nội dung / Đã đóng truy vết — Ma trận màn hình 229 + Đặc tả 282 đã ánh xạ tới route bản thiết kế cụ thể • 2026-10-03',
  },
  'OUT-05': {
    screenReference: 'BẢN THIẾT KẾ • /system-blueprint/outbound/OUT-05 • Bàn làm việc lấy hàng / Luồng quét',
    finding: 'ĐẠT nội dung / Đã đóng truy vết — Ma trận màn hình 229 + Đặc tả 282 đã ánh xạ tới route bản thiết kế cụ thể • 2026-10-03',
  },
  'OUT-06': {
    screenReference: 'BẢN THIẾT KẾ • /system-blueprint/outbound/OUT-06 • Trạm đóng gói',
    finding: 'ĐẠT nội dung / Đã đóng truy vết — Ma trận màn hình 229 + Đặc tả 282 đã ánh xạ tới route bản thiết kế cụ thể • 2026-10-03',
  },
  'TR-01': {
    screenReference: 'BẢN THIẾT KẾ • /system-blueprint/transfer-replenishment/TR-01 • Tạo điều chuyển kho',
    finding: 'ĐẠT nội dung / Đã đóng truy vết — Ma trận màn hình 229 + Đặc tả 282 đã ánh xạ tới route bản thiết kế cụ thể • 2026-10-03',
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

const inventoryEffectCatalog = `IN-04\tGhi nhận tiếp nhận chưa tác động sổ cái; OnHand của kho không đổi trước Receipt POST.
IN-06\tQC/disposition chưa tác động sổ cái trước Receipt POST; quyết định nhóm tồn khi ghi sổ.
IN-07\tRanh giới ghi sổ tồn kho: số lượng chấp nhận/hư hỏng đi vào tồn kho chuẩn đúng một lần.
IN-08\tDi chuyển vị trí nội bộ sau Receipt POST; tổng số lượng toàn kho không đổi.
OUT-02\tChỉ là dự phóng giữ hàng: Available giảm; OnHand toàn kho không đổi.
OUT-03\tChỉ là dự phóng phân bổ: gắn lượng đã giữ vào tồn/vị trí đủ điều kiện; OnHand không đổi.
OUT-05\tThực hiện lấy hàng vật lý; OnHand toàn kho giữ nguyên cho tới khi xác nhận giao hàng.
OUT-06\tĐóng gói/hợp nhất tồn vào HU/thùng; không khấu trừ OnHand ở cấp kho.
OUT-08\tXác nhận giao hàng là ranh giới khấu trừ vật lý của xuất kho; OnHand giảm đúng một lần.
OUT-10\tTheo dõi/POD không tác động sổ cái; hàng giao thất bại quay về cần ghi nhận trả/nhận rõ ràng.
INV-02\tSổ cái có dấu và bất biến là nguồn sự thật giao dịch cho mọi biến động tồn kho.
INV-03\tLớp đọc/dự phóng được tái tạo từ sổ cái bất biến; không sở hữu sự thật giao dịch.
INV-04\tPhép tính đọc/dự phóng trên trạng thái đủ điều kiện, khóa, lượng giữ và phân bổ.
INV-05\tChuyển trạng thái kiểm soát điều kiện hợp lệ; mọi dịch chuyển số lượng vẫn do sổ cái chi phối.
INV-08\tDi chuyển vị trí trong cùng kho; nguồn giảm, đích tăng, tổng kho được bảo toàn.
INV-09\tGiao dịch đảo/hiệu chỉnh; dòng sổ cái gốc đã ghi vẫn bất biến.
INV-11\tToàn vẹn/đối chiếu đọc sổ cái và dự phóng; tái tạo không viết lại lịch sử sổ cái.
TR-02\tMô hình đọc cho lượng đang vận chuyển và các chiều nguồn/đích/tham chiếu.
TR-03\tXuất điều chuyển dịch chuyển số lượng nguồn → đang vận chuyển đúng một lần.
TR-04\tNhận điều chuyển dịch chuyển số lượng đang vận chuyển → đích đúng một lần.
TR-05\tBổ sung nội bộ giữa vị trí dự trữ/lấy hàng; tổng kho được bảo toàn.
CT-07\tRanh giới ghi sổ điều chỉnh sau rà soát/phê duyệt; tạo chênh lệch sổ cái có dấu rõ ràng.
QR-02\tChuyển trạng thái tới/từ QC_HOLD/QUARANTINE kiểm soát điều kiện sử dụng tồn.
QR-03\tPhát hiện hư hỏng ghi trạng thái/disposition; ảnh hưởng tồn chỉ xảy ra qua ghi sổ chuẩn đã phê duyệt.
QR-04\tHàng khách trả chỉ tăng tồn tại Return Receipt POST sau kiểm tra/disposition.
QR-05\tThu hồi chặn tồn đủ điều kiện theo trạng thái/chính sách; không âm thầm viết lại sổ cái.
QR-06\tGhi sổ tiêu hủy làm giảm tồn thông qua giao dịch sổ cái rõ ràng đã phê duyệt.
HU-01\tThay đổi trạng thái chứa/vị trí HU phải bảo toàn sự thật tồn kho bên dưới.
DY-04\tCross-dock có thể bỏ qua lưu trữ nhưng ranh giới ghi sổ nhận/xuất vẫn phải chuẩn.
RP-06\tĐối chiếu/báo cáo chỉ đọc giữa số dư từ sổ cái và số dư vận hành.
AX-09\tKitting/de-kitting dùng ghi sổ chuyển đổi rõ ràng; linh kiện/thành phẩm vẫn truy vết được.
RP-09\tKPI phân tích chỉ đọc từ ngữ nghĩa báo cáo được quản trị; không thay đổi giao dịch tồn kho.
RP-10\tXuất dữ liệu phân tích là chỉ đọc và phải giữ phả hệ về dữ liệu OLTP/sổ cái.
AD-12\tBản địa hóa chỉ ở lớp trình bày; mã/số lượng chuẩn không đổi.
AD-13\tGiữ pháp lý bảo toàn bằng chứng/lưu trữ và không được viết lại tồn kho hay lịch sử kiểm toán.
AD-14\tXử lý quyền riêng tư có thể che/xuất dữ liệu cá nhân được phép nhưng không làm sai sự thật nghiệp vụ/kiểm toán/pháp lý.
OP-08\tTelemetry chỉ quan sát ma sát/độ trễ quy trình; không bao giờ là nguồn KPI tồn kho nghiệp vụ.
OP-09\tOffboarding không tác động sổ cái cho tới khi các quy trình tồn kho/điều chuyển chuẩn đưa số dư về 0 trước khi đóng.
AX-11\tCartonization là khuyến nghị/tối ưu đóng gói; không được ghi sổ tồn hay đánh dấu giao hàng đã xuất.
AX-12\tLập kế hoạch tải là tối ưu vận hành; ranh giới tồn kho khi xếp/giao vẫn phải chuẩn.
AX-13\tThực hiện disposition dùng giao dịch trả/đổi trạng thái/tái phân loại/tiêu hủy rõ ràng; không viết lại số dư âm thầm.
AX-14\tChính sách an toàn kiểm soát điều kiện nhiệm vụ/thực hiện; không trực tiếp thay đổi số lượng tồn.
AX-15\tTự động hóa/thiết bị không sở hữu sự thật tồn kho; lệnh WMS phát sinh vẫn dùng hợp đồng ghi sổ chuẩn.
AX-16\tDữ liệu RFID/IoT chỉ quan sát cho tới khi lệnh WMS đã xác thực áp dụng thay đổi trạng thái/tồn kho chuẩn.
AX-17\tHỗ trợ giọng nói/đèn hướng dẫn nhiệm vụ nhưng không bỏ qua quy tắc lệnh/idempotency chuẩn.
AX-18\tChính sách Hazmat giới hạn lưu trữ/điều kiện sử dụng; mọi di chuyển/đổi trạng thái vẫn chuẩn và có kiểm toán.
AX-19\tSai lệch nhiệt độ tạo bằng chứng/giữ/disposition; thay đổi số lượng cần ghi sổ chuẩn rõ ràng.
AX-20\tSố lượng hai UOM được lưu với chuyển đổi/dung sai chuẩn và truy vết sổ cái bất biến.
AX-21\tĐổi chủ sở hữu là giao dịch tái phân loại tồn kho rõ ràng; số lượng vật lý được bảo toàn.
AX-22\tTính giá/lập hóa đơn dùng bằng chứng vận hành như mô hình đọc và không sở hữu sự thật tồn kho.
AX-23\tTính ATP/CTP/cam kết là kết quả lập kế hoạch chỉ đọc; giữ hàng cần lệnh chuẩn riêng.
AX-24\tCân bằng chỉ tạo đề xuất điều chuyển; tồn kho di chuyển qua Xuất/Nhận điều chuyển chuẩn.
AX-25\tLập kế hoạch bổ sung liên kho chỉ là khuyến nghị; thực hiện dùng lệnh điều chuyển/bổ sung chuẩn.
AX-26\tĐịnh tuyến chọn điểm hoàn tất đơn nhưng không thay đổi tồn; giữ/phân bổ phía sau vẫn là nguồn quyết định.
AX-27\tTín hiệu dự báo/nhu cầu chỉ là đầu vào lập kế hoạch và không bao giờ là sự thật tồn kho.
AX-28\tChính sách tồn an toàn/điểm đặt lại ảnh hưởng ngưỡng và dự phóng, không trực tiếp đổi số lượng vật lý.
AX-29\tPhân loại ABC/XYZ là metadata phân tích và không thay đổi tồn kho.
AX-30\tChính sách tối ưu tạo khuyến nghị; thực hiện được chấp nhận vẫn dùng lệnh nghiệp vụ chuẩn.
AX-31\tMô phỏng chạy trên ảnh chụp bất biến và không thể thay đổi dữ liệu chủ/chứng từ/sổ cái hệ thống thật.
AX-32\tĐộ chính xác/độ lệch dự báo chỉ là đo lường phân tích.
AX-33\tNgoại lệ bổ sung là trạng thái quy trình/mô hình đọc; xử lý đi qua hành động bổ sung/điều chuyển chuẩn.
AX-34\tĐề xuất mua hàng không tạo tồn hay sự thật PO cho tới khi tích hợp ERP/Mua hàng được phê duyệt.
AX-35\tĐiểm rủi ro là hỗ trợ quyết định có thể giải thích/chỉ đọc và không được tạo thay đổi nghiệp vụ ẩn.
AX-36\tPhân tích lead-time chỉ mang tính phân tích và không thay đổi sự thật PO/phiếu nhập/tồn kho.
AX-37\tTăng tốc/trì hoãn chỉ là khuyến nghị; ERP/Mua hàng vẫn là nguồn sự thật cung ứng thương mại.
AX-38\tPhát hiện bất thường nhu cầu chỉ tư vấn và không tự thay đổi bổ sung/tồn kho nếu chưa có chính sách quản trị.
AX-39\tChính sách công bằng tạo đề xuất phân bổ xác định; phân bổ thực tế dùng lệnh phân bổ nguyên tử chuẩn.
AX-40\tPhân khúc mức dịch vụ là metadata chính sách được quản trị và không trực tiếp thay đổi tồn kho.
AX-41\tKịch bản giả định được cô lập khỏi dữ liệu giao dịch/dữ liệu chủ/sổ cái hệ thống thật.
AX-42\tSổ đăng ký chính sách quyết định quản trị/phiên bản hóa khuyến nghị; thực hiện vẫn qua người/hợp đồng khi bắt buộc.
MD-09\tHợp đồng SLA là metadata chính sách/ngữ nghĩa; không trực tiếp thay đổi chứng từ hay tồn kho.
WH-07\tNgoại lệ lịch kiểm soát điều kiện/thời gian thực hiện và không trực tiếp đổi số lượng tồn.
IG-09\tWMS xuất sự kiện số lượng/biến động/định giá nhưng không tính sự thật kế toán về giá vốn/thuế/đa tiền tệ.
IG-10\tBảng điều khiển đối chiếu là lớp đọc/kiểm soát; thử lại phải idempotent và không được bịa thay đổi nghiệp vụ.
OP-10\tLỗi nghiệp vụ đã ghi sổ cần đảo + giao dịch hiệu chỉnh; sửa chữa có kiểm soát không được viết lại lịch sử sổ cái bất biến.
OP-11\tCông cụ hỗ trợ gọi hợp đồng Lớp ứng dụng và giữ quyền/kiểm toán/rào chắn tồn kho; không truy cập DB trực tiếp thường lệ.
OV-08\tDòng hoạt động là mô hình đọc trên sự kiện/kiểm toán/thông báo được quản trị và không thể trở thành sự thật giao dịch.
MO-11\tTra cứu sản phẩm là tìm kiếm chính xác chỉ đọc trong phạm vi bảo mật.
MO-12\tXử lý ngoại lệ chỉ đổi quy trình ngoại lệ/nhiệm vụ; mọi ảnh hưởng tồn kho phải đi qua lệnh chuẩn của nghiệp vụ sở hữu.`

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
    ? 'Sổ đăng ký quyền 17 + phạm vi kho; route hệ thống thật đã tồn tại và phía máy chủ vẫn là nguồn quyết định cuối.'
    : 'Bắt buộc Sổ đăng ký quyền 17 + phạm vi kho; phải ánh xạ chính xác mã triển khai trước khi bật trên hệ thống thật.';

const commandApiModelFor = (capability: BlueprintCapability) => {
  if (!capability.surfaces.includes('API')) return 'Bản xem trước chức năng này không yêu cầu bề mặt API trực tiếp.';
  if (capability.status === 'live') return 'Đã có bằng chứng API/route trong nhánh; ngữ nghĩa lệnh tiếp tục tuân theo các đặc tả chuẩn được tham chiếu.';
  if (capability.status === 'foundation') return 'Đã có nền tảng API một phần; hợp đồng lệnh/trạng thái/lỗi vẫn cần thêm bằng chứng hoàn thiện.';
  return 'Đặc tả chuẩn quy định hành vi lệnh/API bắt buộc; bản thiết kế chủ động không tuyên bố ánh xạ triển khai khi chưa có bằng chứng.';
};

const evidenceFor = (capability: BlueprintCapability, inventoryEffect: string): CapabilityEvidenceItem[] => {
  const implementation = implementationEvidence(capability.status);
  return [
    { key: 'business', label: 'Quy tắc nghiệp vụ', status: 'covered', note: 'Đã có đặc tả chuẩn được tham chiếu.' },
    { key: 'data', label: 'Mô hình dữ liệu', status: capability.status === 'live' ? 'covered' : implementation, note: 'Bằng chứng hệ thống thật đi theo mức trưởng thành triển khai; bản thiết kế không giả lập bảng dữ liệu.' },
    { key: 'api', label: 'Lệnh / API', status: implementation, note: commandApiModelFor(capability) },
    { key: 'permission', label: 'Quyền', status: capability.status === 'live' ? 'partial' : 'spec-only', note: permissionModelFor(capability) },
    { key: 'ux', label: 'UX / Màn hình', status: 'covered', note: 'Đã có mô phỏng chức năng tương tác + trung tâm công việc của phân hệ; triển khai hệ thống thật vẫn được theo dõi riêng.' },
    { key: 'inventory', label: 'Ảnh hưởng tồn kho', status: 'covered', note: inventoryEffect },
    { key: 'event', label: 'Sự kiện / Lỗi', status: capability.status === 'live' ? 'partial' : 'spec-only', note: 'Phải tiếp tục phù hợp hợp đồng sự kiện/lỗi chuẩn và chính sách idempotency.' },
    { key: 'test', label: 'Bằng chứng kiểm thử', status: capability.status === 'live' ? 'partial' : 'spec-only', note: 'Đã có kiểm thử bản thiết kế/mô phỏng; bằng chứng hệ thống thật và tích hợp được theo dõi riêng.' },
    { key: 'operations', label: 'Đơn vị sở hữu vận hành', status: 'covered', note: 'Phân hệ sở hữu đã rõ; bằng chứng sổ tay vận hành/phát hành đi theo mức trưởng thành triển khai.' },
  ];
};

export const getCapabilityGovernanceProfile = (
  module: BlueprintModule,
  capability: BlueprintCapability,
): CapabilityGovernanceProfile => {
  const inventoryEffect = inventoryEffects[capability.id]
    ?? 'Không thay đổi số dư tùy tiện. Tuân theo đặc tả chuẩn được tham chiếu; chức năng báo cáo/mô hình đọc không làm thay đổi sổ cái.';
  const traceability = screenTraceability[capability.id];

  return {
    releaseWave: releaseWaveFor(module.key, capability.id),
    applicability: applicabilityFor(module.key, capability),
    maturity: maturityByStatus[capability.status],
    ownerModule: module.name,
    referencedSpecs: parseSpecNumbers(capability.spec),
    permissionModel: permissionModelFor(capability),
    commandApiModel: commandApiModelFor(capability),
    stateModel: module.flow?.join(' → ') ?? 'Hợp đồng chuyển trạng thái được quy định bởi đặc tả của chức năng.',
    inventoryEffect,
    operationalOwner: module.name,
    reviewStatus: traceability ? 'Traceability Closed' : 'Designed',
    screenReference: traceability?.screenReference,
    reviewFinding: traceability?.finding,
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
  covered: 'Đã có bằng chứng',
  partial: 'Một phần',
  'spec-only': 'Chỉ có đặc tả',
  missing: 'Thiếu',
};
