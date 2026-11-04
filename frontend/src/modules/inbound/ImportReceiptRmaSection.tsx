import React, { useState, useEffect } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { ImportReceiptRmaWorkflowGrid, mapCustomerReturnsRows } from './ImportReceiptRmaPanels';
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
                      <td colSpan={7} className="ui-empty-cell" style={{ textAlign: 'center', padding: '14px 12px', color: '#475569' }}>
                        Chưa có dữ liệu đơn hoàn RMA nào đang chờ xử lý hoặc quá hạn khiếu nại.
                      </td>
                    </tr>
                  ) : customerReturns.map((row) => (
                    <tr key={row.returnCode}>
                      <td style={{ whiteSpace: 'nowrap' }}><strong>{row.returnCode}</strong></td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <code>{row.trackingCode}</code>
                        <div style={{ fontSize: 11, color: '#475569' }}>Đơn: {row.orderCode}</div>
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

          <ImportReceiptRmaWorkflowGrid
            receipts={receipts}
            customerReturns={customerReturns}
            warehouseId={warehouseId}
            warehouses={warehouses}
            products={products}
            warehouseLocations={warehouseLocations}
            rmaLocationCode={rmaLocationCode}
            setRmaLocationCode={setRmaLocationCode}
            rmaProductId={rmaProductId}
            setRmaProductId={setRmaProductId}
            rmaSourceOrderCode={rmaSourceOrderCode}
            setRmaSourceOrderCode={setRmaSourceOrderCode}
            rmaReasonCode={rmaReasonCode}
            setRmaReasonCode={setRmaReasonCode}
            rmaQuantity={rmaQuantity}
            setRmaQuantity={setRmaQuantity}
            rmaDispositionType={rmaDispositionType}
            setRmaDispositionType={setRmaDispositionType}
            rtsTrackingCode={rtsTrackingCode}
            setRtsTrackingCode={setRtsTrackingCode}
            currentRtvSignature={currentRtvSignature}
            isRtvAlreadyConfirmed={isRtvAlreadyConfirmed}
            setRtvConfirmedSignature={setRtvConfirmedSignature}
            deadlineInfo={deadlineInfo}
            governanceBusy={governanceBusy}
            setGovernanceBusy={setGovernanceBusy}
            setSuccessMsg={setSuccessMsg}
            setError={setError}
          />

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
