import React from 'react';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../../ui/ProductionUi';
import type { BusinessPartner } from './BusinessPartnerComponents';

interface BusinessPartnerListSectionProps {
  items: BusinessPartner[];
  loading: boolean;
  canManage: boolean;
  page: number;
  pages: number;
  setPage: React.Dispatch<React.SetStateAction<number>>;
  search: string;
  setSearch: React.Dispatch<React.SetStateAction<string>>;
  role: string;
  setRole: React.Dispatch<React.SetStateAction<string>>;
  active: string;
  setActive: React.Dispatch<React.SetStateAction<string>>;
  activeSuppliersCount: number;
  activeCustomersCount: number;
  dualRolePartnersCount: number;
  addressDeclaredCount: number;
  missingContactInfoCount: number;
  openEdit: (x: BusinessPartner) => void;
  remove: (x: BusinessPartner) => void;
}

export function BusinessPartnerListSection({
  items,
  loading,
  canManage,
  page,
  pages,
  setPage,
  search,
  setSearch,
  role,
  setRole,
  active,
  setActive,
  activeSuppliersCount,
  activeCustomersCount,
  dualRolePartnersCount,
  addressDeclaredCount,
  missingContactInfoCount,
  openEdit,
  remove,
}: BusinessPartnerListSectionProps) {
  return (
    <>
      <UiCard title="Tổng quan Hồ sơ Nhà cung cấp & Khách hàng">
        <UiMetricGrid>
          <UiMetric value={items.length} label="Tổng đối tác trên trang" />
          <UiMetric value={activeSuppliersCount} label="NCC đang hoạt động" />
          <UiMetric value={activeCustomersCount} label="KH đang hoạt động" />
          <UiMetric value={dualRolePartnersCount} label="Đối tác kiêm nhiệm (NCC & KH)" />
          <UiMetric value={addressDeclaredCount} label="Đã chuẩn hóa địa chỉ giao nhận" />
          <UiMetric value={missingContactInfoCount} label="Thiếu thông tin liên hệ" />
        </UiMetricGrid>
        <div style={{ marginTop: 10, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, minWidth: 0, maxWidth: '100%' }}>
          <span className="ui-badge success" style={{ whiteSpace: 'normal', maxWidth: '100%', flexShrink: 1, lineHeight: 1.35, overflowWrap: 'anywhere' }}>
            Thanh toán không tiền mặt &gt;= 5.000.000 ₫ (Luật Thuế GTGT 48/2024/QH15 &amp; NĐ 181/2025/NĐ-CP)
          </span>
          <span style={{ fontSize: 12.5, color: '#334155', overflowWrap: 'anywhere' }}>
            Chuẩn hóa Mã số thuế (10/13 số) / Mã định danh CCCD (12 số - TT 86/2024/TT-BTC) &amp; Tài khoản ngân hàng thụ hưởng.
          </span>
        </div>
      </UiCard>

      <UiToolbar>
        <UiToolbarField label="Tìm đối tác">
          <input aria-label="Tìm đối tác" placeholder="Mã hoặc tên" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
        </UiToolbarField>
        <UiToolbarField label="Vai trò">
          <select aria-label="Lọc vai trò" value={role} onChange={event => { setRole(event.target.value); setPage(1); }}>
            <option value="">Mọi vai trò</option>
            <option value="supplier">Nhà cung cấp</option>
            <option value="customer">Khách hàng</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Trạng thái">
          <select aria-label="Lọc trạng thái" value={active} onChange={event => { setActive(event.target.value); setPage(1); }}>
            <option value="">Mọi trạng thái</option>
            <option value="true">Hoạt động</option>
            <option value="false">Ngừng hoạt động</option>
          </select>
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">Trang {page}/{pages}</div>
      </UiToolbar>

      <UiCard title="Danh sách đối tác">
        {loading ? <p role="status">Đang tải đối tác...</p> : <UiTableScroll><table className="ui-table-compact" aria-label="Danh sách đối tác kinh doanh">
          <thead><tr><th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Mã đối tác</th><th style={{ minWidth: 160 }}>Tên đối tác</th><th style={{ minWidth: 145, whiteSpace: 'nowrap' }}>Vai trò</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 145, whiteSpace: 'nowrap' }}>Công nợ hiện tại</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 140, whiteSpace: 'nowrap' }}>Hạn mức &amp; Kỳ hạn</th><th style={{ minWidth: 130 }}>Liên hệ</th><th style={{ minWidth: 105, whiteSpace: 'nowrap' }}>Trạng thái</th>{canManage && <th style={{ minWidth: 110, whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
          <tbody>
            {items.length === 0
              ? <tr><td colSpan={canManage ? 8 : 7} className="ui-empty-cell">Không có đối tác phù hợp với bộ lọc hiện tại.</td></tr>
              : items.map(item => <tr key={item.id}>
                <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.code}</strong></td>
                <td>
                  <div>{item.name}</div>
                  {item.address && <small>{item.address}</small>}
                  {(item.taxCode || item.bankAccountNumber) && (
                    <div>
                      <small style={{ color: '#475569' }}>
                        {item.taxCode ? `MST/CCCD: ${item.taxCode}` : ''}
                        {item.bankAccountNumber ? ` • TK: ${item.bankAccountNumber}${item.bankName ? ` (${item.bankName})` : ''}` : ''}
                      </small>
                    </div>
                  )}
                </td>
                <td style={{ minWidth: 145, whiteSpace: 'nowrap' }}>
                  <div className="ui-inline-wrap" style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap' }}>
                    {item.isSupplier && <UiBadge>Nhà cung cấp</UiBadge>}
                    {item.isCustomer && <UiBadge>Khách hàng</UiBadge>}
                    {!item.isSupplier && !item.isCustomer && <span>—</span>}
                  </div>
                </td>
                <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 3, whiteSpace: 'nowrap' }}>
                    {item.isCustomer && (
                      <div style={{ whiteSpace: 'nowrap' }}><small>Phải thu KH: <strong>{Number(item.receivableBalance ?? 0).toLocaleString('vi-VN')} ₫</strong></small></div>
                    )}
                    {item.isSupplier && (
                      <div style={{ whiteSpace: 'nowrap' }}><small>Phải trả NCC: <strong>{Number(item.payableBalance ?? 0).toLocaleString('vi-VN')} ₫</strong></small></div>
                    )}
                    {item.isCustomer && Number(item.creditLimit ?? 0) > 0 && Number(item.receivableBalance ?? 0) > Number(item.creditLimit ?? 0) && (
                      <UiBadge tone="danger">Vượt hạn mức tín dụng</UiBadge>
                    )}
                    {item.isCustomer && Number(item.creditLimit ?? 0) > 0 && Number(item.receivableBalance ?? 0) >= Number(item.creditLimit ?? 0) * 0.8 && Number(item.receivableBalance ?? 0) <= Number(item.creditLimit ?? 0) && (
                      <UiBadge tone="warning">Cảnh báo công nợ (&ge; 80%)</UiBadge>
                    )}
                    {item.isCustomer && Number(item.creditLimit ?? 0) === 0 && Number(item.receivableBalance ?? 0) > 0 && (
                      <UiBadge tone="danger">Vượt hạn mức tín dụng</UiBadge>
                    )}
                  </div>
                </td>
                <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 3, whiteSpace: 'nowrap' }}>
                    <div style={{ whiteSpace: 'nowrap' }}><small>Hạn mức: <strong>{Number(item.creditLimit ?? 0) > 0 ? `${Number(item.creditLimit).toLocaleString('vi-VN')} ₫` : 'Không giới hạn'}</strong></small></div>
                    <div style={{ whiteSpace: 'nowrap' }}><small>Kỳ hạn TT: <strong>{item.paymentTermDays ?? 30} ngày</strong></small></div>
                  </div>
                </td>
                <td><div>{item.phone || '—'}</div>{item.email && <small>{item.email}</small>}</td>
                <td style={{ whiteSpace: 'nowrap' }}><UiBadge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                {canManage && (
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <div style={{ display: 'inline-flex', gap: '12px', alignItems: 'center', whiteSpace: 'nowrap' }}>
                      <button type="button" onClick={() => openEdit(item)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>
                      <button
                        type="button"
                        onClick={() => void remove(item)}
                        style={{ whiteSpace: 'nowrap', borderColor: '#dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }}
                      >
                        Xóa
                      </button>
                    </div>
                  </td>
                )}
              </tr>)}
          </tbody>
        </table></UiTableScroll>}
        <div className="ui-pagination" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, marginTop: 10 }}>
          <span style={{ fontSize: 12.5, color: '#475569' }}>
            Tổng số trên trang: <strong>{items.length}</strong> đối tác • Trang {page}/{pages} (tối đa 10 bản ghi/trang)
          </span>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <button type="button" disabled={page <= 1} title={page <= 1 ? 'Đang ở trang đầu tiên' : undefined} onClick={() => setPage(value => value - 1)}>Trước</button>
            <span style={{ fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>Trang {page}/{pages}</span>
            <button type="button" disabled={page >= pages} title={page >= pages ? 'Đang ở trang cuối cùng' : undefined} onClick={() => setPage(value => value + 1)}>Sau</button>
          </div>
        </div>
      </UiCard>
    </>
  );
}
