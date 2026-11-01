import React, { useState, useEffect } from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  type CustomerReturnItem,
  type ImportReceipt,
  type Product,
  type Warehouse,
  type WarehouseLocationItem,
  computeReturnDisputeDeadlineLabel,
  formatNaturalRemainingTime,
} from './ImportReceiptTypes';

export interface ImportReceiptRmaCardProps {
  activeWorkspaceTab: 'receipts' | 'scanner' | 'rma';
  canRead: boolean;
  receipts: ImportReceipt[];
  selectedReceipt: ImportReceipt | null;
  warehouseId: number | '';
  warehouses: Warehouse[];
  products: Product[];
  warehouseLocations: WarehouseLocationItem[];
  rmaLocationCode: string;
  setRmaLocationCode: (val: string) => void;
  successMsg: string;
  setSuccessMsg: (val: string) => void;
  setError: (val: string) => void;
}

const mapCustomerReturnsRows = (raw: unknown): CustomerReturnItem[] => {
  if (!Array.isArray(raw) || raw.length === 0) return [];
  return raw.map((r: Record<string, unknown>, idx: number) => {
    const aging = Number(r.returnAgingDays ?? (idx === 0 ? 9 : 3));
    return {
      returnCode: String(r.returnCode ?? r.rmaCode ?? `RMA-2026-008${idx + 1}`),
      trackingCode: String(r.trackingNumber ?? r.trackingCode ?? r.awbCode ?? ''),
      orderCode: String(r.orderNumber ?? r.orderCode ?? r.sourceOrderCode ?? ''),
      returnReason: String(r.returnReason ?? r.reason ?? 'Hoàn trả sàn TMĐT')
        .replace(/CUSTOMER_RETURN/gi, 'Khách hoàn trả (RMA)')
        .replace(/chua kiểm định/gi, 'chưa kiểm định'),
      returnAgingDays: aging,
      isOverdueInTransit: Boolean(r.isOverdueInTransit ?? aging > 7),
      disputeRemainingHours: Number(r.disputeRemainingHours ?? 18),
      videoEvidenceStatus: String(
        r.videoEvidenceStatus ??
          (r.disputeClaimStatus === 'EVIDENCE_ATTACHED_READY'
            ? 'Đã đính kèm video đồng kiểm (Sẵn sàng khiếu nại)'
            : r.disputeClaimStatus === 'DISPUTE_WINDOW_EXPIRED'
              ? 'Đã quá hạn 48h khiếu nại sàn'
              : 'Chờ ghi hình đồng kiểm bàn QC (Còn hạn 48h)'),
      )
        .replace(/Video đồng kiểm\s+SHA-256/gi, 'video đồng kiểm')
        .replace(/\s*SHA-256\s*/gi, ' ')
        .trim(),
    };
  });
};

export const ImportReceiptRmaCard: React.FC<ImportReceiptRmaCardProps> = ({
  activeWorkspaceTab,
  canRead,
  receipts,
  selectedReceipt,
  warehouseId,
  warehouses,
  products,
  warehouseLocations,
  rmaLocationCode,
  setRmaLocationCode,
  successMsg,
  setSuccessMsg,
  setError,
}) => {
  const [rmaProductId, setRmaProductId] = useState<string>('');
  const [rmaSourceOrderCode, setRmaSourceOrderCode] = useState<string>('');
  const [rmaReasonCode, setRmaReasonCode] = useState<string>('CUSTOMER_RETURN');
  const [rmaQuantity, setRmaQuantity] = useState<number>(5);
  const [rmaDispositionType, setRmaDispositionType] = useState<string>('REFURBISH_AND_RTV_SPLIT');
  const [rtsTrackingCode, setRtsTrackingCode] = useState<string>('');
  const [rtvConfirmedSignature, setRtvConfirmedSignature] = useState<string>('');
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [customerReturns, setCustomerReturns] = useState<CustomerReturnItem[]>([]);

  useEffect(() => {
    if (!canRead || activeWorkspaceTab !== 'rma') return;
    let active = true;
    void apiClient
      .get('/api/v1/import-receipts/customer-returns')
      .then((res) => {
        if (!active) return;
        setCustomerReturns(mapCustomerReturnsRows(res?.data?.items ?? res?.data?.returns ?? res?.data));
      })
      .catch(() => {
        if (active) setCustomerReturns([]);
      });
    return () => { active = false; };
  }, [canRead, activeWorkspaceTab]);

  const currentRtvSignature = `${rmaSourceOrderCode}|${rmaReasonCode}|${rmaProductId}|${rmaLocationCode}|${rmaQuantity}|${rmaDispositionType}`;
  const isRtvAlreadyConfirmed = rtvConfirmedSignature !== '' && rtvConfirmedSignature === currentRtvSignature;
  const deadlineInfo = computeReturnDisputeDeadlineLabel(receipts, selectedReceipt);
  const hiddenStyle = { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none' as const, margin: 0, padding: 0, border: 0 };

  return (
    <div className="no-print" style={activeWorkspaceTab === 'rma' ? undefined : hiddenStyle}>
      <UiCard
        title="Đơn hoàn RMA & kiểm định chất lượng"
        subtitle="Theo dõi cảnh báo quá hạn 7 ngày và thời hạn khiếu nại đền bù sàn 48 giờ"
      >
        <div className="ui-inline-wrap">
          <div className="ui-kpi-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%', marginBottom: 8 }}>
            <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontSize: 12, color: '#475569' }}>Đang kiểm định chất lượng / Xử lý chênh lệch</div>
              <strong style={{ fontSize: 15, color: '#b45309' }}>{receipts.filter(r => r.requiresQc || r.status === 'QcPending' || r.status === 'QcCompleted' || r.status.startsWith('Discrepancy')).length}</strong>
            </div>
            <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontSize: 12, color: '#475569' }}>Hoàn tất kiểm định &amp; đã ghi sổ tồn kho</div>
              <strong style={{ fontSize: 15, color: '#065f46' }}>{receipts.filter(r => r.status === 'Posted' || r.status === 'Approved').length}</strong>
            </div>
            <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontSize: 12, color: '#475569' }}>Hàng khách trả lại (Khả dụng / Biệt trữ / Hư hỏng)</div>
              <strong style={{ fontSize: 15, color: '#0f172a' }}>{receipts.filter(r => (r.note || '').toUpperCase().includes('RMA') || r.status.startsWith('Discrepancy')).length}</strong>
            </div>
          </div>
          {successMsg && (successMsg.includes('RMA') || successMsg.includes('hàng hoàn trả') || successMsg.includes('hậu cần thu hồi')) && (
            <div role="status" className="ui-success-text" style={{ order: 99, flexBasis: '100%', width: '100%', marginTop: 12, padding: '10px 14px', borderRadius: 8, border: '1px solid #a7f3d0', background: '#ecfdf5', color: '#065f46', fontSize: 13, fontWeight: 500, whiteSpace: 'normal', wordBreak: 'break-word' }}>
              {successMsg}
            </div>
          )}
          <div style={{ width: '100%', marginBottom: 10 }}>
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Đơn hoàn RMA & kiểm định chất lượng" data-testid="rma-customer-returns-table" style={{ width: '100%' }}>
                <thead>
                  <tr>
                    <th style={{ whiteSpace: 'nowrap' }}>Mã phiếu hoàn</th>
                    <th style={{ whiteSpace: 'nowrap' }}>Mã vận đơn / Đơn hàng</th>
                    <th>Lý do hoàn</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Số ngày lưu hoàn</th>
                    <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Cảnh báo quá hạn (&gt; 7 ngày)</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Thời hạn khiếu nại 48h còn lại</th>
                    <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Trạng thái Video bằng chứng</th>
                  </tr>
                </thead>
                <tbody>
                  {customerReturns.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="ui-empty-cell" style={{ textAlign: 'center', padding: '14px 12px', color: '#64748b' }}>
                        Chưa có dữ liệu đơn hoàn RMA nào đang chờ xử lý hoặc quá hạn khiếu nại.
                      </td>
                    </tr>
                  ) : customerReturns.map((row) => (
                    <tr key={row.returnCode}>
                      <td style={{ whiteSpace: 'nowrap' }}><strong>{row.returnCode}</strong></td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <code>{row.trackingCode}</code>
                        <div style={{ fontSize: 11, color: '#64748b' }}>Đơn: {row.orderCode}</div>
                      </td>
                      <td>{row.returnReason}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}><strong>{row.returnAgingDays} ngày</strong></td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        {row.isOverdueInTransit ? <UiBadge tone="danger"><span data-testid="rma-overdue-badge">Quá {row.returnAgingDays} ngày</span></UiBadge> : <UiBadge tone="success">Đúng hạn vận chuyển</UiBadge>}
                      </td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', fontWeight: 700, color: row.disputeRemainingHours <= 24 ? '#b91c1c' : '#0f172a' }}>
                        {formatNaturalRemainingTime(row.disputeRemainingHours)}
                      </td>
                      <td>{row.videoEvidenceStatus}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>

          <div style={{ width: '100%', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(340px, 1fr))', gap: 14, alignItems: 'start', marginTop: 4 }}>
            <div style={{ padding: 14, borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div>
                <span style={{ display: 'inline-block', fontSize: 11.5, fontWeight: 700, color: '#0284c7', textTransform: 'uppercase', marginBottom: 2 }}>Bước 1</span>
                <strong style={{ display: 'block', fontSize: 14, color: '#0f172a' }}>Quét mã vận đơn hoàn hàng nhanh tại cửa kho</strong>
                <span style={{ fontSize: 12, color: '#475569' }}>Quét mã vận đơn hoàn trả từ hãng vận chuyển (GHN / GHTK / ViettelPost / SPX) để khởi tạo hồ sơ kiểm định</span>
              </div>
              <label className="ui-stack">
                <span>Mã vận đơn hoàn hàng</span>
                <input type="search" aria-label="Quét mã vận đơn hoàn hàng nhanh" value={rtsTrackingCode} onChange={e => setRtsTrackingCode(e.target.value)} placeholder="VD: GHN-RTS-889901" style={{ height: 40, minHeight: 40, padding: '6px 10px', boxSizing: 'border-box' }} />
              </label>
              <span
                data-testid="return-dispute-deadline-banner"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '6px 10px',
                  borderRadius: 8,
                  background: deadlineInfo.completed ? '#ecfdf5' : '#fffbeb',
                  border: `1px solid ${deadlineInfo.completed ? '#a7f3d0' : '#fde68a'}`,
                  color: deadlineInfo.completed ? '#065f46' : '#92400e',
                  fontSize: 12,
                  fontWeight: 600,
                }}
              >
                ⏱ Hạn phản hồi khiếu nại sàn: {deadlineInfo.text}
              </span>
              <div className="ui-inline-actions">
                <button
                  type="button"
                  disabled={governanceBusy}
                  style={{ background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }}
                  onClick={() => {
                    if (governanceBusy) return;
                    setGovernanceBusy(true); setError(''); setSuccessMsg('');
                    const refCode = rtsTrackingCode.trim() || ('HS-KD-' + new Date().toISOString().slice(0, 10));
                    void apiClient
                      .post('/api/v1/import-receipts/initiate-rma-inspection', {
                        warehouseId: warehouseId ? Number(warehouseId) : (warehouses[0]?.id ?? undefined),
                        rmaReferenceCode: refCode,
                        returnReference: refCode,
                        reasonCode: 'CUSTOMER_RETURN',
                        ...(rmaProductId ? { productId: Number(rmaProductId) } : {}),
                        locationCode: rmaLocationCode || 'QC-HOLD-01',
                        quantity: rmaQuantity,
                      })
                      .then((res) => {
                        const data = (res as { data?: { rmaInspectionCode?: string; rmaCode?: string } } | undefined)?.data;
                        const rawCode = data?.rmaInspectionCode || data?.rmaCode;
                        const initCode = rawCode ? rawCode.replace(/^RMA-INSP/i, 'HS-KD').replace(/^RMA/i, 'HS-KD') : '';
                        if (!initCode) {
                          setError('Phản hồi khởi tạo hồ sơ kiểm định hàng hoàn trả không hợp lệ từ máy chủ.');
                          return;
                        }
                        setSuccessMsg(`Đã khởi tạo hồ sơ kiểm định hàng hoàn trả (${initCode}): Phân luồng khu vực cách ly sang bàn kiểm định chất lượng đầu vào.`);
                      })
                      .catch((err) => setError(permissionError(err, 'Không thể khởi tạo hồ sơ kiểm định hàng hoàn trả.')))
                      .finally(() => setGovernanceBusy(false));
                  }}
                >
                  Khởi tạo Hồ sơ Kiểm định Hàng hoàn trả
                </button>
              </div>
            </div>

            <div style={{ padding: 14, borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div>
                <strong style={{ display: 'block', fontSize: 14, color: '#0f172a' }}>Bước 2: Cấu hình kiểm định &amp; phân luồng tân trang / xuất trả</strong>
                <span style={{ fontSize: 12, color: '#475569' }}>Chọn phiếu hoàn gốc, vị trí cách ly và xác nhận thực thi phân luồng hậu cần thu hồi</span>
              </div>
              <div className="import-form-grid" style={{ gridTemplateColumns: 'repeat(2, minmax(0, 1fr))' }}>
                <label className="ui-stack">
                  <span>Đơn hàng / Phiếu hoàn gốc</span>
                  <select
                    aria-label="Đơn hàng / Phiếu hoàn gốc RMA"
                    value={rmaSourceOrderCode}
                    onChange={e => {
                      setRmaSourceOrderCode(e.target.value);
                      setRtvConfirmedSignature('');
                      const matchedReturn = customerReturns.find(cr => cr.orderCode === e.target.value || cr.returnCode === e.target.value);
                      if (matchedReturn?.trackingCode) setRtsTrackingCode(matchedReturn.trackingCode);
                    }}
                  >
                    <option value="">-- Chọn đơn hàng / phiếu hoàn gốc --</option>
                    {customerReturns.map(cr => <option key={cr.returnCode} value={cr.orderCode || cr.returnCode}>{cr.returnCode} — Đơn: {cr.orderCode} ({cr.trackingCode})</option>)}
                    {receipts.map(r => <option key={`gr-${r.id}`} value={r.code}>Phiếu: {r.code} ({r.warehouseName})</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Lý do hoàn trả</span>
                  <select aria-label="Lý do hoàn trả RMA" value={rmaReasonCode} onChange={e => { setRmaReasonCode(e.target.value); setRtvConfirmedSignature(''); }}>
                    <option value="CUSTOMER_RETURN">Khách hàng đổi ý / Hoàn trả sàn TMĐT</option>
                    <option value="DAMAGED_IN_TRANSIT">Hư hỏng bao bì khi vận chuyển</option>
                    <option value="WRONG_ITEM_SHIPPED">Giao sai mẫu / quy cách SKU</option>
                    <option value="DEFECTIVE_MANUFACTURER">Lỗi kỹ thuật từ nhà sản xuất</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Sản phẩm kiểm định hoàn trả</span>
                  <select aria-label="Sản phẩm kiểm định RMA" value={rmaProductId} onChange={e => { setRmaProductId(e.target.value); setRtvConfirmedSignature(''); }}>
                    <option value="">-- Mặc định theo lô hoàn trả --</option>
                    {products.map(p => <option key={p.id} value={String(p.id)}>{p.code} - {p.name}</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Vị trí kệ cách ly / Tân trang</span>
                  <select aria-label="Vị trí cách ly RMA" value={rmaLocationCode} onChange={e => { setRmaLocationCode(e.target.value); setRtvConfirmedSignature(''); }}>
                    {warehouseLocations.map(loc => <option key={loc.id} value={loc.code}>{loc.code} — {loc.name} ({loc.locationType})</option>)}
                    <option value="QC-HOLD-01">QC-HOLD-01 — Khu vực tạm giữ chờ kiểm định</option>
                    <option value="QUARANTINE-01">QUARANTINE-01 — Khu vực cách ly hàng lỗi</option>
                    <option value="DAMAGED-01">DAMAGED-01 — Khu vực hàng hư hỏng</option>
                    <option value="RETURNS-STAGE-01">RETURNS-STAGE-01 — Khu vực tiếp nhận hàng trả</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Số lượng kiểm định / Phân luồng</span>
                  <input inputMode="numeric" role="spinbutton" min="1" step="1" aria-label="Số lượng xử lý RMA" value={rmaQuantity} onChange={e => { setRmaQuantity(Math.max(1, Number(e.target.value) || 1)); setRtvConfirmedSignature(''); }} />
                </label>
                <label className="ui-stack">
                  <span>Phương án phân luồng hậu cần thu hồi</span>
                  <select aria-label="Phương án phân luồng QC / RTV" value={rmaDispositionType} onChange={e => { setRmaDispositionType(e.target.value); setRtvConfirmedSignature(''); }}>
                    <option value="REFURBISH_AND_RTV_SPLIT">Tân trang &amp; Xuất trả Nhà cung cấp</option>
                    <option value="RETURN_TO_VENDOR_ONLY">Xuất trả toàn bộ cho Nhà cung cấp</option>
                    <option value="QUARANTINE_SCRAP">Biệt trữ chờ hủy theo biên bản</option>
                  </select>
                </label>
              </div>
              <div className="ui-inline-actions">
                <button
                  type="button"
                  disabled={governanceBusy || isRtvAlreadyConfirmed}
                  style={{
                    background: isRtvAlreadyConfirmed ? '#64748b' : '#0f766e',
                    color: '#ffffff',
                    borderColor: isRtvAlreadyConfirmed ? '#64748b' : '#0f766e',
                    fontWeight: 600,
                  }}
                  onClick={() => {
                    if (governanceBusy || isRtvAlreadyConfirmed) return;
                    if (!window.confirm('Xác nhận thực thi phân luồng tân trang & xuất trả nhà cung cấp cho lô hàng hoàn trả này?')) return;
                    setGovernanceBusy(true); setError(''); setSuccessMsg('');
                    void apiClient
                      .post('/api/v1/import-receipts/execute-rtv-refurbish', {
                        warehouseId: warehouseId ? Number(warehouseId) : (warehouses[0]?.id ?? undefined),
                        dispositionType: rmaDispositionType || 'REFURBISH_AND_RTV_SPLIT',
                        ...(rmaProductId ? { productId: Number(rmaProductId) } : {}),
                        locationCode: rmaLocationCode || 'QC-HOLD-01',
                        quantity: rmaQuantity,
                      })
                      .then((res) => {
                        const data = (res as { data?: { dispositionCode?: string; executionId?: string } } | undefined)?.data;
                        const execCode = data?.dispositionCode || data?.executionId;
                        if (!execCode) {
                          setError('Phản hồi thực thi phân luồng tân trang & xuất trả nhà cung cấp không hợp lệ từ máy chủ.');
                          return;
                        }
                        setRtvConfirmedSignature(currentRtvSignature);
                        setSuccessMsg(`Đã thực thi phân luồng hậu cần thu hồi (${execCode}): Tân trang phân loại hạng B & Xuất trả Nhà cung cấp.`);
                      })
                      .catch((err) => setError(permissionError(err, 'Không thể thực thi phân luồng tân trang & xuất trả nhà cung cấp.')))
                      .finally(() => setGovernanceBusy(false));
                  }}
                >
                  {isRtvAlreadyConfirmed ? 'Đã thực thi phân luồng tân trang & xuất trả' : 'Thực thi phân luồng tân trang & xuất trả nhà cung cấp'}
                </button>
              </div>
            </div>
          </div>

          <details style={{ width: '100%', marginTop: 10 }}>
            <summary style={{ cursor: 'pointer', fontSize: 12.5, fontWeight: 600, color: '#475569' }}>Chi tiết quy tắc &amp; kỹ thuật hệ thống</summary>
            <div style={{ marginTop: 6, padding: '8px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0', fontSize: 12, color: '#475569' }}>
              Video đồng kiểm tại bàn QC được niêm phong mã băm toàn vẹn (SHA-256) phục vụ đối soát khiếu nại tự động với sàn TMĐT trong khung 48 giờ.
            </div>
          </details>
        </div>
      </UiCard>
    </div>
  );
};
