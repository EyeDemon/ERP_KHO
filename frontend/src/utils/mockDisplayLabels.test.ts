import { describe, expect, it } from 'vitest';
import {
  mockDisplayText,
  mockOwnerLabel,
  mockStatusLabel,
  mockTypeLabel,
} from './mockDisplayLabels';

describe('mockDisplayLabels', () => {
  it('Việt hóa loại bản ghi và nhãn catalog thường gặp', () => {
    expect(mockTypeLabel('Global Search')).toBe('Tìm kiếm toàn hệ thống');
    expect(mockTypeLabel('Inventory Balance')).toBe('Số dư tồn kho');
    expect(mockDisplayText('Warehouse Map & Heatmap')).toBe('Bản đồ kho & bản đồ nhiệt');
    expect(mockDisplayText('Current usage')).toBe('Mức sử dụng hiện tại');
  });

  it('Việt hóa trạng thái nhưng giữ nguyên mã kỹ thuật chưa có ánh xạ', () => {
    expect(mockStatusLabel('IN_TRANSIT')).toBe('Đang vận chuyển');
    expect(mockStatusLabel('READY_TO_POST')).toBe('Sẵn sàng ghi sổ');
    expect(mockStatusLabel('CUSTOM_TECHNICAL_CODE')).toBe('CUSTOM_TECHNICAL_CODE');
  });

  it('Việt hóa tên tiến trình/người phụ trách hệ thống', () => {
    expect(mockOwnerLabel('Inventory Posting Engine')).toBe('Bộ máy ghi sổ tồn kho');
    expect(mockOwnerLabel('Warehouse Admin')).toBe('Quản trị kho');
  });

  it('Việt hóa mô tả hỗn hợp mà không làm mất mã nghiệp vụ', () => {
    expect(mockDisplayText('SKU-1001 • OnHand 1.250 / Available 1.030'))
      .toBe('SKU-1001 • Tồn thực tế 1.250 / Khả dụng 1.030');
    expect(mockDisplayText('Exact-first index • Product / Barcode / Document / Lot / Serial / Partner'))
      .toBe('Chỉ mục ưu tiên khớp chính xác • Sản phẩm / Mã vạch / Chứng từ / Lô / Sê-ri / Đối tác');
  });
});
