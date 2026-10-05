export type ProductionNavItem = {
  path: string;
  label: string;
  section: string;
  description: string;
  permission?: string;
  access?: 'always' | 'stocktake' | 'approvals';
};

export const productionNavigation: ProductionNavItem[] = [
  { path: '/', label: 'Tổng quan', section: 'Vận hành', description: 'Điểm vào nhanh tới các work center và trạng thái demo/runtime.', access: 'always' },
  { path: '/dock-yard', label: 'Dock & Yard', section: 'Vận hành', description: 'Lịch xe, gate check-in, yard slot, dock assignment và turnaround.', permission: 'dock_appointment.read' },
  { path: '/products', label: 'Sản phẩm', section: 'Dữ liệu nền', description: 'SKU, danh mục, đơn vị tính và barcode của sản phẩm.', permission: 'product.read' },
  { path: '/warehouses', label: 'Kho hàng', section: 'Dữ liệu nền', description: 'Danh mục kho và trạng thái hoạt động.', permission: 'warehouse.read' },
  { path: '/warehouse-structure', label: 'Cấu trúc vị trí', section: 'Dữ liệu nền', description: 'Zone, dãy kệ, kệ, tầng và ô/vị trí theo warehouse scope.', permission: 'location.read' },
  { path: '/warehouse-map', label: 'Bản đồ kho', section: 'Dữ liệu nền', description: 'Layout vật lý, utilization và operational heatmap theo warehouse scope.', permission: 'location.read' },
  { path: '/warehouse-calendar', label: 'Lịch & ca kho', section: 'Dữ liệu nền', description: 'Timezone, lịch tuần, cutoff và capacity theo ca vận hành.', permission: 'warehouse.read' },
  { path: '/units', label: 'Đơn vị tính', section: 'Dữ liệu nền', description: 'Danh mục đơn vị tính dùng trong chứng từ và tồn kho.', permission: 'uom.read' },
  { path: '/business-partners', label: 'Đối tác', section: 'Dữ liệu nền', description: 'Nhà cung cấp, khách hàng và thông tin liên hệ.', permission: 'partner.read' },
  { path: '/purchase-orders', label: 'Đơn mua (PO)', section: 'Inbound', description: 'Expected inbound từ ERP/Procurement, dung sai và trạng thái nhận hàng.', permission: 'purchase_order.read' },
  { path: '/asns', label: 'ASN dự kiến', section: 'Inbound', description: 'Lô hàng dự kiến, vận chuyển, arrival và handoff sang tiếp nhận.', permission: 'asn.read' },
  { path: '/import-receipts', label: 'Phiếu nhập kho', section: 'Inbound', description: 'Nhận hàng, QC, discrepancy và posting nhập kho.', permission: 'receipt.read' },
  { path: '/putaway-tasks', label: 'Cất hàng', section: 'Inbound', description: 'Nhiệm vụ putaway từ khu nhận hàng tới location đích.', permission: 'putaway.read' },
  { path: '/export-receipts', label: 'Phiếu xuất kho', section: 'Outbound', description: 'Chứng từ xuất, reservation và dispatch hàng khỏi kho.', permission: 'export_receipt.read' },
  { path: '/stock-reservations', label: 'Giữ hàng', section: 'Outbound', description: 'Theo dõi tồn đã cam kết theo chứng từ và thời hạn.', access: 'always' },
  { path: '/stock-allocations', label: 'Allocation', section: 'Outbound', description: 'Gắn reservation vào vị trí pickable cụ thể, release và reallocate có kiểm soát.', permission: 'allocation.read' },
  { path: '/inventory', label: 'Tồn kho', section: 'Inventory Control', description: 'Tồn hiện tại, lịch sử movement và báo cáo xuất-nhập-tồn.', access: 'always' },
  { path: '/inventory-reconciliation', label: 'Đối chiếu tồn kho', section: 'Inventory Control', description: 'So sánh operational balance với immutable ledger để phát hiện lệch.', access: 'always' },
  { path: '/stocktakes', label: 'Kiểm kê kho', section: 'Inventory Control', description: 'Phiếu kiểm kê, variance và quy trình duyệt điều chỉnh.', access: 'stocktake' },
  { path: '/stock-transfers', label: 'Điều chuyển kho', section: 'Inventory Control', description: 'Luân chuyển tồn giữa các kho và theo dõi in-transit.', access: 'always' },
  { path: '/approvals', label: 'Phê duyệt', section: 'Kiểm soát', description: 'Hàng đợi phê duyệt theo SLA, chứng từ và warehouse scope.', access: 'approvals' },
  { path: '/permissions', label: 'Quản trị quyền', section: 'Kiểm soát', description: 'Danh mục permission và grant theo role.', permission: 'permission.read' },
];

export const productionSections = ['Vận hành', 'Dữ liệu nền', 'Inbound', 'Outbound', 'Inventory Control', 'Kiểm soát'] as const;

export const resolveProductionPage = (pathname: string): ProductionNavItem | undefined => {
  if (pathname === '/') return productionNavigation[0];
  return productionNavigation
    .filter(item => item.path !== '/')
    .sort((a, b) => b.path.length - a.path.length)
    .find(item => pathname === item.path || pathname.startsWith(item.path + '/'));
};
