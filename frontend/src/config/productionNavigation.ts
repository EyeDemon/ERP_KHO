export type ProductionNavItem = {
  path: string;
  label: string;
  section: string;
  description: string;
  permission?: string;
  access?: 'always' | 'stocktake' | 'approvals';
};

export const productionNavigation: ProductionNavItem[] = [
  { path: '/', label: 'Tổng quan', section: 'Vận hành', description: 'Điểm vào nhanh tới các trung tâm công việc và trạng thái vận hành thực tế.', access: 'always' },
  { path: '/dock-yard', label: 'Cổng, sân bãi & cửa kho', section: 'Vận hành', description: 'Lịch xe, nhận xe vào cổng, vị trí sân bãi, phân cửa kho và thời gian quay vòng.', permission: 'dock_appointment.read' },
  { path: '/products', label: 'Sản phẩm', section: 'Dữ liệu nền', description: 'SKU, danh mục, đơn vị tính và mã vạch của sản phẩm.', permission: 'product.read' },
  { path: '/warehouses', label: 'Kho hàng', section: 'Dữ liệu nền', description: 'Danh mục kho và trạng thái hoạt động.', permission: 'warehouse.read' },
  { path: '/warehouse-structure', label: 'Cấu trúc vị trí', section: 'Dữ liệu nền', description: 'Khu, dãy kệ, kệ, tầng và ô/vị trí theo phạm vi kho được phép.', permission: 'location.read' },
  { path: '/warehouse-map', label: 'Bản đồ kho', section: 'Dữ liệu nền', description: 'Bố cục vật lý, mức sử dụng và bản đồ nhiệt vận hành theo phạm vi kho được phép.', permission: 'location.read' },
  { path: '/warehouse-calendar', label: 'Lịch & ca kho', section: 'Dữ liệu nền', description: 'Múi giờ, lịch tuần, giờ chốt và năng lực theo ca vận hành.', permission: 'warehouse.read' },
  { path: '/units', label: 'Đơn vị tính', section: 'Dữ liệu nền', description: 'Danh mục đơn vị tính dùng trong chứng từ và tồn kho.', permission: 'uom.read' },
  { path: '/business-partners', label: 'Đối tác', section: 'Dữ liệu nền', description: 'Nhà cung cấp, khách hàng và thông tin liên hệ.', permission: 'partner.read' },
  { path: '/purchase-orders', label: 'Đơn mua (PO)', section: 'Nhập kho', description: 'Đơn hàng dự kiến từ ERP/Mua hàng, dung sai và trạng thái nhận hàng.', permission: 'purchase_order.read' },
  { path: '/asns', label: 'ASN dự kiến', section: 'Nhập kho', description: 'Lô hàng dự kiến, vận chuyển, thời điểm đến và bàn giao sang tiếp nhận.', permission: 'asn.read' },
  { path: '/import-receipts', label: 'Phiếu nhập kho', section: 'Nhập kho', description: 'Nhận hàng, kiểm tra chất lượng, chênh lệch và ghi sổ nhập kho.', permission: 'receipt.read' },
  { path: '/putaway-tasks', label: 'Cất hàng', section: 'Nhập kho', description: 'Nhiệm vụ cất hàng từ khu nhận hàng tới vị trí đích.', permission: 'putaway.read' },
  { path: '/export-receipts', label: 'Phiếu xuất kho', section: 'Xuất kho', description: 'Chứng từ xuất, giữ hàng và xác nhận hàng rời kho.', permission: 'export_receipt.read' },
  { path: '/stock-reservations', label: 'Giữ hàng', section: 'Xuất kho', description: 'Theo dõi tồn đã cam kết theo chứng từ và thời hạn.', access: 'always' },
  { path: '/stock-allocations', label: 'Phân bổ tồn', section: 'Xuất kho', description: 'Gắn lượng đã giữ vào vị trí có thể lấy hàng cụ thể, giải phóng và phân bổ lại có kiểm soát.', permission: 'allocation.read' },
  { path: '/picking-tasks', label: 'Lấy hàng', section: 'Xuất kho', description: 'Ưu tiên quét mã khi lấy hàng theo phân bổ, xử lý thiếu hàng và ngoại lệ.', permission: 'picking.read' },
  { path: '/packing-sessions', label: 'Đóng gói & đơn vị xử lý', section: 'Xuất kho', description: 'Đóng gói lượng đã lấy vào thùng/khay/đơn vị xử lý, bảo toàn số lượng và hỗ trợ đơn vị xử lý lồng nhau.', permission: 'packing.read' },
  { path: '/shipments', label: 'Thực hiện giao hàng', section: 'Xuất kho', description: 'Luồng giao hàng READY → STAGING → LOADING → LOADED → DISPATCHED, gắn ngữ cảnh đơn vị xử lý/cửa kho và ranh giới trừ tồn khi SHIP.', permission: 'shipment.read' },
  { path: '/backorders', label: 'Đơn bán & đơn thiếu hàng', section: 'Xuất kho', description: 'Vòng đời nhu cầu, giữ/phân bổ một phần, phục hồi đơn thiếu hàng và hủy đơn.', permission: 'backorder.read' },
  { path: '/inventory', label: 'Tồn kho', section: 'Kiểm soát tồn kho', description: 'Tồn thực tế/đã giữ/khả dụng theo nhóm Trạng thái/Lô/Sê-ri, lịch sử biến động và báo cáo xuất-nhập-tồn.', permission: 'inventory.read' },
  { path: '/inventory-locks', label: 'Khóa / đóng băng tồn kho', section: 'Kiểm soát tồn kho', description: 'Khóa nhóm tồn theo Kho/Vị trí/Sản phẩm/Trạng thái/Lô/Sê-ri và mở khóa có lưu vết kiểm toán.', permission: 'inventory_lock.read' },
  { path: '/inventory-movements', label: 'Di chuyển vị trí nội bộ', section: 'Kiểm soát tồn kho', description: 'Di chuyển tồn chưa được giữ giữa các vị trí trong cùng kho, giữ nguyên Trạng thái/Lô/Sê-ri và ghi giao dịch MOVE.', permission: 'inventory.read' },
  { path: '/inventory-reversals', label: 'Đảo giao dịch tồn kho', section: 'Kiểm soát tồn kho', description: 'Đảo hiệu chỉnh cho giao dịch di chuyển/đổi trạng thái mà không sửa lịch sử đã ghi sổ.', permission: 'inventory_ledger.read' },
  { path: '/inventory-traceability', label: 'Truy vết & phả hệ tồn kho', section: 'Kiểm soát tồn kho', description: 'Truy nhóm tồn hiện tại và sổ cái bất biến theo Sản phẩm/Lô/Sê-ri/Tham chiếu.', permission: 'inventory_traceability.read' },
  { path: '/inventory-reconciliation', label: 'Đối chiếu tồn kho', section: 'Kiểm soát tồn kho', description: 'So sánh số dư vận hành với sổ cái bất biến để phát hiện chênh lệch.', access: 'always' },
  { path: '/stocktakes', label: 'Kiểm kê kho', section: 'Kiểm soát tồn kho', description: 'Phiếu kiểm kê, chênh lệch và quy trình duyệt điều chỉnh.', access: 'stocktake' },
  { path: '/stock-transfers', label: 'Điều chuyển kho', section: 'Kiểm soát tồn kho', description: 'Luân chuyển tồn giữa các kho và theo dõi hàng đang vận chuyển.', access: 'always' },
  { path: '/approvals', label: 'Phê duyệt', section: 'Kiểm soát', description: 'Hàng đợi phê duyệt theo SLA, chứng từ và phạm vi kho được phép.', access: 'approvals' },
  { path: '/permissions', label: 'Quản trị quyền', section: 'Kiểm soát', description: 'Danh mục quyền và cấp quyền theo vai trò.', permission: 'permission.read' },
];

export const productionSections = ['Vận hành', 'Dữ liệu nền', 'Nhập kho', 'Xuất kho', 'Kiểm soát tồn kho', 'Kiểm soát'] as const;

export const resolveProductionPage = (pathname: string): ProductionNavItem | undefined => {
  if (pathname === '/') return productionNavigation[0];
  return productionNavigation
    .filter(item => item.path !== '/')
    .sort((a, b) => b.path.length - a.path.length)
    .find(item => pathname === item.path || pathname.startsWith(item.path + '/'));
};
