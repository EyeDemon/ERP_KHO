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
    screenReference: 'UNMAPPED — Chuyển vị trí (Location Transfer)',
    title: 'Internal Location Transfer Workbench',
    subtitle: 'Same-warehouse move với scan source → product/lot/serial → quantity/UOM → destination.',
    fields: [
      { label: 'Kho', value: 'WH-HCM-01', helper: 'Source và destination phải cùng warehouse.' },
      { label: 'Vị trí nguồn', value: 'A01-R02-L03-B04' },
      { label: 'Vị trí đích', value: 'B02-R01-L02-B03' },
      { label: 'Sản phẩm', value: 'SKU-1001 • Cà phê Arabica 500g' },
      { label: 'Operation UOM', value: 'Gói' },
      { label: 'Số lượng', value: '40 Gói', helper: 'Base UOM: 40 Gói.' },
      { label: 'Lot', value: 'LOT-1001-260930' },
      { label: 'Lý do', value: 'REBALANCE_PICK_FACE' },
    ],
    steps: ['Quét vị trí nguồn', 'Quét SKU / lot / serial', 'Nhập quantity + UOM', 'Quét vị trí đích', 'Validate lock/status/capacity', 'Confirm move'],
    validations: [
      'Source ≠ destination và cùng warehouse.',
      'Quantity không vượt eligible quantity tại source.',
      'Lot/serial/status/owner phải hợp lệ và không bị lock.',
      'Destination phải tương thích product/status/capacity.',
      'Quantity entry luôn hiển thị operation UOM + Base UOM.',
    ],
    inventoryBoundary: 'Posting tạo source -Q và destination +Q; tổng OnHand toàn warehouse được bảo toàn.',
    permissionNote: 'Server phải kiểm tra permission + warehouse scope; UI không được thay server authorization.',
  },
  {
    capabilityIds: ['OUT-05', 'MO-04'],
    screenReference: 'UNMAPPED — Lấy hàng(Picking)',
    title: 'Picking Workbench / Scan Flow',
    subtitle: 'Scan-first task execution với allocated stock, lot/serial validation và short-pick exception.',
    fields: [
      { label: 'Wave / Task', value: 'WV-2026-301 / PICK-2026-6110' },
      { label: 'Vị trí lấy', value: 'A01-R02-L03-B04' },
      { label: 'SKU', value: 'SKU-1001' },
      { label: 'Allocated', value: '120 Gói' },
      { label: 'Picked', value: '118 Gói', helper: 'Short 2 Gói cần reason/evidence.' },
      { label: 'Lot', value: 'LOT-1001-260930' },
      { label: 'Destination', value: 'PACK-02' },
      { label: 'Short-pick reason', value: 'LOCATION_SHORT' },
    ],
    steps: ['Claim task', 'Scan location', 'Scan product/lot/serial', 'Confirm quantity', 'Resolve short pick nếu có', 'Move to pack/stage'],
    validations: [
      'Chỉ pick inventory đã allocated và eligible.',
      'Serial tracked item phải scan đúng từng serial.',
      'Short pick phải có reason code và tạo exception/reallocation path.',
      'Picking không được giảm warehouse OnHand; dispatch mới là outbound deduction boundary.',
      'Quantity entry luôn hiển thị operation UOM + Base UOM.',
    ],
    inventoryBoundary: 'Pick thay đổi execution/location state nhưng warehouse OnHand không giảm trước Shipment Dispatch.',
    permissionNote: 'Task claim/execute phải kiểm tra warehouse scope, assignee policy và task version.',
  },
  {
    capabilityIds: ['OUT-06', 'MO-05'],
    screenReference: 'UNMAPPED — Đóng gói (Packing)',
    title: 'Packing Station',
    subtitle: 'Pack vào carton/tote/HU, xác nhận quantity, weight/dimension và tạo label có audit.',
    fields: [
      { label: 'Shipment', value: 'SHP-2026-5108' },
      { label: 'Pack station', value: 'PACK-02' },
      { label: 'Handling Unit', value: 'CTN-SHP-5108-01' },
      { label: 'Packaging Type', value: 'PKG-CARTON-M' },
      { label: 'Items', value: '22 Cái' },
      { label: 'Weight', value: '8.4 kg' },
      { label: 'Dimensions', value: '400 × 300 × 250 mm' },
      { label: 'Label', value: 'SSCC / Shipping Label • pending print' },
    ],
    steps: ['Scan shipment/order', 'Open/create HU', 'Scan packed items', 'Validate complete quantity', 'Capture weight/dimensions', 'Print label', 'Close HU'],
    validations: [
      'Packed quantity không vượt picked quantity.',
      'Serial item không được đóng gói trùng hoặc thiếu.',
      'HU hierarchy và packaging type phải hợp lệ.',
      'Reprint label phải có audit/reason.',
      'Packing không được tự trừ warehouse OnHand.',
    ],
    inventoryBoundary: 'Packing/consolidation là HU/execution state; physical deduction vẫn chỉ xảy ra tại Shipment Dispatch.',
    permissionNote: 'Pack/close/reprint phải tách permission theo canonical Permission Registry khi production hóa.',
  },
  {
    capabilityIds: ['TR-01', 'MO-07'],
    screenReference: 'UNMAPPED — Tạo phiếu chuyển kho',
    title: 'Create Warehouse Transfer',
    subtitle: 'Tạo yêu cầu source → destination, lines/UOM và policy trước khi dispatch sang In Transit.',
    fields: [
      { label: 'Transfer No.', value: 'TRF-DRAFT-0024' },
      { label: 'Kho nguồn', value: 'WH-HCM-01' },
      { label: 'Kho đích', value: 'WH-DN-01' },
      { label: 'SKU', value: 'SKU-1001' },
      { label: 'Requested Qty', value: '200 Gói', helper: 'Base UOM: 200 Gói.' },
      { label: 'Requested ship date', value: '03/10/2026' },
      { label: 'Reason', value: 'NETWORK_REBALANCE' },
      { label: 'State', value: 'DRAFT' },
    ],
    steps: ['Create draft', 'Add/validate lines', 'Submit/approve nếu policy yêu cầu', 'Dispatch source → transit', 'Receive transit → destination', 'Close/reconcile'],
    validations: [
      'Source và destination warehouse phải khác nhau.',
      'Product/UOM phải active và convert được về Base UOM.',
      'Create/approve chưa được mutate inventory.',
      'Dispatch/Receive phải idempotent và concurrency-safe.',
      'Source + Transit + Destination luôn bảo toàn transfer quantity.',
    ],
    inventoryBoundary: 'Create/approve ledger-neutral; Dispatch source→transit; Receive transit→destination.',
    permissionNote: 'Create/approve/dispatch/receive là các command khác nhau và phải kiểm tra scope ở server.',
  },
];

export const getSpecializedScreenPreview = (capabilityId?: string) =>
  specializedScreenPreviews.find((preview) => capabilityId && preview.capabilityIds.includes(capabilityId));
