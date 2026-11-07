import { describe, expect, it } from 'vitest';
import {
  rmaConditionLabelVi,
  rmaDispositionLabelVi,
  rmaStatusLabelVi,
  taxpayerTypeLabelVi,
  warehouseLocationLabelVi,
  warehouseLocationTypeLabelVi,
} from './vietnameseEnumLabels';

const FORBIDDEN_RAW_ENGLISH =
  /\b(QUARANTINED|PENDING_INSPECTION|INSPECTED|COMPLETED|GOOD|DAMAGED_BOX|REPACK_REQUIRED|DEFECTIVE|PENDING|RESTOCK_AVAILABLE|REFURBISH_REPACK|RTV_VENDOR|SCRAP_DISPOSAL|RECEIVING|QUARANTINE|STORAGE|ENTERPRISE|HOUSEHOLD_INDIVIDUAL|INDIVIDUAL_CONSUMER)\b/;

describe('vietnameseEnumLabels — 100% Vietnamese operational enum dictionaries', () => {
  it('maps RMA statuses to canonical Vietnamese labels', () => {
    expect(rmaStatusLabelVi('QUARANTINED')).toBe('Đang biệt trữ');
    expect(rmaStatusLabelVi('PENDING_INSPECTION')).toBe('Chờ kiểm định QC');
    expect(rmaStatusLabelVi('INSPECTED')).toBe('Đã kiểm định');
    expect(rmaStatusLabelVi('COMPLETED')).toBe('Đã hoàn tất');
  });

  it('maps RMA physical conditions including DAMAGED_BOX and REPACK_REQUIRED', () => {
    expect(rmaConditionLabelVi('GOOD')).toBe('Nguyên vẹn');
    expect(rmaConditionLabelVi('DAMAGED_BOX')).toBe('Móp vỏ hộp (Cần thay vỏ)');
    expect(rmaConditionLabelVi('REPACK_REQUIRED')).toBe('Móp vỏ hộp (Cần thay vỏ)');
    expect(rmaConditionLabelVi('DEFECTIVE')).toBe('Lỗi kỹ thuật / Hư hỏng');
  });

  it('maps RMA 4-way dispositions and pending state to canonical Vietnamese labels', () => {
    expect(rmaDispositionLabelVi('PENDING')).toBe('Chờ quyết định');
    expect(rmaDispositionLabelVi('RESTOCK_AVAILABLE')).toBe('Nhập lại tồn khả dụng');
    expect(rmaDispositionLabelVi('REFURBISH_REPACK')).toBe('Tân trang & Đóng gói lại');
    expect(rmaDispositionLabelVi('RTV_VENDOR')).toBe('Xuất trả NCC (RTV)');
    expect(rmaDispositionLabelVi('SCRAP_DISPOSAL')).toBe('Xuất hủy');
  });

  it('maps warehouse location types and codes without leaking raw English enums', () => {
    expect(warehouseLocationTypeLabelVi('RECEIVING')).toBe('Khu tiếp nhận');
    expect(warehouseLocationTypeLabelVi('Receiving')).toBe('Khu tiếp nhận');
    expect(warehouseLocationTypeLabelVi('QUARANTINE')).toBe('Khu biệt trữ RMA');
    expect(warehouseLocationTypeLabelVi('STORAGE')).toBe('Kệ lưu trữ');
    expect(warehouseLocationLabelVi('RECEIVING')).toBe('Khu tiếp nhận');
    expect(warehouseLocationLabelVi('QUARANTINE')).toBe('Khu biệt trữ RMA');
    expect(warehouseLocationLabelVi('QUARANTINE-01')).toBe('Khu biệt trữ RMA (Q-01)');
    expect(warehouseLocationLabelVi('STORAGE')).toBe('Kệ lưu trữ');
    expect(warehouseLocationLabelVi('A-01-01')).toBe('A-01-01');
  });

  it('maps taxpayer types to clean Vietnamese labels without English enum prefixes', () => {
    expect(taxpayerTypeLabelVi('ENTERPRISE')).toBe('Doanh nghiệp / Tổ chức (MST 10 hoặc 13 số)');
    expect(taxpayerTypeLabelVi('HOUSEHOLD_INDIVIDUAL')).toBe('Hộ / Cá nhân kinh doanh (CCCD 12 số)');
    expect(taxpayerTypeLabelVi('INDIVIDUAL_CONSUMER')).toBe('Cá nhân tiêu dùng');
  });

  it('never leaks raw uppercase English enum tokens across any helper', () => {
    const outputs = [
      rmaStatusLabelVi('QUARANTINED'),
      rmaStatusLabelVi('PENDING_INSPECTION'),
      rmaStatusLabelVi('INSPECTED'),
      rmaStatusLabelVi('COMPLETED'),
      rmaConditionLabelVi('GOOD'),
      rmaConditionLabelVi('DAMAGED_BOX'),
      rmaConditionLabelVi('REPACK_REQUIRED'),
      rmaConditionLabelVi('DEFECTIVE'),
      rmaDispositionLabelVi('PENDING'),
      rmaDispositionLabelVi('RESTOCK_AVAILABLE'),
      rmaDispositionLabelVi('REFURBISH_REPACK'),
      rmaDispositionLabelVi('RTV_VENDOR'),
      rmaDispositionLabelVi('SCRAP_DISPOSAL'),
      warehouseLocationLabelVi('RECEIVING'),
      warehouseLocationLabelVi('QUARANTINE'),
      warehouseLocationLabelVi('QUARANTINE-01'),
      warehouseLocationLabelVi('STORAGE'),
      taxpayerTypeLabelVi('ENTERPRISE'),
      taxpayerTypeLabelVi('HOUSEHOLD_INDIVIDUAL'),
      taxpayerTypeLabelVi('INDIVIDUAL_CONSUMER'),
    ];
    for (const label of outputs) {
      expect(label).not.toMatch(FORBIDDEN_RAW_ENGLISH);
    }
  });
});
