export type ProductionNavItem = {
  path: string;
  aliases?: string[];
  label: string;
  section: string;
  description: string;
  permission?: string;
  access?: 'always' | 'stocktake' | 'approvals';
  expressMode?: boolean;
};

export const productionSections = [
  'Điều hành',
  'Nhập kho',
  'Xuất kho',
  'Giao vận',
  'Hàng hoàn',
  'Tồn kho',
  'Sản phẩm',
  'Tài chính',
  'Cấu trúc kho',
  'Quản trị công ty',
] as const;

export const productionNavigation: ProductionNavItem[] = [
  // 1. Điều hành (1)
  { path: '/', label: 'Tổng quan vận hành', section: 'Điều hành', description: 'Trung tâm điều hành kho trực tuyến.', access: 'always', expressMode: true },

  // 2. Nhập kho (5)
  { path: '/dock-yard', label: 'Điều phối bãi xe', section: 'Nhập kho', description: 'Quản lý lịch xe và phân cửa bến bãi.', permission: 'dock_appointment.read', expressMode: false },
  { path: '/purchase-orders', label: 'Đơn mua hàng', section: 'Nhập kho', description: 'Theo dõi đơn mua hàng và dung sai.', permission: 'purchase_order.read', expressMode: false },
  { path: '/asns', label: 'Thông báo nhận hàng trước', section: 'Nhập kho', description: 'Lô hàng dự kiến đến kho kiểm nhận.', permission: 'asn.read', expressMode: false },
  { path: '/import-receipts', label: 'Phiếu nhập kho', section: 'Nhập kho', description: 'Kiểm nhận thực tế và ghi sổ nhập.', permission: 'receipt.read', expressMode: true },
  { path: '/putaway-tasks', aliases: ['/putaway'], label: 'Cất hàng vào kệ', section: 'Nhập kho', description: 'Cất hàng từ bãi nhận vào kệ đích.', permission: 'putaway.read', expressMode: false },

  // 3. Xuất kho (7 — theo luồng vật lý)
  { path: '/sales-orders', label: 'Đơn bán hàng', section: 'Xuất kho', description: 'Tiếp nhận đơn bán và giữ chỗ tồn.', permission: 'sales_order.read', expressMode: true },
  { path: '/backorders', label: 'Đơn thiếu hàng', section: 'Xuất kho', description: 'Theo dõi và cấp phát bù đơn thiếu.', permission: 'backorder.read', expressMode: true },
  { path: '/stock-reservations', label: 'Giữ hàng tồn kho', section: 'Xuất kho', description: 'Theo dõi lượng tồn cam kết giữ chỗ.', permission: 'inventory.read', expressMode: false },
  { path: '/stock-allocations', label: 'Phân bổ vị trí lấy', section: 'Xuất kho', description: 'Phân bổ vị trí lấy theo Ưu tiên Hạn dùng / Nhập trước.', permission: 'allocation.read', expressMode: false },
  { path: '/picking-tasks', label: 'Lấy hàng tại kệ', section: 'Xuất kho', description: 'Thực thi lấy hàng bằng quét mã vạch.', permission: 'picking.read', expressMode: false },
  { path: '/packing-sessions', label: 'Trạm đóng gói đơn hàng', section: 'Xuất kho', description: 'Đóng gói đơn hàng vào thùng carton.', permission: 'packing.read', expressMode: true },
  { path: '/export-receipts', label: 'Phiếu xuất kho', section: 'Xuất kho', description: 'Chứng từ xuất kho và xác nhận rời.', permission: 'export_receipt.read', expressMode: true },

  // 4. Giao vận (2)
  { path: '/shipments', label: 'Vận đơn giao hàng', section: 'Giao vận', description: 'Theo dõi vận đơn và trạng thái giao.', permission: 'shipment.read', expressMode: true },
  { path: '/handover-manifests', label: 'Biên bản bàn giao ĐVVC', section: 'Giao vận', description: 'Quét mã vận đơn bàn giao hãng vận.', permission: 'inventory.read', expressMode: true },

  // 5. Hàng hoàn (1)
  { path: '/customer-returns', label: 'Xử lý hàng hoàn RMA', section: 'Hàng hoàn', description: 'Tiếp nhận và kiểm định hàng hoàn.', permission: 'receipt.read', expressMode: true },

  // 6. Tồn kho (7)
  { path: '/inventory', label: 'Tra cứu tồn kho', section: 'Tồn kho', description: 'Tra cứu tồn khả dụng theo vị trí và lô.', permission: 'inventory.read', expressMode: true },
  { path: '/inventory-locks', label: 'Khóa tồn kho trạng thái', section: 'Tồn kho', description: 'Phong tỏa và mở khóa lô hàng tồn.', permission: 'inventory_lock.read', expressMode: false },
  { path: '/inventory-movements', label: 'Chuyển vị trí nội bộ', section: 'Tồn kho', description: 'Di chuyển tồn giữa các kệ nội bộ.', permission: 'inventory.read', expressMode: false },
  { path: '/stock-transfers', label: 'Chuyển kho liên chi nhánh', section: 'Tồn kho', description: 'Luân chuyển hàng giữa các điểm kho.', permission: 'inventory.read', expressMode: false },
  { path: '/stocktakes', label: 'Kiểm kê kho định kỳ', section: 'Tồn kho', description: 'Đếm mù tại kệ và duyệt chênh lệch.', access: 'stocktake', expressMode: true },
  { path: '/inventory-reversals', label: 'Đảo bút toán kho', section: 'Tồn kho', description: 'Sinh giao dịch đảo sổ cái bất biến.', permission: 'inventory_ledger.read', expressMode: false },
  { path: '/inventory-reconciliation', label: 'Đối soát sổ cái tồn kho', section: 'Tồn kho', description: 'Đối soát cân bằng sổ cái và số dư.', permission: 'inventory_ledger.read', expressMode: false },

  // 7. Sản phẩm (6)
  { path: '/products', label: 'Danh mục sản phẩm', section: 'Sản phẩm', description: 'Quản lý hồ sơ SKU, mã vạch và lô.', permission: 'product.read', expressMode: true },
  { path: '/categories', label: 'Nhóm hàng hóa', section: 'Sản phẩm', description: 'Phân loại nhóm ngành hàng hóa kho.', permission: 'product.read', expressMode: true },
  { path: '/units', label: 'Đơn vị tính quy đổi', section: 'Sản phẩm', description: 'Đơn vị tính cơ sở và hệ số quy đổi.', permission: 'uom.read', expressMode: true },
  { path: '/kit-work-orders', aliases: ['/kitting'], label: 'Lệnh lắp ráp bộ Kit', section: 'Sản phẩm', description: 'Lập lệnh lắp ráp hoặc xả bộ Kit.', permission: 'packing.read', expressMode: true },
  { path: '/inventory-traceability', label: 'Truy vết lô sê-ri', section: 'Sản phẩm', description: 'Truy vết phả hệ lô hàng và sê-ri.', permission: 'inventory_traceability.read', expressMode: false },
  { path: '/business-partners', label: 'Danh mục đối tác', section: 'Sản phẩm', description: 'Hồ sơ nhà cung cấp và khách hàng.', permission: 'partner.read', expressMode: true },

  // 8. Tài chính (5)
  { path: '/cod-reconciliation', label: 'Đối soát thu hộ COD', section: 'Tài chính', description: 'Đối soát tiền thu hộ COD và cước.', permission: 'inventory.cost.read', expressMode: true },
  { path: '/cost-valuation', label: 'Định giá tồn kho bình quân', section: 'Tài chính', description: 'Tính giá vốn bình quân gia quyền.', permission: 'inventory.cost.read', expressMode: true },
  { path: '/profit-report', label: 'Báo cáo đối soát lợi nhuận', section: 'Tài chính', description: 'Đối soát lãi gộp, phí sàn, lãi ròng', permission: 'inventory.cost.read', expressMode: true },
  { path: '/misa-exports', aliases: ['/accounting-export'], label: 'Kết xuất chứng từ kế toán', section: 'Tài chính', description: 'Kết xuất bảng kê kế toán và HĐĐT.', permission: 'inventory.cost.read', expressMode: true },
  { path: '/partner-debts', label: 'Công nợ đối tác', section: 'Tài chính', description: 'Quản lý công nợ phải thu phải trả.', permission: 'partner.read', expressMode: false },

  // 9. Cấu trúc kho (4)
  { path: '/warehouses', label: 'Danh sách kho hàng', section: 'Cấu trúc kho', description: 'Danh sách điểm kho và địa chỉ kho.', permission: 'warehouse.read', expressMode: true },
  { path: '/warehouse-structure', label: 'Sơ đồ vị trí kệ', section: 'Cấu trúc kho', description: 'Thiết lập Khu vực, Dãy, Kệ và Ô.', permission: 'location.read', expressMode: false },
  { path: '/warehouse-map', label: 'Bản đồ nhiệt 2D', section: 'Cấu trúc kho', description: 'Sơ đồ bố cục và mật độ lấp đầy kệ.', permission: 'location.read', expressMode: false },
  { path: '/warehouse-calendar', label: 'Lịch làm việc kho', section: 'Cấu trúc kho', description: 'Lịch làm việc tuần và giờ chốt ca.', permission: 'warehouse.read', expressMode: false },

  // 10. Quản trị công ty (7)
  { path: '/approvals', label: 'Phê duyệt chứng từ', section: 'Quản trị công ty', description: 'Phê duyệt điều chỉnh và xuất hủy.', access: 'approvals', expressMode: false },
  { path: '/users', label: 'Quản lý người dùng', section: 'Quản trị công ty', description: 'Quản trị tài khoản và khóa bảo mật.', permission: 'permission.read', expressMode: true },
  { path: '/roles', label: 'Vai trò phân quyền', section: 'Quản trị công ty', description: 'Thiết lập vai trò và ma trận quyền.', permission: 'permission.read', expressMode: true },
  { path: '/warehouse-scopes', label: 'Phạm vi kho nhân sự', section: 'Quản trị công ty', description: 'Phân quyền truy cập theo điểm kho.', permission: 'permission.read', expressMode: true },
  { path: '/account-security', label: 'Bảo mật tài khoản 2FA', section: 'Quản trị công ty', description: 'Xác thực 2 lớp và mã khôi phục.', permission: 'permission.read', expressMode: false },
  { path: '/saas-billing', label: 'Gói thuê bao SaaS', section: 'Quản trị công ty', description: 'Theo dõi gói cước và hạn mức SaaS.', permission: 'permission.read', expressMode: true },
  { path: '/onboarding-wizard', label: 'Khởi tạo doanh nghiệp', section: 'Quản trị công ty', description: 'Thiết lập kho mẫu nhanh 5 bước.', permission: 'permission.read', expressMode: true },
];

const aliasProductionPages: Record<string, ProductionNavItem> = {
  '/putaway': { path: '/putaway-tasks', aliases: ['/putaway'], label: 'Cất hàng vào kệ', section: 'Nhập kho', description: 'Cất hàng từ bãi nhận vào kệ đích.', permission: 'putaway.read', expressMode: false },
  '/accounting-export': { path: '/misa-exports', aliases: ['/accounting-export'], label: 'Kết xuất chứng từ kế toán', section: 'Tài chính', description: 'Kết xuất bảng kê kế toán và HĐĐT.', permission: 'inventory.cost.read', expressMode: true },
  '/kitting': { path: '/kit-work-orders', aliases: ['/kitting'], label: 'Lệnh lắp ráp bộ Kit', section: 'Sản phẩm', description: 'Lập lệnh lắp ráp hoặc xả bộ Kit.', permission: 'packing.read', expressMode: true },
  '/outbound-demand': { path: '/sales-orders', label: 'Đơn bán hàng', section: 'Xuất kho', description: 'Tiếp nhận đơn bán và giữ chỗ tồn.', permission: 'sales_order.read', expressMode: true },
  '/permissions': { path: '/permissions', label: 'Quản trị quyền truy cập', section: 'Quản trị công ty', description: 'Thiết lập vai trò và ma trận quyền.', permission: 'permission.read', expressMode: false },
  '/partner-aging': { path: '/partner-debts', label: 'Công nợ đối tác', section: 'Tài chính', description: 'Quản lý công nợ phải thu phải trả.', permission: 'partner.read', expressMode: false },
  '/ecommerce-integrations': { path: '/onboarding-wizard', label: 'Tích hợp sàn TMĐT', section: 'Quản trị công ty', description: 'Kết nối sàn TMĐT và đồng bộ đơn.', permission: 'permission.read', expressMode: true },
};

export const resolveProductionPage = (pathname: string): ProductionNavItem | undefined => {
  if (pathname === '/') return productionNavigation[0];
  if (aliasProductionPages[pathname]) return aliasProductionPages[pathname];
  return productionNavigation
    .filter(item => item.path !== '/')
    .sort((a, b) => b.path.length - a.path.length)
    .find(item =>
      pathname === item.path ||
      pathname.startsWith(item.path + '/') ||
      Boolean(item.aliases?.some(alias => pathname === alias || pathname.startsWith(alias + '/'))),
    );
};
