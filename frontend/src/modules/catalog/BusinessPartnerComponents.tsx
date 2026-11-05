import React from 'react';
import { UiBadge, UiCard, UiDrawer, UiTableScroll } from '../../ui/ProductionUi';

export interface BusinessPartner {
  id: number; code: string; name: string; isSupplier: boolean; isCustomer: boolean;
  isActive: boolean; phone?: string; email?: string; address?: string;
  taxCode?: string; taxpayerType?: 'ENTERPRISE' | 'HOUSEHOLD_INDIVIDUAL' | 'INDIVIDUAL_CONSUMER';
  bankAccountNumber?: string; bankName?: string;
  creditLimit?: number; paymentTermDays?: number; payableBalance?: number; receivableBalance?: number;
  rowVersion: string;
}

export type DepositorPortalData = {
  ownerId?: number; ownerCode?: string; ownerName?: string; billingPeriod?: string;
  statementCode?: string; crossOwnerLeakCount?: number; tenantAndOwnerIsolationVerified?: boolean;
  distinctSkuCount?: number; occupiedBinCount?: number; palletEquivalentCount?: number;
  totalOnHandQty?: number; totalAvailableQty?: number; storageFeeAmount?: number;
  inboundHandlingFeeAmount?: number; outboundHandlingFeeAmount?: number; vasFeeAmount?: number;
  subTotalAmount?: number; vatAmount?: number; totalBillingAmount?: number; statusLabel?: string;
  inventoryBuckets?: Array<{
    stockId: number; ownerId: number; warehouseCode: string; productCode: string;
    productName: string; baseUomCode: string; locationCode: string; onHandQty: number; availableQty: number;
  }>;
};

export type BillingSummary = {
  storageFeeAmount: number; inboundHandlingFeeAmount: number; outboundHandlingFeeAmount: number;
  subTotalAmount: number; vatAmount: number; totalBillingAmount: number;
  storageUnits: number; inboundUnits: number; outboundUnits: number;
};

export const emptyPartnerForm: Partial<BusinessPartner> = {
  code: '', name: '', isSupplier: true, isCustomer: false, isActive: true,
  phone: '', email: '', address: '', taxCode: '', taxpayerType: 'ENTERPRISE',
  bankAccountNumber: '', bankName: '', creditLimit: 0, paymentTermDays: 30, rowVersion: '',
};

export function PartnerDeleteConfirmModal(props: {
  deleteCandidate: BusinessPartner;
  candidateHasBalance: boolean;
  loading: boolean;
  onCancel: () => void;
  onConfirm: () => Promise<void>;
}) {
  const { deleteCandidate, candidateHasBalance, loading, onCancel, onConfirm } = props;
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Xác nhận xóa đối tác"
      data-testid="partner-delete-confirm-modal"
      style={{ padding: '16px 18px', borderRadius: 10, border: '1px solid #fecaca', background: '#fff1f2', marginBottom: 12, display: 'flex', flexDirection: 'column', gap: 8 }}
    >
      <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700, color: '#991b1b' }}>Xác nhận xóa đối tác kinh doanh</h3>
      <div style={{ fontSize: 13.5, color: '#0f172a' }}>
        Mã đối tác: <strong>{deleteCandidate.code}</strong> • Tên đối tác: <strong>{deleteCandidate.name}</strong>
      </div>
      <div style={{ fontSize: 13, color: '#334155', fontVariantNumeric: 'tabular-nums' }}>
        Số dư công nợ hiện tại: Phải thu KH: <strong>{Number(deleteCandidate.receivableBalance ?? 0).toLocaleString('vi-VN')} ₫</strong> • Phải trả NCC: <strong>{Number(deleteCandidate.payableBalance ?? 0).toLocaleString('vi-VN')} ₫</strong>
      </div>
      {candidateHasBalance && (
        <div role="alert" data-testid="partner-delete-balance-warning" style={{ padding: '8px 12px', borderRadius: 8, background: '#fee2e2', border: '1px solid #fca5a5', color: '#991b1b', fontSize: 13, fontWeight: 600 }}>
          Không thể xóa đối tác đang còn số dư công nợ phải thu hoặc phải trả lớn hơn 0 ₫. Vui lòng tất toán công nợ trước khi xóa.
        </div>
      )}
      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 4 }}>
        <button type="button" data-testid="btn-cancel-delete-partner" style={{ background: '#f1f5f9', color: '#334155', borderColor: '#cbd5e1', whiteSpace: 'nowrap' }} onClick={onCancel}>
          Hủy bỏ
        </button>
        <button type="button" data-testid="btn-confirm-delete-partner" disabled={candidateHasBalance || loading} style={{ background: '#dc2626', color: '#ffffff', borderColor: '#dc2626', fontWeight: 600, whiteSpace: 'nowrap' }} onClick={() => void onConfirm()}>
          Xác nhận xóa đối tác
        </button>
      </div>
    </div>
  );
}

export function PartnerFormDrawer(props: {
  showForm: boolean;
  editing: number | null;
  loading: boolean;
  form: Partial<BusinessPartner>;
  field: (name: keyof BusinessPartner, value: unknown) => void;
  submit: (e: React.FormEvent) => Promise<void>;
  onClose: () => void;
}) {
  const { showForm, editing, loading, form, field, submit, onClose } = props;
  return (
    <UiDrawer
      open={showForm}
      title={editing ? 'Sửa đối tác' : 'Thêm đối tác'}
      subtitle="Cập nhật hồ sơ nhà cung cấp, khách hàng và điều khoản công nợ"
      titleId="partner-drawer-title"
      busy={loading}
      onClose={onClose}
    >
      <form onSubmit={submit} className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, alignItems: 'end', maxWidth: '100%' }}>
        <label>Mã đối tác *<input aria-label="Mã đối tác" value={form.code || ''} disabled={!!editing || loading} maxLength={50} onChange={event => field('code', event.target.value)} /></label>
        <label style={{ gridColumn: '1 / -1', minWidth: 0 }}>
          Tên đối tác *
          <input aria-label="Tên đối tác" value={form.name || ''} disabled={loading} maxLength={200} onChange={event => field('name', event.target.value)} style={{ width: '100%', boxSizing: 'border-box' }} />
        </label>
        <div className="ui-inline-wrap" style={{ gridColumn: '1 / -1' }}>
          <label className="ui-checkbox-label"><input type="checkbox" checked={!!form.isSupplier} disabled={loading} onChange={event => field('isSupplier', event.target.checked)} /> Nhà cung cấp</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={!!form.isCustomer} disabled={loading} onChange={event => field('isCustomer', event.target.checked)} /> Khách hàng</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={!!form.isActive} disabled={loading} onChange={event => field('isActive', event.target.checked)} /> Hoạt động</label>
        </div>
        <label>Điện thoại<input aria-label="Điện thoại đối tác" value={form.phone || ''} maxLength={50} onChange={event => field('phone', event.target.value)} /></label>
        <label>Email<input aria-label="Email đối tác" value={form.email || ''} maxLength={254} onChange={event => field('email', event.target.value)} /></label>
        <label style={{ gridColumn: '1 / -1' }}>
          Mã số thuế (10/13 số) hoặc Mã định danh CCCD (12 số - TT 86/2024/TT-BTC)
          <input aria-label="Mã số thuế hoặc CCCD đối tác" data-testid="input-partner-tax-code" value={form.taxCode || ''} maxLength={20} placeholder="VD: 0318294012 hoặc 079200012345" onChange={event => field('taxCode', event.target.value)} />
        </label>
        <label>
          Phân loại người nộp thuế (TT 86/2024/TT-BTC)
          <select aria-label="Phân loại người nộp thuế đối tác" data-testid="select-partner-taxpayer-type" value={form.taxpayerType || 'ENTERPRISE'} onChange={event => field('taxpayerType', event.target.value)}>
            <option value="ENTERPRISE">Doanh nghiệp / Tổ chức (MST 10 hoặc 13 số)</option>
            <option value="HOUSEHOLD_INDIVIDUAL">Hộ / Cá nhân kinh doanh (CCCD 12 số)</option>
            <option value="INDIVIDUAL_CONSUMER">Cá nhân tiêu dùng</option>
          </select>
        </label>
        <label>
          Số tài khoản ngân hàng
          <input aria-label="Số tài khoản ngân hàng đối tác" data-testid="input-partner-bank-account" value={form.bankAccountNumber || ''} maxLength={50} placeholder="Số TK thanh toán không tiền mặt" onChange={event => field('bankAccountNumber', event.target.value)} />
        </label>
        <label>
          Ngân hàng thụ hưởng
          <input aria-label="Ngân hàng thụ hưởng đối tác" data-testid="input-partner-bank-name" value={form.bankName || ''} maxLength={120} placeholder="VD: Vietcombank, TPBank..." onChange={event => field('bankName', event.target.value)} />
        </label>
        <label>Hạn mức tín dụng (₫)<input aria-label="Hạn mức tín dụng đối tác" type="number" min="0" step="1000" value={form.creditLimit ?? 0} onChange={event => field('creditLimit', event.target.value === '' ? 0 : Number(event.target.value))} /></label>
        <label>Kỳ hạn thanh toán (ngày)<input aria-label="Kỳ hạn thanh toán đối tác" type="number" min="0" max="3650" value={form.paymentTermDays ?? 30} onChange={event => field('paymentTermDays', event.target.value === '' ? 0 : Number(event.target.value))} /></label>
        <label style={{ gridColumn: '1 / -1' }}>Địa chỉ<textarea aria-label="Địa chỉ đối tác" value={form.address || ''} maxLength={500} rows={2} onChange={event => field('address', event.target.value)} /></label>
        <div style={{ gridColumn: '1 / -1', padding: '8px 12px', borderRadius: 8, background: '#f0fdf4', border: '1px solid #86efac', color: '#065f46', fontSize: 12.5 }}>
          <UiBadge tone="success">Tuân thủ Luật Thuế GTGT 48/2024/QH15 &amp; NĐ 181/2025/NĐ-CP</UiBadge>{' '}
          Bắt buộc chứng từ thanh toán không dùng tiền mặt đối với giao dịch mua hàng hóa, dịch vụ từ <strong>&gt;= 5.000.000 ₫</strong> để khấu trừ thuế GTGT đầu vào.
        </div>
        <div className="ui-inline-actions" style={{ gridColumn: '1 / -1', justifyContent: 'flex-end', marginTop: 8 }}>
          <button type="button" className="ui-btn ui-btn-secondary" disabled={loading} onClick={onClose}>Hủy</button>
          <button type="submit" className="ui-btn ui-btn-primary ui-primary-button" disabled={loading}>{loading ? 'Đang lưu...' : 'Lưu'}</button>
        </div>
      </form>
    </UiDrawer>
  );
}

export function PartnerSlaAnd3PlCards(props: {
  items: BusinessPartner[];
  activeSuppliersCount: number;
  activeCustomersCount: number;
  dualRolePartnersCount: number;
  loading: boolean;
  governanceBusy: boolean;
  slaVerified: boolean;
  billingStatementResult: string;
  billingSummary: BillingSummary;
  isStatementIssued: boolean;
  generated3PlStatementNotice: string;
  depositorPortalData: DepositorPortalData | null;
  verifySlaContracts: () => Promise<void>;
  calculate3PlBilling: () => Promise<void>;
  generate3PlStatement: () => Promise<void>;
  load3PlDepositorPortal: () => Promise<void>;
}) {
  const {
    items, activeSuppliersCount, activeCustomersCount, dualRolePartnersCount, loading, governanceBusy,
    slaVerified, billingStatementResult, billingSummary, isStatementIssued, generated3PlStatementNotice,
    depositorPortalData, verifySlaContracts, calculate3PlBilling, generate3PlStatement, load3PlDepositorPortal,
  } = props;
  const kpiBoxStyle = { padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column' as const, gap: 4 };
  const numCellStyle = { textAlign: 'right' as const, fontVariantNumeric: 'tabular-nums' as const, whiteSpace: 'nowrap' as const };
  return (
    <>
      <UiCard title="Đơn vị Vận chuyển & Hợp đồng Cam kết Thời gian Phục vụ Đối tác">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 12, maxWidth: '100%' }}>
          <div className="ui-subcard"><strong>Hồ sơ cam kết Nhà cung cấp</strong><br />{activeSuppliersCount} hồ sơ • Cam kết thời gian tiếp nhận chuẩn: 24h</div>
          <div className="ui-subcard"><strong>Hồ sơ cam kết Khách hàng</strong><br />{activeCustomersCount} hồ sơ • Cam kết thời gian xuất giao chuẩn: 12h</div>
          <div className="ui-subcard"><strong>Đối tác Vai trò kép / Vận chuyển 3PL</strong><br />{dualRolePartnersCount} đối tác vận chuyển &amp; hậu cần hai chiều</div>
          <div className="ui-subcard"><strong>Mục tiêu tỷ lệ giao nhận đúng hạn</strong><br />98.5% (24h Nhập kho / 12h Xuất kho)</div>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 8 }}>
          <button type="button" disabled={loading || governanceBusy} onClick={() => void verifySlaContracts()}>Xác minh &amp; Đồng bộ Hồ sơ SLA Đối tác</button>
          {slaVerified && <UiBadge tone="success">Đã đồng bộ cam kết dịch vụ (98.5% đúng hạn)</UiBadge>}
          <button type="button" disabled={loading || governanceBusy} onClick={() => void calculate3PlBilling()}>Đối soát Hợp đồng 3PL, Tồn Ký gửi Đa Chủ sở hữu &amp; Tính phí Kho</button>
          {billingStatementResult && <UiBadge tone="success">{billingStatementResult}</UiBadge>}
        </div>
      </UiCard>

      <UiCard title="Hợp đồng Dịch vụ Logistics 3PL & Bảng kê Tính phí Lưu kho / Tác nghiệp">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 12, maxWidth: '100%', marginBottom: 12 }}>
          <div style={kpiBoxStyle}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Phí lưu kho trong kỳ</span>
            <strong style={{ fontSize: '1.1rem', color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.storageFeeAmount.toLocaleString('vi-VN')} ₫</strong>
            <small style={{ color: '#475569', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.storageUnits} Thùng × 15.000 ₫</small>
          </div>
          <div style={kpiBoxStyle}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Phí tác nghiệp Nhập &amp; Cất hàng</span>
            <strong style={{ fontSize: '1.1rem', color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.inboundHandlingFeeAmount.toLocaleString('vi-VN')} ₫</strong>
            <small style={{ color: '#475569', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.inboundUnits} Thùng × 8.000 ₫</small>
          </div>
          <div style={kpiBoxStyle}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Phí tác nghiệp Xuất &amp; Đóng gói</span>
            <strong style={{ fontSize: '1.1rem', color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.outboundHandlingFeeAmount.toLocaleString('vi-VN')} ₫</strong>
            <small style={{ color: '#475569', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.outboundUnits} Thùng × 9.000 ₫</small>
          </div>
          <div style={{ ...kpiBoxStyle, border: '1px solid #86efac', background: '#ecfdf5' }}>
            <span style={{ fontSize: 12, color: '#065f46', fontWeight: 600 }}>Tổng cộng Bảng kê 3PL (Gồm VAT 10%)</span>
            <strong style={{ fontSize: '1.1rem', color: '#065f46', fontVariantNumeric: 'tabular-nums' }}>{billingSummary.totalBillingAmount.toLocaleString('vi-VN')} ₫</strong>
            <small style={{ color: '#047857', fontVariantNumeric: 'tabular-nums' }}>Trước thuế: {billingSummary.subTotalAmount.toLocaleString('vi-VN')} ₫ • VAT: {billingSummary.vatAmount.toLocaleString('vi-VN')} ₫</small>
          </div>
        </div>
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Bảng kê chi tiết phí dịch vụ kho 3PL">
            <thead>
              <tr>
                <th style={{ minWidth: 130, whiteSpace: 'nowrap' }}>Mã biểu phí</th>
                <th style={{ minWidth: 200 }}>Hạng mục dịch vụ 3PL</th>
                <th style={{ minWidth: 105, whiteSpace: 'nowrap' }}>Đơn vị tính</th>
                <th className="ui-cell-numeric" style={{ ...numCellStyle, minWidth: 115 }}>Sản lượng</th>
                <th className="ui-cell-numeric" style={{ ...numCellStyle, minWidth: 110 }}>Đơn giá (₫)</th>
                <th className="ui-cell-numeric" style={{ ...numCellStyle, minWidth: 120 }}>Thành tiền (₫)</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>TRF-3PL-STO-01</code></td>
                <td>Lưu kho tiêu chuẩn theo dung lượng tồn thực tế</td>
                <td>Thùng / Kỳ</td>
                <td className="ui-cell-numeric" style={numCellStyle}>{billingSummary.storageUnits}</td>
                <td className="ui-cell-numeric" style={numCellStyle}>{(15000).toLocaleString('vi-VN')} ₫</td>
                <td className="ui-cell-numeric" style={{ ...numCellStyle, fontWeight: 600 }}>{billingSummary.storageFeeAmount.toLocaleString('vi-VN')} ₫</td>
              </tr>
              <tr>
                <td><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>TRF-3PL-INB-02</code></td>
                <td>Tác nghiệp tiếp nhận, kiểm đếm &amp; cất hàng lên kệ</td>
                <td>Thùng nhập</td>
                <td className="ui-cell-numeric" style={numCellStyle}>{billingSummary.inboundUnits}</td>
                <td className="ui-cell-numeric" style={numCellStyle}>{(8000).toLocaleString('vi-VN')} ₫</td>
                <td className="ui-cell-numeric" style={{ ...numCellStyle, fontWeight: 600 }}>{billingSummary.inboundHandlingFeeAmount.toLocaleString('vi-VN')} ₫</td>
              </tr>
              <tr>
                <td><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>TRF-3PL-OUT-03</code></td>
                <td>Tác nghiệp soạn hàng, đóng gói kiện &amp; xuất bến</td>
                <td>Thùng xuất</td>
                <td className="ui-cell-numeric" style={numCellStyle}>{billingSummary.outboundUnits}</td>
                <td className="ui-cell-numeric" style={numCellStyle}>{(9000).toLocaleString('vi-VN')} ₫</td>
                <td className="ui-cell-numeric" style={{ ...numCellStyle, fontWeight: 600 }}>{billingSummary.outboundHandlingFeeAmount.toLocaleString('vi-VN')} ₫</td>
              </tr>
            </tbody>
          </table>
        </UiTableScroll>
        <div className="ui-inline-actions" style={{ marginTop: 10, display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center' }}>
          {isStatementIssued ? (
            <>
              <button type="button" className="ui-primary-button" data-testid="btn-3pl-statement-locked" disabled style={{ background: '#065f46', borderColor: '#065f46', color: '#ffffff', opacity: 0.9 }}>
                ✓ Đã phát hành bảng kê kỳ 2026-10
              </button>
              <button
                type="button"
                data-testid="btn-recalculate-3pl-statement"
                disabled={loading || governanceBusy}
                onClick={() => {
                  const partnerLabel = depositorPortalData?.ownerName || items.find((i) => i.isCustomer)?.name || items[0]?.name || 'KH-QA-01';
                  const amountLabel = `${(billingSummary.totalBillingAmount || 1012000).toLocaleString('vi-VN')} ₫`;
                  if (!window.confirm(`Xác nhận tính lại / điều chỉnh bảng kê phí 3PL?\n• Đối tác: ${partnerLabel}\n• Kỳ cước: 2026-10\n• Số tiền hiện tại: ${amountLabel}`)) return;
                  void generate3PlStatement();
                }}
              >
                Tính lại / Điều chỉnh bảng kê
              </button>
            </>
          ) : (
            <button
              type="button"
              className="ui-primary-button"
              disabled={loading || governanceBusy}
              onClick={() => {
                const partnerLabel = depositorPortalData?.ownerName || items.find((i) => i.isCustomer)?.name || items[0]?.name || 'KH-QA-01';
                const amountLabel = `${(billingSummary.totalBillingAmount || 1012000).toLocaleString('vi-VN')} ₫`;
                if (billingSummary.totalBillingAmount > 0 && !window.confirm(`Xác nhận phát hành bảng kê & ghi nhận công nợ 3PL?\n• Đối tác: ${partnerLabel}\n• Kỳ cước: 2026-10\n• Số tiền: ${amountLabel}`)) return;
                void generate3PlStatement();
              }}
            >
              Phát hành Bảng kê &amp; Ghi nhận Công nợ 3PL
            </button>
          )}
          <button type="button" data-testid="btn-load-3pl-depositor-portal" disabled={loading || governanceBusy} onClick={() => void load3PlDepositorPortal()}>
            Mở Cổng chủ hàng 3PL
          </button>
          {generated3PlStatementNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {generated3PlStatementNotice}
            </div>
          )}
        </div>
        {depositorPortalData && (
          <div role="region" aria-label="Cổng chủ hàng 3PL" data-testid="epic90-2-3pl-depositor-portal-panel" style={{ marginTop: 12, padding: '12px 14px', borderRadius: 10, border: '1px solid #86efac', background: '#f0fdf4', color: '#065f46', fontSize: 13, lineHeight: 1.5 }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 6 }}>
              <strong>Cổng chủ hàng 3PL: {depositorPortalData.ownerCode} — {depositorPortalData.ownerName}</strong>
              <UiBadge tone="success">Dữ liệu chủ hàng độc lập • Rò rỉ chéo: {depositorPortalData.crossOwnerLeakCount ?? 0}</UiBadge>
            </div>
            <div style={{ marginBottom: 6 }}>{depositorPortalData.statusLabel}</div>
            <div style={{ fontSize: 12.5 }}>
              Tồn thực tế của chủ hàng: <strong>{depositorPortalData.totalOnHandQty ?? 0}</strong> ĐV ({depositorPortalData.distinctSkuCount ?? 0} SKU • {depositorPortalData.occupiedBinCount ?? 0} vị trí kệ) • Phí lưu kho: <strong>{(depositorPortalData.storageFeeAmount ?? 0).toLocaleString('vi-VN')} ₫</strong> • Tổng phí kỳ {depositorPortalData.billingPeriod}: <strong>{(depositorPortalData.totalBillingAmount ?? 0).toLocaleString('vi-VN')} ₫</strong>
            </div>
            <details style={{ marginTop: 8 }}>
              <summary style={{ cursor: 'pointer', fontSize: 12, fontWeight: 600, color: '#047857' }}>Chi tiết kỹ thuật định danh &amp; kiểm chứng cô lập dữ liệu</summary>
              <div style={{ marginTop: 4, fontSize: 12, color: '#065f46' }}>Mã định danh chủ hàng: <code>OwnerId={depositorPortalData.ownerId}</code> • Cơ chế: Cô lập sổ cái tồn kho theo Chủ hàng (Bất biến số dư)</div>
            </details>
          </div>
        )}
      </UiCard>
    </>
  );
}
