import axios from 'axios';

const resources: Record<string, string> = {
  approval: 'phê duyệt phiếu nhập', receipt: 'phiếu nhập', quality_inspection: 'kiểm tra chất lượng', quality_disposition: 'kết quả kiểm tra chất lượng',
  receiving_discrepancy: 'sai lệch nhận hàng', putaway: 'cất hàng', location: 'vị trí kho', warehouse: 'kho hàng', warehouse_zone: 'cấu trúc khu vực kho',
  product: 'sản phẩm', product_category: 'danh mục sản phẩm', product_barcode: 'mã vạch sản phẩm', product_uom: 'đơn vị quy đổi sản phẩm',
  uom: 'đơn vị tính', partner: 'đối tác', reason_code: 'mã lý do', quality_policy: 'chính sách chất lượng',
  receiving_tolerance_policy: 'chính sách dung sai nhận hàng', permission: 'quyền truy cập', role: 'vai trò', user: 'người dùng', user_warehouse: 'phạm vi kho của người dùng',
};
const actions: Record<string, string> = { read: 'Xem', create: 'Tạo', update: 'Cập nhật', deactivate: 'Ngừng sử dụng', manage: 'Quản lý',
  cancel: 'Hủy', receive: 'Nhận hàng trên', complete: 'Hoàn tất', post: 'Ghi nhận tồn kho từ', execute: 'Thực hiện',
  approve: 'Phê duyệt', reject: 'Từ chối', submit: 'Gửi xử lý', resolve: 'Xử lý', assign: 'Phân công' };
export const permissionLabel = (code: string) => {
  if (code === 'permission.assign') return 'Cấp và thu hồi quyền truy cập';
  const [resource, action] = code.split('.');
  return resources[resource] && actions[action] ? `${actions[action]} ${resources[resource]}` : 'Quyền chưa có diễn giải';
};
export const roleLabel = (name: string) => ({ Admin: 'Quản trị viên', Manager: 'Quản lý', Viewer: 'Người xem', WarehouseStaff: 'Nhân viên kho' }[name] || 'Vai trò khác');
export const permissionError = (error: unknown, fallback = 'Không thể xử lý yêu cầu. Vui lòng thử lại.') => {
  const status = axios.isAxiosError(error) ? error.response?.status : undefined;
  return ({ 401: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.', 403: 'Bạn không có quyền thực hiện thao tác này.',
    404: 'Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.', 409: 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.' } as Record<number, string>)[status || 0] || fallback;
};
