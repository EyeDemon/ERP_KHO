export type BlueprintStatus = 'live' | 'foundation' | 'planned' | 'optional';

export type BlueprintSurface = 'Web' | 'Mobile' | 'API' | 'Worker';

export interface BlueprintCapability {
  id: string;
  name: string;
  goal: string;
  surfaces: BlueprintSurface[];
  status: BlueprintStatus;
  spec: string;
  route?: string;
}

export interface BlueprintModule {
  key: string;
  name: string;
  description: string;
  flow?: string[];
  capabilities: BlueprintCapability[];
}

export const blueprintStatusLabels: Record<BlueprintStatus, string> = {
  live: 'Đã có chức năng',
  foundation: 'Đã có nền / đang hoàn thiện',
  planned: 'Theo đặc tả — chưa triển khai',
  optional: 'Nâng cao / bật theo nhu cầu',
};

export const blueprintDemoStatusLabels: Record<BlueprintStatus, string> = {
  live: 'Có mock + chức năng thật',
  foundation: 'Có mock + nền thật',
  planned: 'Có mock tương tác • production chưa có',
  optional: 'Có mock nâng cao • bật khi cần',
};

export const erpWmsBlueprint: BlueprintModule[] = [
  {
    key: 'overview',
    name: 'Tổng quan & Work Center',
    description: 'Điểm vào chung cho quản lý công việc, phê duyệt, ngoại lệ và sức khỏe vận hành.',
    flow: ['Dashboard', 'Nhiệm vụ của tôi', 'Task Queue', 'Phê duyệt', 'Ngoại lệ', 'Global Search', 'Control Tower'],
    capabilities: [
      { id: 'OV-01', name: 'Dashboard vận hành', goal: 'KPI inbound, outbound, tồn kho, backlog, SLA và cảnh báo.', surfaces: ['Web'], status: 'foundation', spec: '68, 216, 227', route: '/' },
      { id: 'OV-02', name: 'Nhiệm vụ của tôi', goal: 'Tập hợp task được giao theo kho, độ ưu tiên và hạn xử lý.', surfaces: ['Web', 'Mobile'], status: 'planned', spec: '80, 216, 223' },
      { id: 'OV-03', name: 'Trung tâm phê duyệt', goal: 'Duyệt/từ chối nghiệp vụ có SoD, SLA và audit trail.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 70, 221', route: '/approvals' },
      { id: 'OV-04', name: 'Trung tâm ngoại lệ', goal: 'Triage, assign, resolve và đóng exception theo workflow.', surfaces: ['Web', 'API'], status: 'planned', spec: '71, 92, 221' },
      { id: 'OV-05', name: 'Distributed Warehouse Control Tower', goal: 'Theo dõi nhiều kho theo exception-driven operations.', surfaces: ['Web'], status: 'planned', spec: '183, 195' },
      { id: 'OV-06', name: 'Warehouse Task Engine & Work Queue', goal: 'Chuẩn hóa RECEIVE/PUTAWAY/PICK/PACK/MOVE/REPLENISH/COUNT/LOAD/QC/RETURN/EXCEPTION/VAS thành task dùng chung web/mobile.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'planned', spec: '80' },
      { id: 'OV-07', name: 'Enterprise / Global Search', goal: 'Exact-first search xuyên Product, Barcode, Partner, Document, Lot/Serial, Location, Shipment và Task theo security scope.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'planned', spec: '95, 216' },
      { id: 'OV-08', name: 'Activity Feed', goal: 'Read model hợp nhất audit/business event/notification để người dùng xem hoạt động gần đây theo permission và warehouse scope.', surfaces: ['Web', 'API'], status: 'planned', spec: '61, 83, 216, 271' },
    ],
  },
  {
    key: 'master-data',
    name: 'Dữ liệu danh mục',
    description: 'Nguồn dữ liệu chuẩn cho sản phẩm, mã vạch, UOM, đối tác và các danh mục dùng chung.',
    capabilities: [
      { id: 'MD-01', name: 'Sản phẩm / SKU', goal: 'CRUD sản phẩm, trạng thái, thuộc tính và tracking policy.', surfaces: ['Web', 'API'], status: 'live', spec: '16, 84, 162', route: '/products' },
      { id: 'MD-02', name: 'Danh mục sản phẩm', goal: 'Phân loại sản phẩm theo category và cấu trúc tìm kiếm.', surfaces: ['Web', 'API'], status: 'foundation', spec: '84, 162' },
      { id: 'MD-03', name: 'Barcode đa mã', goal: 'Quản lý nhiều barcode và exact lookup cho scan.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '65, 84, 162' },
      { id: 'MD-04', name: 'Đơn vị tính & quy đổi', goal: 'Quản lý UOM và quy đổi về Base UOM cho mọi quantity entry.', surfaces: ['Web', 'API'], status: 'live', spec: '16, 84, 220', route: '/units' },
      { id: 'MD-05', name: 'Business Partner', goal: 'Supplier/Customer dùng chung, role, active state và snapshot trên chứng từ.', surfaces: ['Web', 'API'], status: 'foundation', spec: '84, 162', route: '/business-partners' },
      { id: 'MD-06', name: 'Carrier', goal: 'Danh mục đơn vị vận chuyển và điều kiện tích hợp.', surfaces: ['Web', 'API'], status: 'planned', spec: '58, 84, 164' },
      { id: 'MD-07', name: 'Reason Code Catalog', goal: 'Chuẩn hóa lý do điều chỉnh, hủy, exception, damage, scrap.', surfaces: ['Web', 'API'], status: 'planned', spec: '20, 276' },
      { id: 'MD-08', name: 'Packaging Type', goal: 'Danh mục carton/tote/pallet/packaging profile dùng cho packing, HU và cartonization khi áp dụng.', surfaces: ['Web', 'API'], status: 'planned', spec: '37, 238' },
      { id: 'MD-09', name: 'Customer / Supplier / Carrier SLA Contracts', goal: 'Quản lý SLA contract dùng chung cho supplier, customer, carrier và 3PL client để KPI/event có cùng semantic.', surfaces: ['Web', 'API'], status: 'planned', spec: '174' },
    ],
  },
  {
    key: 'warehouse-structure',
    name: 'Kho & Vị trí',
    description: 'Mô hình vật lý từ warehouse tới bin, capacity, calendar, dock và yard.',
    capabilities: [
      { id: 'WH-01', name: 'Warehouse', goal: 'Quản lý kho, scope truy cập, timezone và trạng thái vận hành.', surfaces: ['Web', 'API'], status: 'live', spec: '16, 60, 148', route: '/warehouses' },
      { id: 'WH-02', name: 'Zone / Aisle / Rack / Level / Bin', goal: 'Cấu trúc vị trí lưu trữ nhiều cấp.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '67, 85' },
      { id: 'WH-03', name: 'Capacity & Storage Constraints', goal: 'Kiểm tra sức chứa và compatibility trước putaway/move.', surfaces: ['Web', 'API'], status: 'planned', spec: '85, 194' },
      { id: 'WH-04', name: 'Warehouse Map & Heatmap', goal: 'Hiển thị bản đồ kho, utilization, congestion và operational heatmap.', surfaces: ['Web'], status: 'planned', spec: '67' },
      { id: 'WH-05', name: 'Warehouse Calendar & Shift', goal: 'Lịch mở cửa, ca làm việc, cutoff và capacity vận hành.', surfaces: ['Web', 'API'], status: 'planned', spec: '60' },
      { id: 'WH-06', name: 'Dock & Yard', goal: 'Quản lý dock door, yard position, check-in và queue.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '46, 248' },
      { id: 'WH-07', name: 'Operational Calendar Exceptions', goal: 'Quản lý company/warehouse/shift calendar exception và emergency override với precedence rõ ràng.', surfaces: ['Web', 'API'], status: 'planned', spec: '60, 175, 176' },
    ],
  },
  {
    key: 'inbound',
    name: 'Nhập kho',
    description: 'Luồng từ expected inbound tới receiving, QC, posting và putaway.',
    flow: ['PO/ASN', 'Appointment', 'Receipt', 'Receiving', 'QC', 'Ready to Post', 'Post', 'Putaway'],
    capabilities: [
      { id: 'IN-01', name: 'Purchase Order / ASN', goal: 'Nhận expected inbound từ ERP/Procurement và theo dõi ASN.', surfaces: ['Web', 'API'], status: 'planned', spec: '34, 160' },
      { id: 'IN-02', name: 'Receiving Appointment', goal: 'Đặt lịch xe/hàng vào kho và phân dock.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '46' },
      { id: 'IN-03', name: 'Goods Receipt Work Center', goal: 'Tìm/lọc/tạo/mở receipt theo state và warehouse scope.', surfaces: ['Web', 'API'], status: 'foundation', spec: '34, 216, 228, 229', route: '/import-receipts' },
      { id: 'IN-04', name: 'Receiving Workbench', goal: 'Scan hàng, nhập quantity/UOM, lot/serial/expiry và discrepancy.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '34, 228, 229' },
      { id: 'IN-05', name: 'Over/Under Receipt Discrepancy', goal: 'Xử lý tolerance, thiếu/thừa và approval khi vượt ngưỡng.', surfaces: ['Web', 'API'], status: 'planned', spec: '34, 70, 71, 228, 229' },
      { id: 'IN-06', name: 'Inbound QC', goal: 'Kiểm tra line bắt buộc QC, evidence và disposition.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '34, 41, 57, 62, 90' },
      { id: 'IN-07', name: 'Receipt Posting', goal: 'Boundary ghi nhận inventory, idempotent, concurrency-safe và audit/outbox.', surfaces: ['Web', 'API'], status: 'foundation', spec: '29, 34, 228' },
      { id: 'IN-08', name: 'Putaway Tasks', goal: 'Phân công, start, move, exception, resume, cancel và hoàn tất cất hàng.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '35, 229', route: '/putaway-tasks' },
      { id: 'IN-09', name: 'Putaway Rule Engine', goal: 'Đề xuất vị trí dựa trên capacity, status, owner và product rule.', surfaces: ['Web', 'API'], status: 'planned', spec: '35, 85' },
    ],
  },
  {
    key: 'outbound',
    name: 'Xuất kho',
    description: 'Luồng từ order tới reservation, allocation, picking, packing, loading và dispatch.',
    flow: ['Order', 'Release', 'Reserve', 'Allocate', 'Pick', 'Pack', 'Stage', 'Load', 'Dispatch', 'Track / POD'],
    capabilities: [
      { id: 'OUT-01', name: 'Sales / Export Order', goal: 'Quản lý phiếu xuất và điều kiện release.', surfaces: ['Web', 'API'], status: 'live', spec: '72, 87, 161, 228', route: '/export-receipts' },
      { id: 'OUT-02', name: 'Reservation', goal: 'Giữ quantity khả dụng mà chưa gắn location cụ thể.', surfaces: ['Web', 'API'], status: 'live', spec: '30, 72', route: '/stock-reservations' },
      { id: 'OUT-03', name: 'Allocation', goal: 'Gắn reserved quantity vào location/lot/serial hợp lệ.', surfaces: ['Web', 'API'], status: 'planned', spec: '30, 86, 228' },
      { id: 'OUT-04', name: 'Wave / Batch / Cluster', goal: 'Nhóm order/pick task để tối ưu thực thi.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '45' },
      { id: 'OUT-05', name: 'Picking', goal: 'Scan-first picking với short pick, serial/status validation và exception.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '36, 65, 72, 223, 228' },
      { id: 'OUT-06', name: 'Packing', goal: 'Đóng gói vào carton/tote/HU, quantity confirmation và label.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '37, 64, 238, 228' },
      { id: 'OUT-07', name: 'Staging & Loading', goal: 'Xếp hàng chờ, kiểm tra load và seal/vehicle context.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '38, 46, 239' },
      { id: 'OUT-08', name: 'Shipment Dispatch', goal: 'Boundary trừ OnHand vật lý đúng một lần và consume reservation/allocation.', surfaces: ['Web', 'API'], status: 'planned', spec: '29, 31, 38, 228' },
      { id: 'OUT-09', name: 'Backorder', goal: 'Theo dõi ordered/reserved/allocated/picked/shipped/backorder/cancelled.', surfaces: ['Web', 'API'], status: 'planned', spec: '72, 87' },
      { id: 'OUT-10', name: 'Shipment Tracking / POD / Delivery Failure', goal: 'Theo dõi post-dispatch logistics state, POD, delivery failure/retry và return-to-warehouse mà không double inventory movement.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '58, 74, 164' },
    ],
  },
  {
    key: 'inventory-control',
    name: 'Tồn kho & Inventory Control',
    description: 'Nguồn sự thật vận hành: immutable ledger, balance projection, availability và inventory status.',
    capabilities: [
      { id: 'INV-01', name: 'Inventory Browser', goal: 'Tra cứu OnHand, Available, Reserved, Allocated, Picked, QC Hold, Quarantine, Blocked, In Transit.', surfaces: ['Web', 'API'], status: 'foundation', spec: '29, 41, 73, 77, 78, 82, 88, 222', route: '/inventory' },
      { id: 'INV-02', name: 'Immutable Inventory Ledger', goal: 'Lưu mọi movement bất biến, có source reference và signed delta.', surfaces: ['Web', 'API'], status: 'foundation', spec: '28, 29, 32' },
      { id: 'INV-03', name: 'Inventory Balance Projection', goal: 'Projection có thể rebuild từ ledger và dùng cho operational queries.', surfaces: ['API', 'Worker'], status: 'foundation', spec: '29, 82' },
      { id: 'INV-04', name: 'Inventory Availability Engine', goal: 'Tính eligible inventory sau status/lock/reservation/allocation rules.', surfaces: ['Web', 'API'], status: 'foundation', spec: '30, 41, 77, 88' },
      { id: 'INV-05', name: 'Inventory Status', goal: 'AVAILABLE, QC_HOLD, QUARANTINE, BLOCKED, DAMAGED, EXPIRED, RECALL_BLOCKED…', surfaces: ['Web', 'API'], status: 'planned', spec: '41, 222, 229' },
      { id: 'INV-06', name: 'Lot / Serial / Expiry', goal: 'Trace lot/serial, expiry, FEFO/FIFO eligibility và duplicate protection.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '48, 65, 78, 222' },
      { id: 'INV-07', name: 'Inventory Locks / Freeze', goal: 'Khóa inventory theo policy và ngăn mutation trái phép.', surfaces: ['Web', 'API'], status: 'planned', spec: '31, 40, 77' },
      { id: 'INV-08', name: 'Internal Location Move', goal: 'Di chuyển cùng kho qua posting boundary và giữ tổng quantity.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '29, 222, 282' },
      { id: 'INV-09', name: 'Reversal', goal: 'Sửa sai bằng transaction đảo/corrective thay vì sửa lịch sử đã post.', surfaces: ['Web', 'API'], status: 'planned', spec: '32' },
      { id: 'INV-10', name: 'Traceability & Genealogy', goal: 'Truy ngược/xuôi theo lot/serial/document/return/recall.', surfaces: ['Web', 'API'], status: 'planned', spec: '48, 79, 222' },
      { id: 'INV-11', name: 'Inventory Integrity & Reconciliation Engine', goal: 'Phát hiện ledger/balance mismatch, chạy reconciliation và rebuild projection có kiểm soát, audit và operational evidence.', surfaces: ['Web', 'API', 'Worker'], status: 'foundation', spec: '82, 152', route: '/inventory-reconciliation' },
    ],
  },
  {
    key: 'transfer-replenishment',
    name: 'Điều chuyển & Replenishment',
    description: 'Bảo toàn quantity qua nguồn, transit, đích và bổ sung pick face.',
    flow: ['Create', 'Approve', 'Dispatch', 'In Transit', 'Receive', 'Close'],
    capabilities: [
      { id: 'TR-01', name: 'Warehouse Transfer', goal: 'Tạo và quản lý chuyển kho source → transit → destination.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '39, 157', route: '/stock-transfers' },
      { id: 'TR-02', name: 'In-Transit Inventory', goal: 'Theo dõi stock đang transit theo source/destination/reference/owner.', surfaces: ['Web', 'API'], status: 'planned', spec: '39' },
      { id: 'TR-03', name: 'Transfer Dispatch', goal: 'Rời source và chuyển quantity sang transit đúng một lần.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '39' },
      { id: 'TR-04', name: 'Transfer Receive', goal: 'Nhận transit vào destination và reconcile thiếu/thừa.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '39' },
      { id: 'TR-05', name: 'Replenishment', goal: 'Bổ sung hàng từ reserve storage sang picking location.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '44, 223' },
    ],
  },
  {
    key: 'count-adjustment',
    name: 'Kiểm kê & Điều chỉnh',
    description: 'Full Count, Cycle Count, blind count, recount, variance resolution và adjustment có approval.',
    flow: ['Plan', 'Freeze/Snapshot', 'Count', 'Review', 'Recount', 'Approval', 'Post', 'Complete'],
    capabilities: [
      { id: 'CT-01', name: 'Full Count', goal: 'Kiểm kê toàn kho theo policy và progress tracking.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '40, 216', route: '/stocktakes' },
      { id: 'CT-02', name: 'Cycle Count', goal: 'Lập kế hoạch kiểm kê định kỳ theo rule/ABC/risk.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '40' },
      { id: 'CT-03', name: 'Blind Count', goal: 'Ẩn system quantity với counter để giảm bias.', surfaces: ['Web', 'Mobile'], status: 'planned', spec: '40, 229' },
      { id: 'CT-04', name: 'Freeze Strategy', goal: 'Hard freeze / soft freeze / snapshot-only theo warehouse/count type.', surfaces: ['Web', 'API'], status: 'planned', spec: '40, 229' },
      { id: 'CT-05', name: 'Recount', goal: 'Lưu immutable attempt history và final accepted count.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '40, 229' },
      { id: 'CT-06', name: 'Variance Resolution', goal: 'Xem evidence, reason và chọn phương án xử lý.', surfaces: ['Web', 'API'], status: 'planned', spec: '40, 71' },
      { id: 'CT-07', name: 'Inventory Adjustment', goal: 'Tạo/approve/post adjustment theo threshold và SoD.', surfaces: ['Web', 'API'], status: 'planned', spec: '40, 229' },
    ],
  },
  {
    key: 'quality-returns',
    name: 'Chất lượng, Returns & Disposition',
    description: 'Kiểm soát hàng lỗi, quarantine, returns, recall và scrap.',
    capabilities: [
      { id: 'QR-01', name: 'QC Work Center', goal: 'Quản lý inspection, criteria, evidence và disposition.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '41, 57, 62, 90' },
      { id: 'QR-02', name: 'QC Hold / Quarantine', goal: 'Chuyển status và chặn reserve/allocate/pick theo policy.', surfaces: ['Web', 'API'], status: 'planned', spec: '41' },
      { id: 'QR-03', name: 'Damaged Inventory', goal: 'Ghi nhận DAMAGE_FOUND từ nhiều flow và disposition.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '41, 42, 62, 71' },
      { id: 'QR-04', name: 'Customer Returns / RMA', goal: 'Receive return, inspect, disposition và post inventory.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '43, 247' },
      { id: 'QR-05', name: 'Recall', goal: 'Trace scope ảnh hưởng và block inventory theo recall.', surfaces: ['Web', 'API'], status: 'planned', spec: '48, 79' },
      { id: 'QR-06', name: 'Scrap', goal: 'Approval + posting giảm tồn kho với reason/evidence/audit.', surfaces: ['Web', 'API'], status: 'planned', spec: '42, 62, 70' },
    ],
  },
  {
    key: 'handling-packaging',
    name: 'Handling Unit, Đóng gói & Nhãn',
    description: 'Quản lý pallet, carton, tote, HU hierarchy và label.',
    capabilities: [
      { id: 'HU-01', name: 'Handling Unit', goal: 'Tạo và theo dõi HU chứa inventory theo location/state.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '37, 216' },
      { id: 'HU-02', name: 'Pallet / Carton / Tote', goal: 'Đóng gói, merge/split và hierarchy HU.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '37' },
      { id: 'HU-03', name: 'Label Center', goal: 'In/preview barcode, lot, HU, shipping label và reprint audit.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '59, 64' },
      { id: 'HU-04', name: 'SSCC / Logistics Label', goal: 'Chuẩn hóa logistics identifier khi áp dụng.', surfaces: ['Web', 'API'], status: 'optional', spec: '64' },
    ],
  },
  {
    key: 'dock-yard-crossdock',
    name: 'Dock, Yard & Cross-dock',
    description: 'Điều phối phương tiện, dock door, staging và dòng hàng bỏ qua storage.',
    capabilities: [
      { id: 'DY-01', name: 'Gate Check-in', goal: 'Ghi nhận xe đến, appointment và kiểm tra điều kiện vào.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '46, 248' },
      { id: 'DY-02', name: 'Dock Scheduling', goal: 'Phân dock theo lịch, priority và capacity.', surfaces: ['Web', 'API'], status: 'planned', spec: '46, 60' },
      { id: 'DY-03', name: 'Yard Management', goal: 'Theo dõi vị trí trailer/vehicle trong yard.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '46, 248' },
      { id: 'DY-04', name: 'Cross-Docking', goal: 'Match inbound → outbound để giảm putaway/storage.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '47, 216' },
    ],
  },
  {
    key: 'reports-analytics',
    name: 'Báo cáo & Analytics',
    description: 'Read model/reporting không thay thế transactional truth.',
    capabilities: [
      { id: 'RP-01', name: 'Inventory Snapshot', goal: 'Báo cáo tồn kho theo sản phẩm/kho và export.', surfaces: ['Web', 'API'], status: 'live', spec: '82, 227', route: '/inventory' },
      { id: 'RP-02', name: 'Xuất - Nhập - Tồn', goal: 'Opening, in, out, closing theo kỳ.', surfaces: ['Web', 'API'], status: 'live', spec: '13, 82, 227', route: '/inventory' },
      { id: 'RP-03', name: 'Inbound / Outbound Performance', goal: 'Throughput, dwell time, backlog và SLA.', surfaces: ['Web'], status: 'planned', spec: '68, 90, 91, 227' },
      { id: 'RP-04', name: 'Inventory Aging / Expiry', goal: 'Aging, near-expiry, expired và slow-moving.', surfaces: ['Web'], status: 'planned', spec: '78, 187, 227' },
      { id: 'RP-05', name: 'Accuracy & Count Variance', goal: 'Inventory accuracy, recount rate và variance trend.', surfaces: ['Web'], status: 'planned', spec: '40, 82, 227' },
      { id: 'RP-06', name: 'Ledger Reconciliation', goal: 'So sánh ledger-derived balance với operational balance.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '29, 82, 155' },
      { id: 'RP-07', name: 'Productivity / Heatmap', goal: 'Task productivity, congestion, location utilization.', surfaces: ['Web'], status: 'planned', spec: '67, 81, 227' },
      { id: 'RP-08', name: 'Audit & Evidence Export', goal: 'Xuất gói bằng chứng phục vụ kiểm toán/compliance.', surfaces: ['Web', 'API'], status: 'planned', spec: '83, 280' },
      { id: 'RP-09', name: 'Inventory Turnover & Days on Hand', goal: 'Theo dõi stock turnover, days on hand, slow-moving/dead stock theo semantic KPI thống nhất.', surfaces: ['Web', 'API'], status: 'planned', spec: '68, 227' },
      { id: 'RP-10', name: 'BI Export & Analytical Platform', goal: 'Tách analytical workload khỏi OLTP qua CDC/staging/warehouse/lakehouse và giữ lineage về transactional truth.', surfaces: ['API', 'Worker'], status: 'planned', spec: '13, 250, 251' },
    ],
  },
  {
    key: 'administration',
    name: 'Quản trị & Bảo mật',
    description: 'Identity, RBAC, warehouse scope, permission registry, workflow, config, audit và SSO.',
    capabilities: [
      { id: 'AD-01', name: 'User Administration', goal: 'Tạo/khóa user, lifecycle và warehouse membership.', surfaces: ['Web', 'API'], status: 'foundation', spec: '17, 112, 113, 225, 276' },
      { id: 'AD-02', name: 'Role & Permission Matrix', goal: 'Permission code registry dùng chung backend/frontend/tests.', surfaces: ['Web', 'API'], status: 'foundation', spec: '17, 225', route: '/permissions' },
      { id: 'AD-03', name: 'Warehouse Scope', goal: 'Chặn cross-warehouse access dù biết record ID.', surfaces: ['API'], status: 'foundation', spec: '17, 99, 225' },
      { id: 'AD-04', name: 'Approval Workflow', goal: 'Workflow, SoD, threshold, SLA và escalation.', surfaces: ['Web', 'API'], status: 'live', spec: '70, 221', route: '/approvals' },
      { id: 'AD-05', name: 'Configuration & Feature Flags', goal: 'Quản lý config có scope và rollout an toàn.', surfaces: ['Web', 'API'], status: 'planned', spec: '20, 98, 276' },
      { id: 'AD-06', name: 'Audit Trail', goal: 'Audit hành động critical, trước/sau và actor/context.', surfaces: ['Web', 'API'], status: 'foundation', spec: '08, 17, 83, 280' },
      { id: 'AD-07', name: 'Notification Center & Alert Engine', goal: 'Thông báo task, approval, exception, SLA và integration failure.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '61, 92, 271' },
      { id: 'AD-08', name: 'SSO / Identity Federation', goal: 'OIDC/SAML/enterprise identity và provisioning.', surfaces: ['Web', 'API'], status: 'planned', spec: '112, 279' },
      { id: 'AD-09', name: 'Number Sequence & Document Numbering', goal: 'Sinh số chứng từ concurrency-safe theo company/warehouse/document type và policy immutability.', surfaces: ['Web', 'API'], status: 'planned', spec: '69, 276' },
      { id: 'AD-10', name: 'Business Rules & Workflow Configuration', goal: 'Cấu hình policy/workflow có scope, version, validation và audit mà không phá domain invariant.', surfaces: ['Web', 'API'], status: 'planned', spec: '51, 276' },
      { id: 'AD-11', name: 'Document / Attachment / Evidence', goal: 'Quản lý evidence an toàn cho receipt, QC, damage, POD, return, scrap, exception và audit.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '62, 83, 96' },
      { id: 'AD-12', name: 'Localization & Regional Format', goal: 'Language/date/time/number/unit presentation theo locale nhưng giữ canonical code/value trong API và database.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '177, 256' },
      { id: 'AD-13', name: 'Legal Hold & eDiscovery', goal: 'Bảo toàn audit/evidence/POD/integration records theo legal hold và tạo export traceable cho compliance.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '83, 257, 280' },
      { id: 'AD-14', name: 'Privacy Request & Data Subject Handling', goal: 'Xử lý privacy request mà không phá business/audit/legal records; mọi action phải có audit và authorization.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '116, 258' },
    ],
  },
  {
    key: 'integration',
    name: 'Tích hợp',
    description: 'Đồng bộ ERP/MDM, orders, carrier/e-commerce/TMS bằng inbox/outbox và hợp đồng idempotent.',
    capabilities: [
      { id: 'IG-01', name: 'Master Data Sync', goal: 'Product, UOM, Partner, Warehouse sync từ ERP/MDM.', surfaces: ['API', 'Worker'], status: 'planned', spec: '162' },
      { id: 'IG-02', name: 'Purchase Order Integration', goal: 'PO → expected inbound/ASN và cancel semantics.', surfaces: ['API', 'Worker'], status: 'planned', spec: '160' },
      { id: 'IG-03', name: 'Sales Order Integration', goal: 'SO → outbound order và status/event feedback.', surfaces: ['API', 'Worker'], status: 'planned', spec: '161' },
      { id: 'IG-04', name: 'Transactional Outbox', goal: 'Business mutation + event commit cùng transaction.', surfaces: ['Worker', 'API'], status: 'foundation', spec: '09, 19, 103, 107' },
      { id: 'IG-05', name: 'Integration Inbox', goal: 'Deduplicate inbound commands/events và retry an toàn.', surfaces: ['Worker', 'API'], status: 'planned', spec: '09, 75, 103, 107' },
      { id: 'IG-06', name: 'Carrier / E-commerce / TMS', goal: 'Shipping, tracking và fulfillment integration.', surfaces: ['API', 'Worker'], status: 'optional', spec: '163, 164, 165, 166, 169' },
      { id: 'IG-07', name: 'Dead-letter & Replay', goal: 'Quản lý integration failure, retry/replay có audit.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '75, 103' },
      { id: 'IG-08', name: 'Controlled Data Import & Bulk Operations', goal: 'Import/export/bulk operation có validation, preview, error report, authorization và audit.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '63, 76' },
      { id: 'IG-09', name: 'Finance / Costing / Valuation Integration Boundary', goal: 'Xuất quantity/movement truth cho Finance/Costing, period cutoff và valuation events mà không biến WMS thành accounting engine.', surfaces: ['API', 'Worker'], status: 'planned', spec: '150, 154, 156, 158, 159, 172' },
      { id: 'IG-10', name: 'Integration Reconciliation Dashboard & Control', goal: 'So sánh source control totals, WMS inbox/document/outbox và target acknowledgement; phát hiện mismatch và hỗ trợ safe retry.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '75, 171' },
    ],
  },
  {
    key: 'mobile',
    name: 'Mobile WMS',
    description: 'Scan-first execution cho operator, hỗ trợ offline/deferred sync theo policy.',
    capabilities: [
      { id: 'MO-01', name: 'Home & My Tasks', goal: 'Kho hiện tại, task priority/due và quick scan.', surfaces: ['Mobile'], status: 'planned', spec: '80, 223, 229' },
      { id: 'MO-02', name: 'Receiving Scan', goal: 'Scan product/barcode, quantity/UOM, lot/expiry/location.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '34, 65, 223, 229' },
      { id: 'MO-03', name: 'Putaway Scan', goal: 'Scan product/HU và location đích, confirm move.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '35, 65, 223, 229' },
      { id: 'MO-04', name: 'Picking Scan', goal: 'Scan location, product, serial và xác nhận quantity.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '36, 65, 223, 229' },
      { id: 'MO-05', name: 'Packing', goal: 'Scan item, pack, complete và label.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '37, 64, 65, 223, 229' },
      { id: 'MO-06', name: 'Count', goal: 'Location/product scan, blind count và recount.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '40, 65, 223, 229' },
      { id: 'MO-07', name: 'Transfer', goal: 'Create/scan/confirm transfer và receiving.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '39, 65, 223, 229' },
      { id: 'MO-08', name: 'Replenishment', goal: 'Create/scan/confirm replenish task.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '44, 65, 223, 229' },
      { id: 'MO-09', name: 'Returns', goal: 'Receive, inspect, disposition và post return.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '43, 65, 223, 229' },
      { id: 'MO-10', name: 'Offline Queue & Sync', goal: 'Cho phép task low-risk offline, retry và giữ dữ liệu khi sync lỗi.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '66, 146, 223, 229, 272' },
      { id: 'MO-11', name: 'Product Lookup', goal: 'Tra cứu nhanh Product/SKU bằng exact barcode, code hoặc tên trong warehouse scope; không cần mở flow nghiệp vụ dài.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '65, 95, 223' },
      { id: 'MO-12', name: 'Exception Handling', goal: 'Xem/assign/resolve/escalate exception từ mobile mà giữ task context, reason, evidence và sync semantics.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '71, 92, 223, 272' },
    ],
  },
  {
    key: 'operations-resilience',
    name: 'Vận hành & Resilience',
    description: 'Runbook, monitoring, backup/restore, DR, commissioning và recovery reconciliation.',
    capabilities: [
      { id: 'OP-01', name: 'Observability & Monitoring', goal: '500/409/412, deadlock, latency, outbox backlog và business integrity alerts.', surfaces: ['Web', 'Worker'], status: 'foundation', spec: '10, 121, 122, 123' },
      { id: 'OP-02', name: 'Warehouse Opening / Closing', goal: 'Health/inventory/integration check trước mở và đóng site.', surfaces: ['Web', 'API'], status: 'planned', spec: '148, 149' },
      { id: 'OP-03', name: 'Backup / Restore Drill', goal: 'Chứng minh recovery và bảo vệ ledger/audit/document transactions.', surfaces: ['Worker'], status: 'foundation', spec: '11, 142, 143' },
      { id: 'OP-04', name: 'Incident & Recovery Reconciliation', goal: 'Reconcile inventory trước khi warehouse reopen.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '140, 147, 151, 152' },
      { id: 'OP-05', name: 'Offline Warehouse Operations', goal: 'Deferred synchronization trong giới hạn an toàn.', surfaces: ['Mobile', 'Worker'], status: 'planned', spec: '145, 146, 272' },
      { id: 'OP-06', name: 'Warehouse Commissioning', goal: 'Checklist site mới trước go-live.', surfaces: ['Web'], status: 'planned', spec: '135, 264, 277' },
      { id: 'OP-07', name: 'Manual Contingency & Post-Outage Reconciliation', goal: 'Biểu mẫu dự phòng và nhập/reconcile sau outage.', surfaces: ['Web'], status: 'planned', spec: '145, 147, 281' },
      { id: 'OP-08', name: 'Feature Telemetry & UX Measurement', goal: 'Đo adoption, friction, error/latency và scan workflow regression mà không dùng product analytics làm business KPI truth.', surfaces: ['Web', 'Mobile', 'Worker'], status: 'planned', spec: '254, 263' },
      { id: 'OP-09', name: 'Warehouse Decommissioning & Tenant Offboarding', goal: 'Đóng warehouse/company/tenant an toàn với inventory zero/reconcile, integration stop, evidence retention và data exit.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '99, 265' },
      { id: 'OP-10', name: 'Production Data Correction & Controlled Repair', goal: 'Sửa dữ liệu production bằng reversal/corrected transaction hoặc controlled correction; không sửa posted ledger trực tiếp.', surfaces: ['Web', 'API'], status: 'planned', spec: '127' },
      { id: 'OP-11', name: 'Support & Administrative Tooling', goal: 'Cung cấp support tooling an toàn qua Application Layer, audit và permission thay vì truy cập DB trực tiếp thường xuyên.', surfaces: ['Web', 'API'], status: 'planned', spec: '128, 153' },
    ],
  },
  {
    key: 'advanced-planning',
    name: 'Advanced WMS & Planning',
    description: 'Capability nâng cao chỉ bật khi có nhu cầu/ngành/customer evidence.',
    capabilities: [
      { id: 'AX-01', name: 'Slotting Optimization', goal: 'Đề xuất slot theo velocity, compatibility và travel cost.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '49' },
      { id: 'AX-02', name: 'Labor / Workload Planning', goal: 'Lập kế hoạch nguồn lực theo task volume và shift capacity.', surfaces: ['Web'], status: 'optional', spec: '50, 60, 80, 81' },
      { id: 'AX-03', name: 'Multi-Warehouse Network Planning', goal: 'DC/hub/spoke role, product eligibility và network quantity.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '178, 182, 183' },
      { id: 'AX-04', name: 'Capacity Forecasting', goal: 'Dự báo congestion theo compatible storage capacity.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '194' },
      { id: 'AX-05', name: '3PL / Multi-owner / Billing', goal: 'Owner-aware inventory và warehouse service billing reconciliation.', surfaces: ['Web', 'API'], status: 'optional', spec: '54, 55, 56, 173, 245, 246' },
      { id: 'AX-06', name: 'RFID / Voice / Pick-to-Light / Robotics', goal: 'Warehouse automation theo feature applicability.', surfaces: ['Mobile', 'API', 'Worker'], status: 'optional', spec: '235, 236, 237' },
      { id: 'AX-07', name: 'Cold-chain / Hazmat / Catch-weight', goal: 'Specialized inventory rules khi ngành yêu cầu.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '240, 241, 242' },
      { id: 'AX-08', name: 'Equipment & Material Handling Assets', goal: 'Quản lý forklift, scanner, printer, cart/conveyor endpoint, assignment, eligibility và maintenance khi áp dụng.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '59' },
      { id: 'AX-09', name: 'Kitting / Bundling / Assembly / De-kitting', goal: 'Light-manufacturing warehouse flow theo component allocation, pick, assembly, QC optional và transformation posting.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '52, 243' },
      { id: 'AX-10', name: 'Value Added Services (VAS)', goal: 'Thực thi labeling/repack/custom service theo order/task có traceability và billing hooks khi khách hàng yêu cầu.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '53, 244' },
      { id: 'AX-11', name: 'Cartonization / Cubing / Packing Optimization', goal: 'Đề xuất carton/tote/pallet theo dimension, weight, packaging rule và carrier constraint; không tự xác nhận PACKED.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '238' },
      { id: 'AX-12', name: 'Load Planning & Trailer Utilization', goal: 'Tối ưu staging-to-load, trailer/container capacity và loading sequence nhưng không tự tạo inventory effect.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'optional', spec: '239' },
      { id: 'AX-13', name: 'Advanced Reverse Logistics / Refurbishment / RTV', goal: 'Mở rộng return disposition sang refurbish, repair, RTV, scrap và reverse-logistics execution theo customer evidence.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '43, 247' },
      { id: 'AX-14', name: 'Warehouse Safety / Ergonomics / Risk Control', goal: 'Áp restricted zone, equipment qualification, heavy-lift threshold, one-way aisle và high-risk acknowledgement vào task/rule layer.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '249' },
      { id: 'AX-15', name: 'WCS / WES / Robotics Integration', goal: 'Trace WMS Task → Device Job → Device Event → WMS Command mà không giao transactional inventory truth cho automation layer.', surfaces: ['API', 'Worker'], status: 'optional', spec: '235' },
      { id: 'AX-16', name: 'RFID / IoT Sensor & Real-Time Capture', goal: 'Thu nhận RFID/sensor/device event có dedupe, timestamp, device identity và traceability về WMS context.', surfaces: ['Mobile', 'API', 'Worker'], status: 'optional', spec: '236' },
      { id: 'AX-17', name: 'Voice Picking / Pick-to-Light / Assisted Picking', goal: 'Hỗ trợ operator bằng voice/light/device guidance nhưng vẫn dùng cùng task, permission, scan/idempotency semantics.', surfaces: ['Mobile', 'API', 'Worker'], status: 'optional', spec: '237' },
      { id: 'AX-18', name: 'Hazardous Materials & Restricted Storage', goal: 'Quản lý compatibility, restricted zone, segregation và handling policy cho dangerous goods khi ngành yêu cầu.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '240' },
      { id: 'AX-19', name: 'Cold Chain & Temperature Excursion', goal: 'Theo dõi temperature-controlled inventory, excursion, evidence và quarantine/disposition policy.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'optional', spec: '241' },
      { id: 'AX-20', name: 'Catch Weight & Dual-UOM Inventory', goal: 'Quản lý quantity song song Base UOM và variable-weight UOM với conversion/tolerance và ledger traceability.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '242' },
      { id: 'AX-21', name: 'Consignment / Vendor-Owned / Customer-Owned Inventory', goal: 'Quản lý owner dimension, ownership eligibility và explicit reclassification cho consignment/multi-owner stock.', surfaces: ['Web', 'API'], status: 'optional', spec: '55, 73, 245' },
      { id: 'AX-22', name: '3PL Contract / Rating / Warehouse Billing', goal: 'Quản lý service contract, rating rule, billable activity và reconciliation với operational evidence.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '56, 173, 246' },
      { id: 'AX-23', name: 'ATP / CTP & Promise Date', goal: 'Tính Available-to-Promise/Capable-to-Promise và promise date cho OMS/Sales mà không tạo reservation ngầm.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '89' },
      { id: 'AX-24', name: 'Network Inventory Balancing', goal: 'Phát hiện surplus/shortage và đề xuất cân bằng tồn giữa kho dựa trên demand, availability, safety stock và transfer economics.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '179' },
      { id: 'AX-25', name: 'Inter-Warehouse Replenishment Planning', goal: 'Lập kế hoạch replenishment giữa các kho nhưng không bypass Reservation/Allocation/Transfer semantics.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '180' },
      { id: 'AX-26', name: 'Order Routing & Fulfillment Node Selection', goal: 'Chọn fulfillment node theo eligibility, availability, SLA, capacity và score giải thích được.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '181' },
      { id: 'AX-27', name: 'Forecast & Demand Signal Integration', goal: 'Nhận forecast/demand signals từ ERP/OMS/planning vào staging/read model mà không biến forecast thành inventory truth.', surfaces: ['API', 'Worker'], status: 'optional', spec: '184' },
      { id: 'AX-28', name: 'Safety Stock & Reorder Policy', goal: 'Quản lý safety stock, reorder point, min/max theo SKU/Warehouse/Owner và projected availability.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '185' },
      { id: 'AX-29', name: 'ABC / XYZ Inventory Classification', goal: 'Phân loại ABC/XYZ phục vụ slotting, cycle count, replenishment và optimization với versioned calculation.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '186' },
      { id: 'AX-30', name: 'Inventory Optimization Policy', goal: 'Kết hợp demand, safety stock, ABC/XYZ, expiry, capacity và network data để đề xuất policy tồn tối ưu.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '188' },
      { id: 'AX-31', name: 'Network Rebalancing Simulation', goal: 'Mô phỏng surplus/shortage, transfer candidates và constraints trên immutable snapshot trước khi người dùng quyết định.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '189' },
      { id: 'AX-32', name: 'Forecast Accuracy & Bias Measurement', goal: 'Đo forecast accuracy/bias theo grain và segment để planning team đánh giá chất lượng dự báo.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '190' },
      { id: 'AX-33', name: 'Replenishment Exception Management', goal: 'Triage overdue/no-source/capacity/policy conflict để tránh silent stockout hoặc task spam.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '191' },
      { id: 'AX-34', name: 'Procurement Suggestion Engine', goal: 'Sinh đề xuất mua khi projected available xuống dưới policy threshold; chỉ gửi ERP khi được approve.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '192' },
      { id: 'AX-35', name: 'Inventory Risk Scoring', goal: 'Tính risk score explainable cho stockout, expiry, accuracy, congestion và supply uncertainty mà không tạo hidden mutation.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '193' },
      { id: 'AX-36', name: 'Supplier Lead-Time Intelligence', goal: 'Đo planned/actual lead time, variability, percentile và supplier service reliability.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '196' },
      { id: 'AX-37', name: 'Purchase Expedite / Defer Recommendation', goal: 'Đề xuất expedite/defer/split/keep inbound supply dựa trên demand, projected inventory và supplier capability.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '197' },
      { id: 'AX-38', name: 'Demand Spike & Anomaly Detection', goal: 'Phát hiện spike/drop/data anomaly trước khi chúng làm sai replenishment và planning.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '198' },
      { id: 'AX-39', name: 'Inventory Allocation Fairness Policy', goal: 'Tạo allocation proposal deterministic theo fairness policy mà không bypass atomic reservation/allocation.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '86, 199' },
      { id: 'AX-40', name: 'Service-Level Segmentation', goal: 'Quản lý service class dùng chung cho fulfillment, safety stock, replenishment, allocation và SLA analytics.', surfaces: ['Web', 'API'], status: 'optional', spec: '87, 200' },
      { id: 'AX-41', name: 'Scenario Planning & What-if Simulation', goal: 'Chạy what-if trên immutable snapshot; scenario data tuyệt đối không mutate production master/document/ledger.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '201' },
      { id: 'AX-42', name: 'Decision Policy Registry & Human-in-the-Loop', goal: 'Version hóa policy ảnh hưởng planning/execution, scope/effective dates/owner và yêu cầu human approval ở decision boundary.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '202, 203' },
    ],
  },
];

export const blueprintTotals = erpWmsBlueprint.reduce(
  (acc, module) => {
    acc.modules += 1;
    acc.capabilities += module.capabilities.length;
    module.capabilities.forEach((capability) => {
      acc[capability.status] += 1;
    });
    return acc;
  },
  { modules: 0, capabilities: 0, live: 0, foundation: 0, planned: 0, optional: 0 } as Record<'modules' | 'capabilities' | BlueprintStatus, number>,
);

export const findBlueprintModule = (key?: string) =>
  erpWmsBlueprint.find((module) => module.key === key);
