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
  mockRoute?: string;
}

export interface BlueprintModule {
  key: string;
  name: string;
  description: string;
  flow?: string[];
  capabilities: BlueprintCapability[];
}

export const blueprintProductionSnapshot = {
  branch: 'feature/erp-wms-complete-ui-blueprint',
  commit: 'b9d926ef8d068b57d7ec26e7148e031a685fe477',
  deployment: 'erp-wms-blueprint-demo.vercel.app',
  deploymentId: 'dpl_Ha1vNBRkxFyhuHp3tk3BkvS4AeYS',
  verifiedAt: '2026-10-08',
  notionPolicy: 'canonical-read-only',
} as const;

export const blueprintStatusLabels: Record<BlueprintStatus, string> = {
  live: 'Đã triển khai hoàn thiện phạm vi hiện tại',
  foundation: 'Đã triển khai một phần / còn thiếu phạm vi',
  planned: 'Chưa triển khai hệ thống thật',
  optional: 'Nâng cao / bật theo nhu cầu',
};

export const blueprintDemoStatusLabels: Record<BlueprintStatus, string> = {
  live: 'Có mô phỏng + hệ thống thật đã hoàn thiện phạm vi hiện tại',
  foundation: 'Có mô phỏng + hệ thống thật đã triển khai một phần',
  planned: 'Có mô phỏng tương tác • hệ thống thật chưa triển khai',
  optional: 'Có mô phỏng nâng cao • bật khi cần',
};

export const erpWmsBlueprint: BlueprintModule[] = [
  {
    key: 'overview',
    name: 'Tổng quan & Trung tâm công việc',
    description: 'Điểm vào chung cho quản lý công việc, phê duyệt, ngoại lệ và sức khỏe vận hành.',
    flow: ['Bảng điều khiển', 'Nhiệm vụ của tôi', 'Hàng đợi nhiệm vụ', 'Phê duyệt', 'Ngoại lệ', 'Tìm kiếm toàn hệ thống', 'Trung tâm điều hành'],
    capabilities: [
      { id: 'OV-01', name: 'Bảng điều khiển vận hành', goal: 'KPI nhập kho, xuất kho, tồn kho, tồn đọng, SLA và cảnh báo.', surfaces: ['Web'], status: 'foundation', spec: '68, 216, 227', route: '/' },
      { id: 'OV-02', name: 'Nhiệm vụ của tôi', goal: 'Tập hợp nhiệm vụ được giao theo kho, độ ưu tiên và hạn xử lý.', surfaces: ['Web', 'Mobile'], status: 'planned', spec: '80, 216, 223' },
      { id: 'OV-03', name: 'Trung tâm phê duyệt', goal: 'Duyệt/từ chối nghiệp vụ có SoD, SLA và dấu vết kiểm toán.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 70, 221', route: '/approvals' },
      { id: 'OV-04', name: 'Trung tâm ngoại lệ', goal: 'Phân loại, giao người xử lý, giải quyết và đóng ngoại lệ theo quy trình.', surfaces: ['Web', 'API'], status: 'planned', spec: '71, 92, 221' },
      { id: 'OV-05', name: 'Trung tâm điều hành kho phân tán', goal: 'Theo dõi nhiều kho theo mô hình vận hành ưu tiên ngoại lệ.', surfaces: ['Web'], status: 'planned', spec: '183, 195' },
      { id: 'OV-06', name: 'Bộ máy nhiệm vụ kho & hàng đợi công việc', goal: 'Chuẩn hóa RECEIVE/PUTAWAY/PICK/PACK/MOVE/REPLENISH/COUNT/LOAD/QC/RETURN/EXCEPTION/VAS thành nhiệm vụ dùng chung cho web/di động.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'planned', spec: '80' },
      { id: 'OV-07', name: 'Tìm kiếm toàn doanh nghiệp', goal: 'Ưu tiên khớp chính xác khi tìm xuyên Sản phẩm, Mã vạch, Đối tác, Chứng từ, Lô/Sê-ri, Vị trí, Lô giao hàng và Nhiệm vụ theo phạm vi bảo mật.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'planned', spec: '95, 216' },
      { id: 'OV-08', name: 'Dòng hoạt động', goal: 'Mô hình đọc hợp nhất kiểm toán/sự kiện nghiệp vụ/thông báo để người dùng xem hoạt động gần đây theo quyền và phạm vi kho.', surfaces: ['Web', 'API'], status: 'planned', spec: '61, 83, 216, 271' },
    ],
  },
  {
    key: 'master-data',
    name: 'Dữ liệu danh mục',
    description: 'Nguồn dữ liệu chuẩn cho sản phẩm, mã vạch, UOM, đối tác và các danh mục dùng chung.',
    capabilities: [
      { id: 'MD-01', name: 'Sản phẩm / SKU', goal: 'CRUD sản phẩm, trạng thái, thuộc tính và tracking policy.', surfaces: ['Web', 'API'], status: 'live', spec: '16, 84, 162', route: '/products' },
      { id: 'MD-02', name: 'Danh mục sản phẩm', goal: 'Phân loại sản phẩm theo category và cấu trúc tìm kiếm.', surfaces: ['Web', 'API'], status: 'live', spec: '84, 162', route: '/products' },
      { id: 'MD-03', name: 'Đa mã vạch', goal: 'Quản lý nhiều barcode và exact lookup cho scan.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '65, 84, 162', route: '/products' },
      { id: 'MD-04', name: 'Đơn vị tính & quy đổi', goal: 'Quản lý UOM và quy đổi về Base UOM cho mọi quantity entry.', surfaces: ['Web', 'API'], status: 'live', spec: '16, 84, 220', route: '/units' },
      { id: 'MD-05', name: 'Đối tác kinh doanh', goal: 'Supplier/Customer dùng chung, role, active state và snapshot trên chứng từ.', surfaces: ['Web', 'API'], status: 'live', spec: '84, 162', route: '/business-partners' },
      { id: 'MD-06', name: 'Đơn vị vận chuyển', goal: 'Danh mục đơn vị vận chuyển và điều kiện tích hợp.', surfaces: ['Web', 'API'], status: 'planned', spec: '58, 84, 164' },
      { id: 'MD-07', name: 'Danh mục mã lý do', goal: 'Chuẩn hóa lý do điều chỉnh, hủy, exception, damage, scrap.', surfaces: ['Web', 'API'], status: 'planned', spec: '20, 276' },
      { id: 'MD-08', name: 'Loại bao bì', goal: 'Danh mục carton/tote/pallet/packaging profile dùng cho packing, HU và cartonization khi áp dụng.', surfaces: ['Web', 'API'], status: 'planned', spec: '37, 238' },
      { id: 'MD-09', name: 'Hợp đồng SLA Khách hàng / Nhà cung cấp / Đơn vị vận chuyển', goal: 'Quản lý SLA contract dùng chung cho supplier, customer, carrier và 3PL client để KPI/event có cùng semantic.', surfaces: ['Web', 'API'], status: 'planned', spec: '174' },
    ],
  },
  {
    key: 'warehouse-structure',
    name: 'Kho & Vị trí',
    description: 'Mô hình vật lý từ warehouse tới bin, capacity, calendar, dock và yard.',
    capabilities: [
      { id: 'WH-01', name: 'Kho', goal: 'Quản lý kho, scope truy cập, timezone và trạng thái vận hành.', surfaces: ['Web', 'API'], status: 'live', spec: '16, 60, 148', route: '/warehouses' },
      { id: 'WH-02', name: 'Khu / Dãy / Kệ / Tầng / Ô', goal: 'Cấu trúc vị trí lưu trữ nhiều cấp.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '02, 16, 17, 67, 84, 216, 282', route: '/warehouse-structure', mockRoute: '/system-blueprint/warehouse-structure/WH-02/workbench' },
      { id: 'WH-03', name: 'Sức chứa & Ràng buộc lưu trữ', goal: 'Kiểm tra sức chứa và compatibility trước putaway/move.', surfaces: ['Web', 'API'], status: 'live', spec: '85, 194', route: '/warehouse-structure' },
      { id: 'WH-04', name: 'Bản đồ kho & Bản đồ nhiệt', goal: 'Hiển thị bản đồ kho, utilization, congestion và operational heatmap.', surfaces: ['Web'], status: 'live', spec: '67', route: '/warehouse-map' },
      { id: 'WH-05', name: 'Lịch kho & Ca làm việc', goal: 'Lịch mở cửa, ca làm việc, cutoff và capacity vận hành.', surfaces: ['Web', 'API'], status: 'live', spec: '60', route: '/warehouse-calendar' },
      { id: 'WH-06', name: 'Cửa kho & Sân bãi', goal: 'Quản lý dock door, yard position, check-in và queue.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '46, 248', route: '/dock-yard' },
      { id: 'WH-07', name: 'Ngoại lệ lịch vận hành', goal: 'Quản lý company/warehouse/shift calendar exception và emergency override với precedence rõ ràng.', surfaces: ['Web', 'API'], status: 'planned', spec: '60, 175, 176' },
    ],
  },
  {
    key: 'inbound',
    name: 'Nhập kho',
    description: 'Luồng từ expected inbound tới receiving, QC, posting và putaway.',
    flow: ['PO/ASN', 'Appointment', 'Receipt', 'Receiving', 'QC', 'Ready to Post', 'Post', 'Putaway'],
    capabilities: [
      { id: 'IN-01', name: 'Đơn mua / ASN', goal: 'Quản lý expected inbound từ Purchase Order và ASN; production work center hiện có tại /purchase-orders và /asns.', surfaces: ['Web', 'API'], status: 'live', spec: '01, 03, 17, 34, 160, 228', route: '/purchase-orders' },
      { id: 'IN-02', name: 'Lịch nhận hàng', goal: 'Dock/Yard appointment, arrival/check-in và phân dock đã có; liên kết canonical từ appointment tới PO/ASN/Receipt vẫn đang hoàn thiện.', surfaces: ['Web', 'API'], status: 'foundation', spec: '03, 46, 248', route: '/dock-yard' },
      { id: 'IN-03', name: 'Trung tâm công việc phiếu nhập', goal: 'Tìm/lọc/tạo/mở receipt theo state và warehouse scope.', surfaces: ['Web', 'API'], status: 'live', spec: '34, 216, 228, 229', route: '/import-receipts' },
      { id: 'IN-04', name: 'Bàn làm việc tiếp nhận', goal: 'Nhận hàng theo quantity/UOM snapshot, ghi nhận observed quantity và handoff sang discrepancy/QC.', surfaces: ['Web', 'API'], status: 'live', spec: '34, 228, 229', route: '/import-receipts' },
      { id: 'IN-05', name: 'Chênh lệch nhận thừa/thiếu', goal: 'Ghi observation version, typed resolution, approval/reject/recount và custody boundary trước QC/Post.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 34, 70, 71, 228, 229', route: '/import-receipts' },
      { id: 'IN-06', name: 'QC hàng nhập', goal: 'QC theo line nằm trong Goods Receipt: bắt buộc QC/no-QC, disposition và readiness trước Receipt POST; đây không phải QC Work Center độc lập xuyên flow.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 34, 41, 57, 62, 90, 228', route: '/import-receipts' },
      { id: 'IN-07', name: 'Ghi sổ phiếu nhập', goal: 'Boundary tăng tồn duy nhất của receipt; ghi location-aware stock/ledger và tạo Cất hàng atomically.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 29, 34, 35, 228', route: '/import-receipts' },
      { id: 'IN-08', name: 'Nhiệm vụ cất hàng', goal: 'Phân công, bắt đầu, move, exception, resume, cancel và hoàn tất cất hàng sau Receipt POST.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 35, 228, 229', route: '/putaway-tasks' },
      { id: 'IN-09', name: 'Bộ máy quy tắc cất hàng', goal: 'Đề xuất vị trí dựa trên capacity, status, owner và product rule.', surfaces: ['Web', 'API'], status: 'planned', spec: '35, 85' },
    ],
  },
  {
    key: 'outbound',
    name: 'Xuất kho',
    description: 'Luồng từ order tới reservation, allocation, picking, packing, loading và dispatch.',
    flow: ['Order', 'Release', 'Reserve', 'Allocate', 'Pick', 'Pack', 'Stage', 'Load', 'Dispatch', 'Track / POD'],
    capabilities: [
      { id: 'OUT-01', name: 'Phiếu xuất MVP', goal: 'Draft → Approved/Reserved → Dispatched; approval chỉ giữ hàng, dispatch mới trừ OnHand và ghi ledger theo Location thực tế. Compatibility approve-and-dispatch là lệnh riêng có kiểm soát; ExportReceipt Dispatched không đồng nghĩa Shipment LOADED/DISPATCHED.', surfaces: ['Web', 'API'], status: 'live', spec: '17 §34, 30 §20, 38 §14, 228, 229', route: '/export-receipts' },
      { id: 'OUT-02', name: 'Giữ hàng', goal: 'Giữ quantity khả dụng theo product/warehouse/source, có expiry/release/consume và reconciliation; chưa phải Allocation vì chưa gắn reservation vào location/lot/serial cụ thể.', surfaces: ['Web', 'API'], status: 'live', spec: '30, 72', route: '/stock-reservations' },
      { id: 'OUT-03', name: 'Phân bổ tồn', goal: 'Foundation production đã gắn reserved quantity vào Location pickable cụ thể, hỗ trợ manual/auto deterministic theo Location, release/reallocate và consume reservation theo bucket. Lot/Serial/Owner, FEFO/FIFO và policy engine nâng cao chưa hoàn tất nên chưa phải full Allocation Engine.', surfaces: ['Web', 'API'], status: 'foundation', spec: '17, 18, 25, 30, 86, 228, 229', route: '/stock-allocations' },
      { id: 'OUT-04', name: 'Đợt / Lô / Cụm', goal: 'Nhóm order/pick task để tối ưu thực thi.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '45' },
      { id: 'OUT-05', name: 'Lấy hàng', goal: 'Foundation production tự sinh Picking task khi source đã Allocation đầy đủ, hỗ trợ assign/start, scan location + product + quantity, Short Pick với alternative-location/backorder/cancel/supervisor override và completion; Picking không trừ OnHand. Lot/Serial canonical, destination Tote/HU và Replenishment chưa hoàn tất nên chưa phải full Picking Engine.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '31, 36, 65, 72, 223, 228', route: '/picking-tasks' },
      { id: 'OUT-06', name: 'Đóng gói', goal: 'Foundation production tự sinh Packing session khi Picking hoàn tất; hỗ trợ create carton/tote/pallet/container HU, scan product + quantity theo PickedQuantity thực tế, close/cancel HU rỗng, nested HU chống circular và xác nhận PACKED/CLOSED. Packing bảo toàn quantity và không trừ OnHand; downstream Shipment staging/loading đã chuyển sang OUT-07 foundation. Lot/Serial canonical, split/merge/repack/reopen, label printing và cartonization optimization chưa hoàn tất.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '17, 18, 37, 64, 238, 228', route: '/packing-sessions' },
      { id: 'OUT-07', name: 'Khu chờ & Xếp hàng', goal: 'Foundation production tự sinh Shipment READY khi Packing đạt PACKED; STAGE xác nhận staging location và chuyển toàn bộ HU hierarchy sang STAGED, START LOADING yêu cầu outbound dock appointment cùng kho ở IN_SERVICE, scan root HU chuyển cả hierarchy sang LOADED và COMPLETE LOADING lưu dock/vehicle/trailer/seal context. Staging/Loading không trừ OnHand; downstream canonical Dispatch/SHIP ledger đã chuyển sang OUT-08 foundation. Staging-capacity/load-planning optimization và carrier/POD chưa hoàn tất.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '38, 46, 239', route: '/shipments' },
      { id: 'OUT-08', name: 'Xác nhận giao hàng', goal: 'Foundation production chỉ cho canonical Shipment LOADED dispatch; xác thực root-HU hierarchy, Picking/Allocation/Reservation và bucket Location/InventoryStatus, consume Allocation + Reservation, chuyển HU sang SHIPPED, ghi SHIP ledger location-aware và giảm OnHand đúng một lần trong transaction Serializable. Nếu Shipment đã tồn tại thì ExportReceipt legacy dispatch bị chặn để tránh double movement; Shipment dispatch đồng bộ ExportReceipt nguồn sang DISPATCHED. Reconciliation và inventory in/out report đã tính TransactionType.Ship. Post-dispatch carrier/POD, reversal và delivery lifecycle chưa hoàn tất.', surfaces: ['Web', 'API'], status: 'foundation', spec: '29, 31, 38, 228', route: '/shipments' },
      { id: 'OUT-09', name: 'Đơn thiếu hàng', goal: 'Foundation production bổ sung Sales Order demand DRAFT/HOLD → RELEASED; release không đổi OnHand mà reserve + auto-allocate phần Available, tạo Backorder cho phần thiếu, và có thể recover trước khi Picking bắt đầu bằng cách tăng Reservation/Allocation và mở rộng Picking task Open/Assigned. Workbench chiếu Ordered/Reserved/Allocated/Picked/Shipped/Backorder/Cancelled từ execution canonical và cancellation không hoàn tồn hàng đã dispatched. Foundation hiện giới hạn một dòng mỗi SKU; Sales Order update/close/amendment, multi-shipment recovery sau khi Picking bắt đầu, SLA ageing/prioritization và OMS integration chưa hoàn tất.', surfaces: ['Web', 'API'], status: 'foundation', spec: '72, 87, 161', route: '/backorders' },
      { id: 'OUT-10', name: 'Theo dõi giao hàng / POD / Giao thất bại', goal: 'Foundation production mở rộng canonical Shipment sau DISPATCHED với internal/manual IN_TRANSIT, DELIVERY_FAILED, retry delivery, DELIVERED + POD metadata, COMPLETED và RETURN_TO_WAREHOUSE initiation; mọi transition sau Dispatch không tạo thêm SHIP ledger và không cộng/trừ OnHand lần hai. Carrier/TMS assignment/booking, authenticated webhook + source-event dedupe/out-of-order ingestion, attachment upload/storage, return receipt linkage và delivery SLA/claim vẫn chưa hoàn tất.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '58, 74, 164', route: '/shipments' },
    ],
  },
  {
    key: 'inventory-control',
    name: 'Tồn kho & Kiểm soát tồn kho',
    description: 'Nguồn sự thật vận hành: sổ cái bất biến, số dư dự phóng, khả dụng và trạng thái tồn kho.',
    capabilities: [
      { id: 'INV-01', name: 'Trình duyệt tồn kho', goal: 'Hệ thống thật hiện có trình duyệt Tồn thực tế/Đã giữ/Khả dụng theo sản phẩm/kho và nhóm tồn đa chiều Vị trí + Trạng thái tồn kho + Lô + Sê-ri + Hạn dùng, kèm biến động sổ cái và báo cáo xuất/nhập. Tổng hợp theo chủ sở hữu/hàng đang vận chuyển, đồ thị phả hệ và tổng hợp nhiều chủ sở hữu vẫn chưa hoàn chỉnh.', surfaces: ['Web', 'API'], status: 'foundation', spec: '29, 41, 73, 77, 78, 82, 88, 222', route: '/inventory' },
      { id: 'INV-02', name: 'Sổ cái tồn kho bất biến', goal: 'InventoryTransaction hiện lưu biến động với tham chiếu nguồn, loại giao dịch, vị trí, Trạng thái tồn kho, Lô/Sê-ri và trạng thái trước→sau cho các luồng đã triển khai; đổi trạng thái là một sự kiện sổ cái riêng và Shipment SHIP giữ các chiều truy vết. Quản trị đảo/hiệu chỉnh và độ phủ mọi loại biến động tương lai vẫn chưa hoàn chỉnh.', surfaces: ['Web', 'API'], status: 'foundation', spec: '28, 29, 32', route: '/inventory' },
      { id: 'INV-03', name: 'Dự phóng số dư tồn kho', goal: 'InventoryStock hiện là số dư/dự phóng vận hành dùng cho truy vấn; đã có đối chiếu với sổ cái nhưng chưa triển khai tiến trình rebuild có kiểm soát từ sổ cái.', surfaces: ['API', 'Worker'], status: 'foundation', spec: '29, 82' },
      { id: 'INV-04', name: 'Bộ máy tính khả dụng tồn kho', goal: 'Hệ thống thật tính khả dụng từ nhóm tồn đủ điều kiện: trạng thái cho phép giữ/phân bổ/lấy/giao, vị trí đang hoạt động/không bị chặn/có thể lấy, hạn dùng còn hiệu lực và Tồn thực tế - Đã giữ; giữ hàng + phân bổ cùng áp dụng FEFO cho lô có hạn dùng. Chính sách nâng cao theo chủ sở hữu/khóa và thời hạn sử dụng tối thiểu còn lại theo khách hàng/kênh vẫn chưa hoàn chỉnh.', surfaces: ['Web', 'API'], status: 'foundation', spec: '30, 41, 77, 88', route: '/inventory' },
      { id: 'INV-05', name: 'Trạng thái tồn kho', goal: 'Nền tảng hệ thống thật có bộ trạng thái chuẩn AVAILABLE/QC_HOLD/QUARANTINE/BLOCKED/DAMAGED/REJECTED/EXPIRED/RECALL_BLOCKED cùng cờ điều kiện dùng xuyên giữ hàng/phân bổ/lấy hàng/giao hàng. /inventory hiển thị trạng thái nhóm tồn và đổi trạng thái bảo toàn tổng Tồn thực tế, ghi sổ cái STATUS_CHANGE trước→sau, lý do + kiểm toán + idempotency; lượng đã giữ/phân bổ không thể bị đổi trạng thái. Mở chung từ QUARANTINE/DAMAGED/REJECTED/EXPIRED/RECALL_BLOCKED về AVAILABLE bị chặn vì quy trình disposition/giải phóng thu hồi có kiểm soát chưa hoàn tất.', surfaces: ['Web', 'API'], status: 'foundation', spec: '41, 222, 229', route: '/inventory' },
      { id: 'INV-06', name: 'Lô / Sê-ri / Hạn dùng', goal: 'Nền tảng hệ thống thật cấu hình Product Tracking None/Lot/Serial + Expiry/ShelfLife, khai báo Lô/Sê-ri/Hạn dùng theo disposition phiếu nhập trước POST, bảo vệ metadata lô và chống trùng sê-ri, mỗi sê-ri = 1 Base UOM, loại lô hết hạn khỏi giữ/phân bổ và giữ FEFO xuyên giữ→phân bổ. Quét lấy hàng xác thực lô/sê-ri, Shipment/sổ cái giữ chiều truy vết và /inventory tra cứu nhóm tồn theo lô/sê-ri/hạn dùng. Phả hệ/điều phối thu hồi đầy đủ, giải phóng thu hồi có kiểm soát, độ sâu quét GS1/di động và chính sách thời hạn sử dụng tối thiểu còn lại vẫn chưa hoàn tất.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '48, 65, 78, 222', route: '/inventory' },
      { id: 'INV-07', name: 'Khóa / đóng băng tồn kho', goal: 'Nền tảng hệ thống thật có Khóa tồn kho theo Kho / Vị trí / Sản phẩm / Trạng thái tồn kho / Lô / Sê-ri với Đóng băng kiểm kê, Giữ do chất lượng/điều tra/thu hồi, Đóng băng bảo trì và Khóa vận hành thủ công. Khóa đang hiệu lực được kiểm tra lại trong các thao tác thay đổi tồn đã nối như giữ/tiêu thụ, phân bổ, lấy hàng, cất hàng, đổi trạng thái và di chuyển nội bộ; các khóa chồng lấp giữ hiệu lực độc lập và mở một khóa không gỡ khóa khác. Khóa theo chủ sở hữu/HU/một phần số lượng, override đặc quyền, chính sách tự đóng băng kiểm kê chu kỳ và cổng khóa cho mọi loại biến động cũ/tương lai vẫn chưa hoàn tất.', surfaces: ['Web', 'API'], status: 'foundation', spec: '31, 40, 77', route: '/inventory-locks' },
      { id: 'INV-08', name: 'Di chuyển vị trí nội bộ', goal: 'Nền tảng hệ thống thật di chuyển tồn chưa được giữ giữa các vị trí trong cùng Kho bằng transaction Serializable, giữ Trạng thái tồn kho / Lô / Sê-ri, kiểm tra khóa nguồn + đích, Nhóm lưu trữ và sức chứa, rồi ghi biến động vị trí + sổ cái MOVE trước→sau + kiểm toán/idempotency mà vẫn bảo toàn tổng số lượng. Di chuyển xuyên kho tiếp tục thuộc Điều chuyển kho; di chuyển lượng đã giữ, di chuyển theo Owner/HU và đảo có kiểm soát vẫn chưa hoàn tất.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '29, 222, 282', route: '/inventory-movements' },
      { id: 'INV-09', name: 'Đảo giao dịch', goal: 'Nền tảng hệ thống thật hỗ trợ đảo giao dịch có kiểm soát cho Di chuyển vị trí nội bộ và Đổi trạng thái tồn kho: không sửa/xóa sổ cái gốc, tạo giao dịch hiệu chỉnh + dấu mốc đảo có liên kết cấu trúc Gốc↔Hiệu chỉnh↔Dấu đảo, có ràng buộc duy nhất ở cơ sở dữ liệu để chống đảo lặp, lưu lý do/kiểm toán/quyền/phạm vi kho. Đảo giao dịch của Receipt/Shipment/Transfer/Adjustment/Return/Scrap vẫn phải đi qua quy trình nghiệp vụ sở hữu; chính sách phụ thuộc downstream và độ phủ hiệu chỉnh cho mọi loại giao dịch chưa hoàn tất.', surfaces: ['Web', 'API'], status: 'foundation', spec: '32', route: '/inventory-reversals' },
      { id: 'INV-10', name: 'Truy vết & phả hệ tồn kho', goal: 'Nền tảng hệ thống thật hỗ trợ truy theo Sản phẩm, Lô, Sê-ri hoặc Tham chiếu trong phạm vi kho được phép, hiển thị các nhóm tồn hiện tại + dòng thời gian sổ cái bất biến và chuỗi đảo có cấu trúc Gốc↔Hiệu chỉnh↔Dấu đảo. Phả hệ xuôi/ngược đầy đủ xuyên Receipt→QC→Move→Pick→Shipment/Return/Recall, điều phối thu hồi, phả hệ Owner/HU và quản trị chuỗi thiếu mắt xích trên toàn hệ thống vẫn chưa hoàn tất.', surfaces: ['Web', 'API'], status: 'foundation', spec: '48, 79, 222', route: '/inventory-traceability' },
      { id: 'INV-11', name: 'Bộ máy toàn vẹn & đối chiếu tồn kho', goal: 'Hệ thống thật có đối chiếu chỉ đọc giữa InventoryStock và sổ cái để phát hiện Khớp/Lệch theo sản phẩm/kho; rebuild/remediation có kiểm soát, phê duyệt và bằng chứng kiểm toán cho sửa lệch vẫn chưa triển khai.', surfaces: ['Web', 'API', 'Worker'], status: 'foundation', spec: '82, 152', route: '/inventory-reconciliation' },
    ],
  },
  {
    key: 'transfer-replenishment',
    name: 'Điều chuyển & Bổ sung',
    description: 'Bảo toàn quantity qua nguồn, transit, đích và bổ sung pick face.',
    flow: ['Create', 'Approve', 'Dispatch', 'In Transit', 'Receive', 'Close'],
    capabilities: [
      { id: 'TR-01', name: 'Điều chuyển kho', goal: 'Tạo và quản lý chuyển kho source → transit → destination.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '39, 157', route: '/stock-transfers' },
      { id: 'TR-02', name: 'Tồn kho đang vận chuyển', goal: 'Theo dõi quantity đang vận chuyển theo phiếu/source/destination đã có trong Stock Transfer; global owner-aware in-transit inventory browser vẫn chưa hoàn chỉnh.', surfaces: ['Web', 'API'], status: 'foundation', spec: '39', route: '/stock-transfers' },
      { id: 'TR-03', name: 'Xuất điều chuyển', goal: 'Approved → InTransit: trừ stock kho nguồn, ghi TransferOut và audit/idempotency theo warehouse scope đúng một lần.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '39', route: '/stock-transfers' },
      { id: 'TR-04', name: 'Nhận điều chuyển', goal: 'InTransit → Received: xác nhận thực nhận/thiếu/hỏng, tăng stock kho đích và ghi TransferIn trước khi hoàn tất.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '39', route: '/stock-transfers' },
      { id: 'TR-05', name: 'Bổ sung', goal: 'Bổ sung hàng từ reserve storage sang picking location.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '44, 223' },
    ],
  },
  {
    key: 'count-adjustment',
    name: 'Kiểm kê & Điều chỉnh',
    description: 'Full Count, Cycle Count, blind count, recount, variance resolution và adjustment có approval.',
    flow: ['Plan', 'Freeze/Snapshot', 'Count', 'Review', 'Recount', 'Approval', 'Post', 'Complete'],
    capabilities: [
      { id: 'CT-01', name: 'Kiểm kê toàn phần', goal: 'Kiểm kê toàn kho theo policy và progress tracking.', surfaces: ['Web', 'Mobile', 'API'], status: 'live', spec: '40, 216', route: '/stocktakes' },
      { id: 'CT-02', name: 'Kiểm kê chu kỳ', goal: 'Lập kế hoạch kiểm kê định kỳ theo rule/ABC/risk.', surfaces: ['Web', 'Mobile', 'API'], status: 'foundation', spec: '40' },
      { id: 'CT-03', name: 'Kiểm kê mù', goal: 'Ẩn system quantity với counter để giảm bias.', surfaces: ['Web', 'Mobile'], status: 'planned', spec: '40, 229' },
      { id: 'CT-04', name: 'Chiến lược đóng băng', goal: 'Hard freeze / soft freeze / snapshot-only theo warehouse/count type.', surfaces: ['Web', 'API'], status: 'planned', spec: '40, 229' },
      { id: 'CT-05', name: 'Kiểm đếm lại', goal: 'Lưu immutable attempt history và final accepted count.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '40, 229' },
      { id: 'CT-06', name: 'Xử lý chênh lệch', goal: 'Xem evidence, reason và chọn phương án xử lý.', surfaces: ['Web', 'API'], status: 'planned', spec: '40, 71' },
      { id: 'CT-07', name: 'Điều chỉnh tồn kho', goal: 'Tạo/approve/post adjustment theo threshold và SoD.', surfaces: ['Web', 'API'], status: 'planned', spec: '40, 229' },
    ],
  },
  {
    key: 'quality-returns',
    name: 'Chất lượng, Trả hàng & Phân loại xử lý',
    description: 'Kiểm soát hàng lỗi, quarantine, returns, recall và scrap.',
    capabilities: [
      { id: 'QR-01', name: 'Trung tâm công việc QC', goal: 'Work center QC độc lập xuyên inbound/returns/damage với inspection, criteria, evidence và disposition; Inbound QC hiện đã live riêng ở IN-06 nhưng chưa làm capability này thành live.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '41, 57, 62, 90' },
      { id: 'QR-02', name: 'Giữ QC / Cách ly', goal: 'Chuyển status và chặn reserve/allocate/pick theo policy.', surfaces: ['Web', 'API'], status: 'planned', spec: '41' },
      { id: 'QR-03', name: 'Tồn kho hư hỏng', goal: 'Ghi nhận DAMAGE_FOUND từ nhiều flow và disposition.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '41, 42, 62, 71' },
      { id: 'QR-04', name: 'Trả hàng khách / RMA', goal: 'Receive return, inspect, disposition và post inventory.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '43, 247' },
      { id: 'QR-05', name: 'Thu hồi', goal: 'Trace scope ảnh hưởng và block inventory theo recall.', surfaces: ['Web', 'API'], status: 'planned', spec: '48, 79' },
      { id: 'QR-06', name: 'Tiêu hủy', goal: 'Approval + posting giảm tồn kho với reason/evidence/audit.', surfaces: ['Web', 'API'], status: 'planned', spec: '42, 62, 70' },
    ],
  },
  {
    key: 'handling-packaging',
    name: 'Đơn vị xử lý, Đóng gói & Nhãn',
    description: 'Quản lý pallet, carton, tote, HU hierarchy và label.',
    capabilities: [
      { id: 'HU-01', name: 'Đơn vị xử lý', goal: 'Tạo và theo dõi HU chứa inventory theo location/state.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '37, 216' },
      { id: 'HU-02', name: 'Pallet / Thùng / Khay', goal: 'Đóng gói, merge/split và hierarchy HU.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '37' },
      { id: 'HU-03', name: 'Trung tâm nhãn', goal: 'In/preview barcode, lot, HU, shipping label và reprint audit.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '59, 64' },
      { id: 'HU-04', name: 'SSCC / Nhãn logistics', goal: 'Chuẩn hóa logistics identifier khi áp dụng.', surfaces: ['Web', 'API'], status: 'optional', spec: '64' },
    ],
  },
  {
    key: 'dock-yard-crossdock',
    name: 'Cửa kho, Sân bãi & Trung chuyển thẳng',
    description: 'Điều phối phương tiện, dock door, staging và dòng hàng bỏ qua storage.',
    capabilities: [
      { id: 'DY-01', name: 'Nhận xe tại cổng', goal: 'Check-in vehicle/trailer/driver, seal và arrival evidence theo warehouse scope.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 46, 248', route: '/dock-yard' },
      { id: 'DY-02', name: 'Lập lịch cửa kho', goal: 'Đặt lịch dock, capacity/cutoff, conflict và dock assignment.', surfaces: ['Web', 'API'], status: 'live', spec: '46, 60, 248', route: '/dock-yard' },
      { id: 'DY-03', name: 'Quản lý sân bãi', goal: 'Theo dõi yard occupancy, vehicle queue, dock assignment và checkout release.', surfaces: ['Web', 'API'], status: 'live', spec: '46, 248', route: '/dock-yard' },
      { id: 'DY-04', name: 'Trung chuyển thẳng', goal: 'Match inbound → outbound để giảm putaway/storage.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '47, 216' },
    ],
  },
  {
    key: 'reports-analytics',
    name: 'Báo cáo & Phân tích',
    description: 'Read model/reporting không thay thế transactional truth.',
    capabilities: [
      { id: 'RP-01', name: 'Ảnh chụp tồn kho', goal: 'Báo cáo tồn kho theo sản phẩm/kho và export.', surfaces: ['Web', 'API'], status: 'live', spec: '82, 227', route: '/inventory' },
      { id: 'RP-02', name: 'Xuất - Nhập - Tồn', goal: 'Opening, in, out, closing theo kỳ.', surfaces: ['Web', 'API'], status: 'live', spec: '13, 82, 227', route: '/inventory' },
      { id: 'RP-03', name: 'Hiệu suất Nhập kho / Xuất kho', goal: 'Throughput, dwell time, backlog và SLA.', surfaces: ['Web'], status: 'planned', spec: '68, 90, 91, 227' },
      { id: 'RP-04', name: 'Tuổi tồn kho / Hạn dùng', goal: 'Aging, near-expiry, expired và slow-moving.', surfaces: ['Web'], status: 'planned', spec: '78, 187, 227' },
      { id: 'RP-05', name: 'Độ chính xác & Chênh lệch kiểm kê', goal: 'Inventory accuracy, recount rate và variance trend.', surfaces: ['Web'], status: 'planned', spec: '40, 82, 227' },
      { id: 'RP-06', name: 'Đối chiếu sổ cái', goal: 'Đối chiếu operational balance với immutable ledger theo warehouse/product để phát hiện lệch.', surfaces: ['Web', 'API'], status: 'live', spec: '29, 82, 155', route: '/inventory-reconciliation' },
      { id: 'RP-07', name: 'Năng suất / Bản đồ nhiệt', goal: 'Task productivity, congestion, location utilization.', surfaces: ['Web'], status: 'planned', spec: '67, 81, 227' },
      { id: 'RP-08', name: 'Xuất kiểm toán & bằng chứng', goal: 'Xuất gói bằng chứng phục vụ kiểm toán/compliance.', surfaces: ['Web', 'API'], status: 'planned', spec: '83, 280' },
      { id: 'RP-09', name: 'Vòng quay tồn kho & Số ngày tồn', goal: 'Theo dõi stock turnover, days on hand, slow-moving/dead stock theo semantic KPI thống nhất.', surfaces: ['Web', 'API'], status: 'planned', spec: '68, 227' },
      { id: 'RP-10', name: 'Xuất BI & Nền tảng phân tích', goal: 'Tách analytical workload khỏi OLTP qua CDC/staging/warehouse/lakehouse và giữ lineage về transactional truth.', surfaces: ['API', 'Worker'], status: 'planned', spec: '13, 250, 251' },
    ],
  },
  {
    key: 'administration',
    name: 'Quản trị & Bảo mật',
    description: 'Identity, RBAC, warehouse scope, permission registry, workflow, config, audit và SSO.',
    capabilities: [
      { id: 'AD-01', name: 'Quản trị người dùng', goal: 'Tạo/khóa user, lifecycle và warehouse membership.', surfaces: ['Web', 'API'], status: 'foundation', spec: '17, 112, 113, 225, 276' },
      { id: 'AD-02', name: 'Ma trận vai trò & quyền', goal: 'Permission registry và grant/revoke theo role với concurrency, idempotency và fail-closed authorization.', surfaces: ['Web', 'API'], status: 'live', spec: '17, 225', route: '/permissions' },
      { id: 'AD-03', name: 'Phạm vi kho', goal: 'Chặn cross-warehouse access dù biết record ID.', surfaces: ['API'], status: 'foundation', spec: '17, 99, 225' },
      { id: 'AD-04', name: 'Quy trình phê duyệt', goal: 'Workflow, SoD, threshold, SLA và escalation.', surfaces: ['Web', 'API'], status: 'live', spec: '70, 221', route: '/approvals' },
      { id: 'AD-05', name: 'Cấu hình & cờ chức năng', goal: 'Quản lý config có scope và rollout an toàn.', surfaces: ['Web', 'API'], status: 'planned', spec: '20, 98, 276' },
      { id: 'AD-06', name: 'Dấu vết kiểm toán', goal: 'Audit hành động critical, trước/sau và actor/context.', surfaces: ['Web', 'API'], status: 'foundation', spec: '08, 17, 83, 280' },
      { id: 'AD-07', name: 'Trung tâm thông báo & Bộ máy cảnh báo', goal: 'Thông báo task, approval, exception, SLA và integration failure.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '61, 92, 271' },
      { id: 'AD-08', name: 'SSO / Liên kết định danh', goal: 'OIDC/SAML/enterprise identity và provisioning.', surfaces: ['Web', 'API'], status: 'planned', spec: '112, 279' },
      { id: 'AD-09', name: 'Chuỗi số & Đánh số chứng từ', goal: 'Sinh số chứng từ concurrency-safe theo company/warehouse/document type và policy immutability.', surfaces: ['Web', 'API'], status: 'planned', spec: '69, 276' },
      { id: 'AD-10', name: 'Quy tắc nghiệp vụ & Cấu hình quy trình', goal: 'Cấu hình policy/workflow có scope, version, validation và audit mà không phá domain invariant.', surfaces: ['Web', 'API'], status: 'planned', spec: '51, 276' },
      { id: 'AD-11', name: 'Tài liệu / Tệp đính kèm / Bằng chứng', goal: 'Quản lý evidence an toàn cho receipt, QC, damage, POD, return, scrap, exception và audit.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '62, 83, 96' },
      { id: 'AD-12', name: 'Bản địa hóa & Định dạng vùng', goal: 'Language/date/time/number/unit presentation theo locale nhưng giữ canonical code/value trong API và database.', surfaces: ['Web', 'Mobile', 'API'], status: 'planned', spec: '177, 256' },
      { id: 'AD-13', name: 'Giữ pháp lý & eDiscovery', goal: 'Bảo toàn audit/evidence/POD/integration records theo legal hold và tạo export traceable cho compliance.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '83, 257, 280' },
      { id: 'AD-14', name: 'Yêu cầu quyền riêng tư & Xử lý chủ thể dữ liệu', goal: 'Xử lý privacy request mà không phá business/audit/legal records; mọi action phải có audit và authorization.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '116, 258' },
    ],
  },
  {
    key: 'integration',
    name: 'Tích hợp',
    description: 'Đồng bộ ERP/MDM, orders, carrier/e-commerce/TMS bằng inbox/outbox và hợp đồng idempotent.',
    capabilities: [
      { id: 'IG-01', name: 'Đồng bộ dữ liệu chủ', goal: 'Product, UOM, Partner, Warehouse sync từ ERP/MDM.', surfaces: ['API', 'Worker'], status: 'planned', spec: '162' },
      { id: 'IG-02', name: 'Tích hợp đơn mua', goal: 'PO → expected inbound/ASN và cancel semantics.', surfaces: ['API', 'Worker'], status: 'planned', spec: '160' },
      { id: 'IG-03', name: 'Tích hợp đơn bán', goal: 'SO → outbound order và status/event feedback.', surfaces: ['API', 'Worker'], status: 'planned', spec: '161' },
      { id: 'IG-04', name: 'Outbox giao dịch', goal: 'Business mutation + event commit cùng transaction.', surfaces: ['Worker', 'API'], status: 'foundation', spec: '09, 19, 103, 107' },
      { id: 'IG-05', name: 'Hộp thư tích hợp', goal: 'Deduplicate inbound commands/events và retry an toàn.', surfaces: ['Worker', 'API'], status: 'planned', spec: '09, 75, 103, 107' },
      { id: 'IG-06', name: 'Đơn vị vận chuyển / Thương mại điện tử / TMS', goal: 'Shipping, tracking và fulfillment integration.', surfaces: ['API', 'Worker'], status: 'optional', spec: '163, 164, 165, 166, 169' },
      { id: 'IG-07', name: 'Thư lỗi & Phát lại', goal: 'Quản lý integration failure, retry/replay có audit.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '75, 103' },
      { id: 'IG-08', name: 'Nhập dữ liệu có kiểm soát & Thao tác hàng loạt', goal: 'Import/export/bulk operation có validation, preview, error report, authorization và audit.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '63, 76' },
      { id: 'IG-09', name: 'Ranh giới tích hợp Tài chính / Giá vốn / Định giá', goal: 'Xuất quantity/movement truth cho Finance/Costing, period cutoff và valuation events mà không biến WMS thành accounting engine.', surfaces: ['API', 'Worker'], status: 'planned', spec: '150, 154, 156, 158, 159, 172' },
      { id: 'IG-10', name: 'Bảng điều khiển & Kiểm soát đối chiếu tích hợp', goal: 'So sánh source control totals, WMS inbox/document/outbox và target acknowledgement; phát hiện mismatch và hỗ trợ safe retry.', surfaces: ['Web', 'API', 'Worker'], status: 'planned', spec: '75, 171' },
    ],
  },
  {
    key: 'mobile',
    name: 'WMS di động',
    description: 'Scan-first execution cho operator, hỗ trợ offline/deferred sync theo policy.',
    capabilities: [
      { id: 'MO-01', name: 'Trang chủ & Nhiệm vụ của tôi', goal: 'Kho hiện tại, task priority/due và quick scan.', surfaces: ['Mobile'], status: 'planned', spec: '80, 223, 229' },
      { id: 'MO-02', name: 'Quét tiếp nhận', goal: 'Scan product/barcode, quantity/UOM, lot/expiry/location.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '34, 65, 223, 229' },
      { id: 'MO-03', name: 'Quét cất hàng', goal: 'Scan product/HU và location đích, confirm move.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '35, 65, 223, 229' },
      { id: 'MO-04', name: 'Quét lấy hàng', goal: 'Scan location, product, serial và xác nhận quantity.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '36, 65, 223, 229' },
      { id: 'MO-05', name: 'Đóng gói', goal: 'Scan item, pack, complete và label.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '37, 64, 65, 223, 229' },
      { id: 'MO-06', name: 'Kiểm kê', goal: 'Location/product scan, blind count và recount.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '40, 65, 223, 229' },
      { id: 'MO-07', name: 'Điều chuyển', goal: 'Create/scan/confirm transfer và receiving.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '39, 65, 223, 229' },
      { id: 'MO-08', name: 'Bổ sung', goal: 'Create/scan/confirm replenish task.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '44, 65, 223, 229' },
      { id: 'MO-09', name: 'Trả hàng', goal: 'Receive, inspect, disposition và post return.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '43, 65, 223, 229' },
      { id: 'MO-10', name: 'Hàng đợi ngoại tuyến & Đồng bộ', goal: 'Cho phép task low-risk offline, retry và giữ dữ liệu khi sync lỗi.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '66, 146, 223, 229, 272' },
      { id: 'MO-11', name: 'Tra cứu sản phẩm', goal: 'Tra cứu nhanh Product/SKU bằng exact barcode, code hoặc tên trong warehouse scope; không cần mở flow nghiệp vụ dài.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '65, 95, 223' },
      { id: 'MO-12', name: 'Xử lý ngoại lệ', goal: 'Xem/assign/resolve/escalate exception từ mobile mà giữ task context, reason, evidence và sync semantics.', surfaces: ['Mobile', 'API'], status: 'planned', spec: '71, 92, 223, 272' },
    ],
  },
  {
    key: 'operations-resilience',
    name: 'Vận hành & Khả năng phục hồi',
    description: 'Runbook, monitoring, backup/restore, DR, commissioning và recovery reconciliation.',
    capabilities: [
      { id: 'OP-01', name: 'Khả năng quan sát & Giám sát', goal: '500/409/412, deadlock, latency, outbox backlog và business integrity alerts.', surfaces: ['Web', 'Worker'], status: 'foundation', spec: '10, 121, 122, 123' },
      { id: 'OP-02', name: 'Mở / Đóng kho', goal: 'Health/inventory/integration check trước mở và đóng site.', surfaces: ['Web', 'API'], status: 'planned', spec: '148, 149' },
      { id: 'OP-03', name: 'Diễn tập sao lưu / khôi phục', goal: 'Chứng minh recovery và bảo vệ ledger/audit/document transactions.', surfaces: ['Worker'], status: 'foundation', spec: '11, 142, 143' },
      { id: 'OP-04', name: 'Đối chiếu sự cố & phục hồi', goal: 'Reconcile inventory trước khi warehouse reopen.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '140, 147, 151, 152' },
      { id: 'OP-05', name: 'Vận hành kho ngoại tuyến', goal: 'Deferred synchronization trong giới hạn an toàn.', surfaces: ['Mobile', 'Worker'], status: 'planned', spec: '145, 146, 272' },
      { id: 'OP-06', name: 'Đưa kho vào vận hành', goal: 'Checklist site mới trước go-live.', surfaces: ['Web'], status: 'planned', spec: '135, 264, 277' },
      { id: 'OP-07', name: 'Dự phòng thủ công & Đối chiếu sau sự cố', goal: 'Biểu mẫu dự phòng và nhập/reconcile sau outage.', surfaces: ['Web'], status: 'planned', spec: '145, 147, 281' },
      { id: 'OP-08', name: 'Đo từ xa chức năng & Đo lường UX', goal: 'Đo adoption, friction, error/latency và scan workflow regression mà không dùng product analytics làm business KPI truth.', surfaces: ['Web', 'Mobile', 'Worker'], status: 'planned', spec: '254, 263' },
      { id: 'OP-09', name: 'Ngừng vận hành kho & Kết thúc tenant', goal: 'Đóng warehouse/company/tenant an toàn với inventory zero/reconcile, integration stop, evidence retention và data exit.', surfaces: ['Web', 'Worker'], status: 'planned', spec: '99, 265' },
      { id: 'OP-10', name: 'Hiệu chỉnh dữ liệu hệ thống thật & Sửa chữa có kiểm soát', goal: 'Sửa dữ liệu production bằng reversal/corrected transaction hoặc controlled correction; không sửa posted ledger trực tiếp.', surfaces: ['Web', 'API'], status: 'planned', spec: '127' },
      { id: 'OP-11', name: 'Công cụ hỗ trợ & quản trị', goal: 'Cung cấp support tooling an toàn qua Application Layer, audit và permission thay vì truy cập DB trực tiếp thường xuyên.', surfaces: ['Web', 'API'], status: 'planned', spec: '128, 153' },
    ],
  },
  {
    key: 'advanced-planning',
    name: 'WMS nâng cao & Lập kế hoạch',
    description: 'Capability nâng cao chỉ bật khi có nhu cầu/ngành/customer evidence.',
    capabilities: [
      { id: 'AX-01', name: 'Tối ưu xếp vị trí', goal: 'Đề xuất slot theo velocity, compatibility và travel cost.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '49' },
      { id: 'AX-02', name: 'Lập kế hoạch nhân lực / Khối lượng công việc', goal: 'Lập kế hoạch nguồn lực theo task volume và shift capacity.', surfaces: ['Web'], status: 'optional', spec: '50, 60, 80, 81' },
      { id: 'AX-03', name: 'Lập kế hoạch mạng lưới nhiều kho', goal: 'DC/hub/spoke role, product eligibility và network quantity.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '178, 182, 183' },
      { id: 'AX-04', name: 'Dự báo sức chứa', goal: 'Dự báo congestion theo compatible storage capacity.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '194' },
      { id: 'AX-05', name: '3PL / Nhiều chủ sở hữu / Tính phí', goal: 'Owner-aware inventory và warehouse service billing reconciliation.', surfaces: ['Web', 'API'], status: 'optional', spec: '54, 55, 56, 173, 245, 246' },
      { id: 'AX-06', name: 'RFID / Giọng nói / Đèn hướng dẫn lấy hàng / Robot', goal: 'Warehouse automation theo feature applicability.', surfaces: ['Mobile', 'API', 'Worker'], status: 'optional', spec: '235, 236, 237' },
      { id: 'AX-07', name: 'Chuỗi lạnh / Hàng nguy hiểm / Khối lượng biến đổi', goal: 'Specialized inventory rules khi ngành yêu cầu.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '240, 241, 242' },
      { id: 'AX-08', name: 'Thiết bị & tài sản xử lý vật liệu', goal: 'Quản lý forklift, scanner, printer, cart/conveyor endpoint, assignment, eligibility và maintenance khi áp dụng.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '59' },
      { id: 'AX-09', name: 'Đóng bộ / Gộp bộ / Lắp ráp / Tháo bộ', goal: 'Light-manufacturing warehouse flow theo component allocation, pick, assembly, QC optional và transformation posting.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '52, 243' },
      { id: 'AX-10', name: 'Dịch vụ gia tăng (VAS)', goal: 'Thực thi labeling/repack/custom service theo order/task có traceability và billing hooks khi khách hàng yêu cầu.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '53, 244' },
      { id: 'AX-11', name: 'Tối ưu thùng / thể tích / đóng gói', goal: 'Đề xuất carton/tote/pallet theo dimension, weight, packaging rule và carrier constraint; không tự xác nhận PACKED.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '238' },
      { id: 'AX-12', name: 'Lập kế hoạch xếp tải & Mức sử dụng xe', goal: 'Tối ưu staging-to-load, trailer/container capacity và loading sequence nhưng không tự tạo inventory effect.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'optional', spec: '239' },
      { id: 'AX-13', name: 'Logistics ngược nâng cao / Tân trang / RTV', goal: 'Mở rộng return disposition sang refurbish, repair, RTV, scrap và reverse-logistics execution theo customer evidence.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '43, 247' },
      { id: 'AX-14', name: 'An toàn kho / Công thái học / Kiểm soát rủi ro', goal: 'Áp restricted zone, equipment qualification, heavy-lift threshold, one-way aisle và high-risk acknowledgement vào task/rule layer.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '249' },
      { id: 'AX-15', name: 'Tích hợp WCS / WES / Robot', goal: 'Trace WMS Task → Device Job → Device Event → WMS Command mà không giao transactional inventory truth cho automation layer.', surfaces: ['API', 'Worker'], status: 'optional', spec: '235' },
      { id: 'AX-16', name: 'Cảm biến RFID / IoT & Ghi nhận thời gian thực', goal: 'Thu nhận RFID/sensor/device event có dedupe, timestamp, device identity và traceability về WMS context.', surfaces: ['Mobile', 'API', 'Worker'], status: 'optional', spec: '236' },
      { id: 'AX-17', name: 'Lấy hàng bằng giọng nói / Đèn hướng dẫn / Hỗ trợ lấy hàng', goal: 'Hỗ trợ operator bằng voice/light/device guidance nhưng vẫn dùng cùng task, permission, scan/idempotency semantics.', surfaces: ['Mobile', 'API', 'Worker'], status: 'optional', spec: '237' },
      { id: 'AX-18', name: 'Hàng nguy hiểm & Lưu trữ hạn chế', goal: 'Quản lý compatibility, restricted zone, segregation và handling policy cho dangerous goods khi ngành yêu cầu.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '240' },
      { id: 'AX-19', name: 'Chuỗi lạnh & Sai lệch nhiệt độ', goal: 'Theo dõi temperature-controlled inventory, excursion, evidence và quarantine/disposition policy.', surfaces: ['Web', 'Mobile', 'API', 'Worker'], status: 'optional', spec: '241' },
      { id: 'AX-20', name: 'Khối lượng biến đổi & Tồn kho hai UOM', goal: 'Quản lý quantity song song Base UOM và variable-weight UOM với conversion/tolerance và ledger traceability.', surfaces: ['Web', 'Mobile', 'API'], status: 'optional', spec: '242' },
      { id: 'AX-21', name: 'Tồn ký gửi / Nhà cung cấp sở hữu / Khách hàng sở hữu', goal: 'Quản lý owner dimension, ownership eligibility và explicit reclassification cho consignment/multi-owner stock.', surfaces: ['Web', 'API'], status: 'optional', spec: '55, 73, 245' },
      { id: 'AX-22', name: 'Hợp đồng 3PL / Tính giá / Tính phí kho', goal: 'Quản lý service contract, rating rule, billable activity và reconciliation với operational evidence.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '56, 173, 246' },
      { id: 'AX-23', name: 'ATP / CTP & Ngày cam kết', goal: 'Tính Available-to-Promise/Capable-to-Promise và promise date cho OMS/Sales mà không tạo reservation ngầm.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '89' },
      { id: 'AX-24', name: 'Cân bằng tồn kho mạng lưới', goal: 'Phát hiện surplus/shortage và đề xuất cân bằng tồn giữa kho dựa trên demand, availability, safety stock và transfer economics.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '179' },
      { id: 'AX-25', name: 'Lập kế hoạch bổ sung liên kho', goal: 'Lập kế hoạch replenishment giữa các kho nhưng không bypass Reservation/Allocation/Transfer semantics.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '180' },
      { id: 'AX-26', name: 'Định tuyến đơn & Chọn điểm hoàn tất', goal: 'Chọn fulfillment node theo eligibility, availability, SLA, capacity và score giải thích được.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '181' },
      { id: 'AX-27', name: 'Tích hợp dự báo & Tín hiệu nhu cầu', goal: 'Nhận forecast/demand signals từ ERP/OMS/planning vào staging/read model mà không biến forecast thành inventory truth.', surfaces: ['API', 'Worker'], status: 'optional', spec: '184' },
      { id: 'AX-28', name: 'Tồn an toàn & Chính sách đặt lại', goal: 'Quản lý safety stock, reorder point, min/max theo SKU/Warehouse/Owner và projected availability.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '185' },
      { id: 'AX-29', name: 'Phân loại tồn kho ABC / XYZ', goal: 'Phân loại ABC/XYZ phục vụ slotting, cycle count, replenishment và optimization với versioned calculation.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '186' },
      { id: 'AX-30', name: 'Chính sách tối ưu tồn kho', goal: 'Kết hợp demand, safety stock, ABC/XYZ, expiry, capacity và network data để đề xuất policy tồn tối ưu.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '188' },
      { id: 'AX-31', name: 'Mô phỏng cân bằng lại mạng lưới', goal: 'Mô phỏng surplus/shortage, transfer candidates và constraints trên immutable snapshot trước khi người dùng quyết định.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '189' },
      { id: 'AX-32', name: 'Độ chính xác dự báo & Đo thiên lệch', goal: 'Đo forecast accuracy/bias theo grain và segment để planning team đánh giá chất lượng dự báo.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '190' },
      { id: 'AX-33', name: 'Quản lý ngoại lệ bổ sung', goal: 'Triage overdue/no-source/capacity/policy conflict để tránh silent stockout hoặc task spam.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '191' },
      { id: 'AX-34', name: 'Bộ máy đề xuất mua hàng', goal: 'Sinh đề xuất mua khi projected available xuống dưới policy threshold; chỉ gửi ERP khi được approve.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '192' },
      { id: 'AX-35', name: 'Chấm điểm rủi ro tồn kho', goal: 'Tính risk score explainable cho stockout, expiry, accuracy, congestion và supply uncertainty mà không tạo hidden mutation.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '193' },
      { id: 'AX-36', name: 'Phân tích lead-time nhà cung cấp', goal: 'Đo planned/actual lead time, variability, percentile và supplier service reliability.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '196' },
      { id: 'AX-37', name: 'Khuyến nghị thúc đẩy / trì hoãn mua hàng', goal: 'Đề xuất expedite/defer/split/keep inbound supply dựa trên demand, projected inventory và supplier capability.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '197' },
      { id: 'AX-38', name: 'Phát hiện tăng vọt nhu cầu & bất thường', goal: 'Phát hiện spike/drop/data anomaly trước khi chúng làm sai replenishment và planning.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '198' },
      { id: 'AX-39', name: 'Chính sách công bằng phân bổ tồn kho', goal: 'Tạo allocation proposal deterministic theo fairness policy mà không bypass atomic reservation/allocation.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '86, 199' },
      { id: 'AX-40', name: 'Phân khúc mức dịch vụ', goal: 'Quản lý service class dùng chung cho fulfillment, safety stock, replenishment, allocation và SLA analytics.', surfaces: ['Web', 'API'], status: 'optional', spec: '87, 200' },
      { id: 'AX-41', name: 'Lập kế hoạch kịch bản & Mô phỏng giả định', goal: 'Chạy what-if trên immutable snapshot; scenario data tuyệt đối không mutate production master/document/ledger.', surfaces: ['Web', 'Worker'], status: 'optional', spec: '201' },
      { id: 'AX-42', name: 'Sổ đăng ký chính sách quyết định & Con người trong vòng kiểm soát', goal: 'Version hóa policy ảnh hưởng planning/execution, scope/effective dates/owner và yêu cầu human approval ở decision boundary.', surfaces: ['Web', 'API', 'Worker'], status: 'optional', spec: '202, 203' },
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
