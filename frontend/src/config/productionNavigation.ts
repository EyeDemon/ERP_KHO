export type ProductionNavItem = {
  path: string;
  label: string;
  section: string;
  description: string;
  permission?: string;
  access?: 'always' | 'stocktake' | 'approvals';
  expressMode?: boolean;
};

export const productionNavigation: ProductionNavItem[] = [
  // 1. Tổng quan (chỉ giữ đúng 1 mục Tổng quan để không đẩy Nhập kho / Xuất kho xuống dưới)
  { path: '/', label: 'Tổng quan', section: 'Tổng quan', description: 'Điểm vào nhanh tới các trung tâm công việc và trạng thái vận hành thực tế.', access: 'always', expressMode: true },

  // 2. Nhập kho
  { path: '/dock-yard', label: 'Cổng, sân bãi & cửa kho', section: 'Nhập kho', description: 'Lịch xe, nhận xe vào cổng, vị trí sân bãi, phân cửa kho và thời gian quay vòng.', permission: 'dock_appointment.read', expressMode: false },
  { path: '/purchase-orders', label: 'Đơn mua (PO)', section: 'Nhập kho', description: 'Đơn hàng dự kiến từ ERP/Mua hàng, dung sai và trạng thái nhận hàng.', permission: 'purchase_order.read', expressMode: false },
  { path: '/asns', label: 'ASN dự kiến', section: 'Nhập kho', description: 'Lô hàng dự kiến, vận chuyển, thời điểm đến và bàn giao sang tiếp nhận.', permission: 'asn.read', expressMode: false },
  { path: '/import-receipts', label: 'Phiếu nhập kho', section: 'Nhập kho', description: 'Nhận hàng, kiểm tra chất lượng, chênh lệch và ghi sổ nhập kho.', permission: 'receipt.read', expressMode: true },
  { path: '/putaway-tasks', label: 'Cất hàng', section: 'Nhập kho', description: 'Nhiệm vụ cất hàng từ khu nhận hàng tới vị trí đích.', permission: 'putaway.read', expressMode: false },

  // 3. Xuất kho
  { path: '/export-receipts', label: 'Phiếu xuất kho', section: 'Xuất kho', description: 'Chứng từ xuất, giữ hàng và xác nhận hàng rời kho.', permission: 'export_receipt.read', expressMode: true },
  { path: '/stock-reservations', label: 'Giữ hàng', section: 'Xuất kho', description: 'Theo dõi tồn đã cam kết theo chứng từ và thời hạn.', permission: 'inventory.read', expressMode: false },
  { path: '/stock-allocations', label: 'Phân bổ tồn', section: 'Xuất kho', description: 'Gắn lượng đã giữ vào vị trí có thể lấy hàng cụ thể, giải phóng và phân bổ lại có kiểm soát.', permission: 'allocation.read', expressMode: false },
  { path: '/picking-tasks', label: 'Lấy hàng', section: 'Xuất kho', description: 'Ưu tiên quét mã khi lấy hàng theo phân bổ, xử lý thiếu hàng và ngoại lệ.', permission: 'picking.read', expressMode: false },
  { path: '/packing-sessions', label: 'Đóng gói & đơn vị xử lý', section: 'Xuất kho', description: 'Đóng gói lượng đã lấy vào thùng/khay/đơn vị xử lý, bảo toàn số lượng và hỗ trợ đơn vị xử lý lồng nhau.', permission: 'packing.read', expressMode: true },
  { path: '/shipments', label: 'Thực hiện giao hàng', section: 'Xuất kho', description: 'Theo dõi vận chuyển xuyên suốt từ Sẵn sàng → Tập kết → Đang xếp hàng → Đã xếp xong → Đã xuất bến đến xác nhận giao hàng.', permission: 'shipment.read', expressMode: true },
  { path: '/handover-manifests', label: 'Biên bản bàn giao bưu tá', section: 'Xuất kho', description: 'Quét mã vận đơn AWB lên xe tải/bưu tá, chặn đơn hủy phút chót và niêm phong biên bản bàn giao.', permission: 'inventory.read', expressMode: true },
  { path: '/backorders', label: 'Đơn bán & đơn thiếu hàng', section: 'Xuất kho', description: 'Vòng đời nhu cầu, giữ/phân bổ một phần, phục hồi đơn thiếu hàng và hủy đơn.', permission: 'backorder.read', expressMode: true },

  // 4. Tồn kho
  { path: '/inventory', label: 'Tồn kho', section: 'Tồn kho', description: 'Tồn thực tế/đã giữ/khả dụng theo nhóm Trạng thái/Lô/Sê-ri, lịch sử biến động và báo cáo xuất-nhập-tồn.', permission: 'inventory.read', expressMode: true },
  { path: '/inventory-locks', label: 'Khóa / đóng băng tồn kho', section: 'Tồn kho', description: 'Khóa nhóm tồn theo Kho/Vị trí/Sản phẩm/Trạng thái/Lô/Sê-ri và mở khóa có lưu vết kiểm toán.', permission: 'inventory_lock.read', expressMode: false },
  { path: '/inventory-movements', label: 'Di chuyển vị trí nội bộ', section: 'Tồn kho', description: 'Di chuyển tồn chưa được giữ giữa các vị trí trong cùng kho, giữ nguyên Trạng thái/Lô/Sê-ri và ghi bút toán di chuyển nội bộ.', permission: 'inventory.read', expressMode: false },
  { path: '/inventory-reversals', label: 'Đảo giao dịch tồn kho', section: 'Tồn kho', description: 'Đảo hiệu chỉnh cho giao dịch di chuyển/đổi trạng thái mà không sửa lịch sử đã ghi sổ.', permission: 'inventory_ledger.read', expressMode: false },
  { path: '/inventory-traceability', label: 'Truy vết & phả hệ tồn kho', section: 'Tồn kho', description: 'Truy nhóm tồn hiện tại và sổ cái bất biến theo Sản phẩm/Lô/Sê-ri/Tham chiếu.', permission: 'inventory_traceability.read', expressMode: false },
  { path: '/inventory-reconciliation', label: 'Đối chiếu tồn kho', section: 'Tồn kho', description: 'So sánh số dư vận hành với sổ cái bất biến để phát hiện chênh lệch.', permission: 'inventory_ledger.read', expressMode: false },
  { path: '/stocktakes', label: 'Kiểm kê kho', section: 'Tồn kho', description: 'Phiếu kiểm kê, chênh lệch và quy trình duyệt điều chỉnh.', access: 'stocktake', expressMode: true },
  { path: '/stock-transfers', label: 'Điều chuyển kho', section: 'Tồn kho', description: 'Luân chuyển tồn giữa các kho và theo dõi hàng đang vận chuyển.', permission: 'inventory.read', expressMode: false },

  // 5. Tài chính
  { path: '/cod-reconciliation', label: 'Đối soát COD 3 chiều', section: 'Tài chính', description: 'Đối soát tiền thu hộ COD & phí vận chuyển 3 chiều giữa sổ cái nội bộ và bảng kê ĐVVC.', permission: 'inventory.cost.read', expressMode: true },
  { path: '/cost-valuation', label: 'Giá vốn & kết xuất MISA', section: 'Tài chính', description: 'Định giá tồn kho bình quân gia quyền, lãi gộp chủ shop, kết xuất MISA AMIS và HĐĐT nháp NĐ 70/2025.', permission: 'inventory.cost.read', expressMode: true },
  { path: '/partner-debts', label: 'Công nợ AP / AR', section: 'Tài chính', description: 'Hóa đơn công nợ phải trả (AP), phải thu (AR), thanh toán và sổ cái công nợ bất biến.', permission: 'partner.read', expressMode: false },

  // 6. Quản trị
  { path: '/onboarding-wizard', label: 'Thiết lập kho nhanh 5 bước', section: 'Quản trị', description: 'Khởi tạo thuê bao SaaS tự phục vụ, chọn mô hình kệ mẫu 1-Click và nhập Excel tồn đầu kỳ trong 15 phút.', permission: 'permission.read', expressMode: true },
  { path: '/saas-billing', label: 'Gói cước SaaS & VietQR', section: 'Quản trị', description: 'Quản lý gói cước thuê bao SaaS, hạn mức 3 nấc và thanh toán tự động VietQR.', permission: 'permission.read', expressMode: true },
  { path: '/products', label: 'Sản phẩm', section: 'Quản trị', description: 'SKU, danh mục, đơn vị tính và mã vạch của sản phẩm.', permission: 'product.read', expressMode: true },
  { path: '/warehouses', label: 'Kho hàng', section: 'Quản trị', description: 'Danh mục kho và trạng thái hoạt động.', permission: 'warehouse.read', expressMode: true },
  { path: '/warehouse-structure', label: 'Cấu trúc vị trí', section: 'Quản trị', description: 'Khu, dãy kệ, kệ, tầng và ô/vị trí theo phạm vi kho được phép.', permission: 'location.read', expressMode: false },
  { path: '/warehouse-map', label: 'Bản đồ kho', section: 'Quản trị', description: 'Bố cục vật lý, mức sử dụng và bản đồ nhiệt vận hành theo phạm vi kho được phép.', permission: 'location.read', expressMode: false },
  { path: '/warehouse-calendar', label: 'Lịch & ca kho', section: 'Quản trị', description: 'Múi giờ, lịch tuần, giờ chốt và năng lực theo ca vận hành.', permission: 'warehouse.read', expressMode: false },
  { path: '/units', label: 'Đơn vị tính', section: 'Quản trị', description: 'Danh mục đơn vị tính dùng trong chứng từ và tồn kho.', permission: 'uom.read', expressMode: true },
  { path: '/business-partners', label: 'Đối tác', section: 'Quản trị', description: 'Nhà cung cấp, khách hàng và thông tin liên hệ.', permission: 'partner.read', expressMode: true },
  { path: '/approvals', label: 'Phê duyệt', section: 'Quản trị', description: 'Hàng đợi phê duyệt theo SLA, chứng từ và phạm vi kho được phép.', access: 'approvals', expressMode: false },
  { path: '/permissions', label: 'Quản trị quyền', section: 'Quản trị', description: 'Danh mục quyền và cấp quyền theo vai trò.', permission: 'permission.read', expressMode: true },
];

export const productionSections = ['Tổng quan', 'Nhập kho', 'Xuất kho', 'Tồn kho', 'Tài chính', 'Quản trị'] as const;

const aliasProductionPages: Record<string, ProductionNavItem> = {
  '/categories': {
    path: '/categories',
    label: 'Danh mục sản phẩm',
    section: 'Quản trị',
    description: 'Phân loại nhóm hàng hóa, nguyên vật liệu và thành phẩm theo cấu trúc danh mục.',
    permission: 'product.read',
    expressMode: true,
  },
  '/misa-exports': {
    path: '/misa-exports',
    label: 'Giá vốn & kết xuất MISA',
    section: 'Tài chính',
    description: 'Định giá tồn kho bình quân gia quyền, lãi gộp chủ shop, kết xuất MISA AMIS và HĐĐT nháp NĐ 70/2025.',
    permission: 'inventory.cost.read',
    expressMode: true,
  },
};

export const resolveProductionPage = (pathname: string): ProductionNavItem | undefined => {
  if (pathname === '/') return productionNavigation[0];
  if (aliasProductionPages[pathname]) return aliasProductionPages[pathname];
  return productionNavigation
    .filter(item => item.path !== '/')
    .sort((a, b) => b.path.length - a.path.length)
    .find(item => pathname === item.path || pathname.startsWith(item.path + '/'));
};
