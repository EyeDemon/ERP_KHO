export interface SpecializedField {
  label: string;
  value: string;
  helper?: string;
}

export interface SpecializedScreenPreview {
  capabilityIds: string[];
  screenReference: string;
  title: string;
  subtitle: string;
  fields: SpecializedField[];
  steps: string[];
  validations: string[];
  inventoryBoundary: string;
  permissionNote: string;
}

export const specializedScreenPreviews: SpecializedScreenPreview[] = [
  {
    capabilityIds: ['INV-08'],
    screenReference: 'BẢN THIẾT KẾ • /system-blueprint/inventory-control/INV-08 • Bàn làm việc di chuyển vị trí nội bộ',
    title: 'Bàn làm việc di chuyển vị trí nội bộ',
    subtitle: 'Di chuyển trong cùng kho theo luồng quét vị trí nguồn → sản phẩm/lô/sê-ri → số lượng/UOM → vị trí đích.',
    fields: [
      { label: 'Kho', value: 'WH-HCM-01', helper: 'Vị trí nguồn và vị trí đích phải thuộc cùng kho.' },
      { label: 'Vị trí nguồn', value: 'A01-R02-L03-B04' },
      { label: 'Vị trí đích', value: 'B02-R01-L02-B03' },
      { label: 'Sản phẩm', value: 'SKU-1001 • Cà phê Arabica 500g' },
      { label: 'UOM thao tác', value: 'Gói' },
      { label: 'Số lượng', value: '40 Gói', helper: 'UOM cơ sở: 40 Gói.' },
      { label: 'Lô', value: 'LOT-1001-260930' },
      { label: 'Lý do', value: 'REBALANCE_PICK_FACE' },
    ],
    steps: ['Quét vị trí nguồn', 'Quét SKU / lô / sê-ri', 'Nhập số lượng + UOM', 'Quét vị trí đích', 'Kiểm tra khóa/trạng thái/sức chứa', 'Xác nhận di chuyển'],
    validations: [
      'Vị trí nguồn ≠ vị trí đích và cùng kho.',
      'Số lượng không vượt lượng đủ điều kiện tại vị trí nguồn.',
      'Lô/sê-ri/trạng thái/chủ sở hữu phải hợp lệ và không bị khóa.',
      'Vị trí đích phải tương thích sản phẩm/trạng thái/sức chứa.',
      'Trường số lượng luôn hiển thị UOM thao tác + UOM cơ sở.',
    ],
    inventoryBoundary: 'Ghi sổ tạo nguồn -Q và đích +Q; tổng OnHand toàn kho được bảo toàn.',
    permissionNote: 'Phía máy chủ phải kiểm tra quyền + phạm vi kho; giao diện không được thay thế phân quyền phía máy chủ.',
  },
  {
    capabilityIds: ['OUT-05', 'MO-04'],
    screenReference: 'BLUEPRINT • OUT-05 /system-blueprint/outbound/OUT-05 • MO-04 /system-blueprint/mobile/MO-04',
    title: 'Bàn làm việc lấy hàng / Luồng quét',
    subtitle: 'Thực hiện nhiệm vụ ưu tiên quét trên tồn đã phân bổ, kiểm tra lô/sê-ri và xử lý ngoại lệ lấy thiếu.',
    fields: [
      { label: 'Đợt / Nhiệm vụ', value: 'WV-2026-301 / PICK-2026-6110' },
      { label: 'Vị trí lấy', value: 'A01-R02-L03-B04' },
      { label: 'SKU', value: 'SKU-1001' },
      { label: 'Đã phân bổ', value: '120 Gói' },
      { label: 'Đã lấy', value: '118 Gói', helper: 'Thiếu 2 Gói cần lý do/bằng chứng.' },
      { label: 'Lô', value: 'LOT-1001-260930' },
      { label: 'Điểm đến', value: 'PACK-02' },
      { label: 'Lý do lấy thiếu', value: 'LOCATION_SHORT' },
    ],
    steps: ['Nhận nhiệm vụ', 'Quét vị trí', 'Quét sản phẩm/lô/sê-ri', 'Xác nhận số lượng', 'Xử lý lấy thiếu nếu có', 'Chuyển sang đóng gói/khu chờ'],
    validations: [
      'Chỉ lấy tồn đã được phân bổ và đủ điều kiện.',
      'Hàng theo dõi sê-ri phải quét đúng từng sê-ri.',
      'Lấy thiếu phải có mã lý do và tạo đường xử lý ngoại lệ/phân bổ lại.',
      'Lấy hàng không được làm giảm OnHand toàn kho; xác nhận giao hàng mới là ranh giới khấu trừ xuất kho.',
      'Trường số lượng luôn hiển thị UOM thao tác + UOM cơ sở.',
    ],
    inventoryBoundary: 'Lấy hàng thay đổi trạng thái thực hiện/vị trí nhưng OnHand toàn kho không giảm trước khi xác nhận giao hàng.',
    permissionNote: 'Nhận/thực hiện nhiệm vụ phải kiểm tra phạm vi kho, chính sách người được giao và phiên bản nhiệm vụ.',
  },
  {
    capabilityIds: ['OUT-06', 'MO-05'],
    screenReference: 'BLUEPRINT • OUT-06 /system-blueprint/outbound/OUT-06 • MO-05 /system-blueprint/mobile/MO-05',
    title: 'Trạm đóng gói',
    subtitle: 'Đóng gói vào thùng/khay/HU, xác nhận số lượng, khối lượng/kích thước và tạo nhãn có lưu vết kiểm toán.',
    fields: [
      { label: 'Lô giao hàng', value: 'SHP-2026-5108' },
      { label: 'Trạm đóng gói', value: 'PACK-02' },
      { label: 'Đơn vị xử lý', value: 'CTN-SHP-5108-01' },
      { label: 'Loại bao bì', value: 'PKG-CARTON-M' },
      { label: 'Mặt hàng', value: '22 Cái' },
      { label: 'Khối lượng', value: '8.4 kg' },
      { label: 'Kích thước', value: '400 × 300 × 250 mm' },
      { label: 'Nhãn', value: 'SSCC / Nhãn giao hàng • chờ in' },
    ],
    steps: ['Quét lô giao hàng/đơn hàng', 'Mở/tạo HU', 'Quét mặt hàng đã đóng gói', 'Kiểm tra đủ số lượng', 'Ghi nhận khối lượng/kích thước', 'In nhãn', 'Đóng HU'],
    validations: [
      'Số lượng đóng gói không vượt số lượng đã lấy.',
      'Hàng theo dõi sê-ri không được đóng gói trùng hoặc thiếu.',
      'Phân cấp HU và loại bao bì phải hợp lệ.',
      'In lại nhãn phải có kiểm toán/lý do.',
      'Đóng gói không được tự làm giảm OnHand toàn kho.',
    ],
    inventoryBoundary: 'Đóng gói/hợp nhất là trạng thái HU/thực hiện; khấu trừ vật lý vẫn chỉ xảy ra khi xác nhận giao hàng.',
    permissionNote: 'Đóng gói/đóng HU/in lại phải tách quyền theo Sổ đăng ký quyền chuẩn khi đưa lên hệ thống thật.',
  },
  {
    capabilityIds: ['TR-01', 'MO-07'],
    screenReference: 'BẢN THIẾT KẾ • TR-01 /system-blueprint/transfer-replenishment/TR-01 • MO-07 /system-blueprint/mobile/MO-07',
    title: 'Tạo điều chuyển kho',
    subtitle: 'Tạo yêu cầu kho nguồn → kho đích, dòng hàng/UOM và chính sách trước khi xuất sang trạng thái Đang vận chuyển.',
    fields: [
      { label: 'Số điều chuyển', value: 'TRF-DRAFT-0024' },
      { label: 'Kho nguồn', value: 'WH-HCM-01' },
      { label: 'Kho đích', value: 'WH-DN-01' },
      { label: 'SKU', value: 'SKU-1001' },
      { label: 'Số lượng yêu cầu', value: '200 Gói', helper: 'UOM cơ sở: 200 Gói.' },
      { label: 'Ngày xuất dự kiến', value: '03/10/2026' },
      { label: 'Lý do', value: 'NETWORK_REBALANCE' },
      { label: 'Trạng thái', value: 'DRAFT' },
    ],
    steps: ['Tạo bản nháp', 'Thêm/kiểm tra dòng hàng', 'Gửi/phê duyệt nếu chính sách yêu cầu', 'Xuất kho nguồn → đang vận chuyển', 'Nhận hàng đang vận chuyển → kho đích', 'Đóng/đối chiếu'],
    validations: [
      'Kho nguồn và kho đích phải khác nhau.',
      'Sản phẩm/UOM phải đang hoạt động và chuyển đổi được về UOM cơ sở.',
      'Tạo/phê duyệt chưa được thay đổi tồn kho.',
      'Xuất/Nhận phải idempotent và an toàn đồng thời.',
      'Nguồn + Đang vận chuyển + Đích luôn bảo toàn số lượng điều chuyển.',
    ],
    inventoryBoundary: 'Tạo/phê duyệt không tác động sổ cái; Xuất chuyển nguồn→đang vận chuyển; Nhận chuyển đang vận chuyển→đích.',
    permissionNote: 'Tạo/phê duyệt/xuất/nhận là các lệnh khác nhau và phải kiểm tra phạm vi ở phía máy chủ.',
  },
];

export const getSpecializedScreenPreview = (capabilityId?: string) =>
  specializedScreenPreviews.find((preview) => capabilityId && preview.capabilityIds.includes(capabilityId));
