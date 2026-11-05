import React from 'react';
import { Lock } from 'lucide-react';
import { type CustomerReturnDto } from './ReturnDispositionDrawer';
import { UiBadge, UiTableScroll } from '../../ui/ProductionUi';
import {
  rmaConditionLabelVi,
  rmaDispositionLabelVi,
  rmaStatusLabelVi,
  warehouseLocationLabelVi,
} from '../../ui/vietnameseEnumLabels';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';

const formatVnd = (val?: number | null): string =>
  typeof val === 'number' && Number.isFinite(val) ? `${val.toLocaleString('vi-VN')} ₫` : '—';

const formatShortDateVi = (iso?: string): string => {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const yyyy = d.getFullYear();
  return `${dd}/${mm}/${yyyy}`;
};

export interface CustomerReturnsTableAndCardsProps {
  items: CustomerReturnDto[];
  showUnitCost: boolean;
  onOpenDisposition: (row: CustomerReturnDto) => Promise<void>;
}

export const CustomerReturnsTableAndCards: React.FC<CustomerReturnsTableAndCardsProps> = ({
  items,
  showUnitCost,
  onOpenDisposition,
}) => (
  <>
    <div className="rma-desktop-table-wrap">
      <UiTableScroll>
        <table
          data-testid="customer-returns-table"
          className="ui-table-compact"
          aria-label="Bảng quản lý hàng hoàn và đổi trả RMA"
          style={{ width: '100%', minWidth: 0 }}
        >
          <thead>
            <tr>
              <th style={{ padding: '6px 6px', minWidth: 96, whiteSpace: 'nowrap' }}>Mã RMA</th>
              <th style={{ padding: '6px 6px', minWidth: 96, whiteSpace: 'nowrap' }}>Trạng thái</th>
              <th style={{ padding: '6px 6px', minWidth: 108, whiteSpace: 'nowrap' }}>Đơn &amp; Vận đơn</th>
              <th style={{ padding: '6px 6px', minWidth: 148, maxWidth: 210, whiteSpace: 'nowrap' }}>Sản phẩm &amp; Tình trạng</th>
              <th style={{ padding: '6px 6px', minWidth: 112, maxWidth: 150, whiteSpace: 'nowrap' }}>Kho &amp; Vị trí</th>
              <th style={{ padding: '6px 6px', minWidth: 74, textAlign: 'right', whiteSpace: 'nowrap' }}>SL hoàn</th>
              <th style={{ padding: '6px 6px', minWidth: 92, paddingRight: 14, textAlign: 'right', whiteSpace: 'nowrap' }}>Giá vốn</th>
              <th
                className="ui-sticky-action"
                style={{
                  padding: '6px 6px',
                  width: 92,
                  minWidth: 86,
                  maxWidth: 98,
                  position: 'sticky',
                  right: 0,
                  zIndex: 4,
                  background: '#f8fafc',
                  boxShadow: '-4px 0 8px -4px rgba(15, 23, 42, 0.12)',
                  whiteSpace: 'nowrap',
                }}
              >
                Thao tác
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((row) => {
              const statusUpper = String(row.status ?? '').toUpperCase();
              const statusTone = statusUpper === 'COMPLETED' ? 'success' : statusUpper === 'INSPECTED' ? 'info' : 'warning';
              const condTone = row.condition === 'GOOD' ? 'success' : row.condition === 'DEFECTIVE' ? 'danger' : 'warning';
              const createdDate = formatShortDateVi(row.createdAt);
              return (
                <tr key={row.id} data-testid={`rma-row-${row.returnCode}`}>
                  <td style={{ padding: '6px 6px', minWidth: 96, whiteSpace: 'nowrap', lineHeight: 1.3 }}>
                    <div><span className="ui-code" style={{ whiteSpace: 'nowrap', fontWeight: 700, color: '#0f172a' }}>{row.returnCode}</span></div>
                    {createdDate ? <div style={{ fontSize: 11.5, color: '#475569', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{createdDate}</div> : null}
                  </td>
                  <td style={{ padding: '6px 6px', minWidth: 96, whiteSpace: 'nowrap' }}>
                    <UiBadge tone={statusTone}>{rmaStatusLabelVi(row.status)}</UiBadge>
                  </td>
                  <td style={{ padding: '6px 6px', minWidth: 108, whiteSpace: 'normal', lineHeight: 1.3 }}>
                    <div><span className="ui-code" style={{ whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>{row.trackingNumber || '—'}</span></div>
                    <div style={{ fontSize: 11.5, color: '#475569' }}><span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.exportReceiptCode || row.salesOrderCode || '—'}</span></div>
                  </td>
                  <td style={{ padding: '6px 6px', minWidth: 148, maxWidth: 210, whiteSpace: 'normal', lineHeight: 1.35 }}>
                    <div><span className="ui-code" style={{ whiteSpace: 'nowrap', fontWeight: 700, color: '#0f172a' }}>{row.productCode}</span></div>
                    <div style={{ fontSize: 12, color: '#334155' }}>{row.productName}</div>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 5, alignItems: 'center', marginTop: 2 }}>
                      <UiBadge tone={condTone}>{rmaConditionLabelVi(row.condition)}</UiBadge>
                      <span style={{ fontSize: 11.5, color: '#475569', fontWeight: 500 }}>
                        • {rmaDispositionLabelVi(row.disposition)}
                      </span>
                    </div>
                  </td>
                  <td style={{ padding: '6px 6px', minWidth: 112, maxWidth: 150, whiteSpace: 'normal', lineHeight: 1.3 }}>
                    <div style={{ fontWeight: 600, color: '#0f172a' }}>
                      {formatWarehouseLabel(row.warehouseName, row.warehouseCode, row.warehouseId)}
                    </div>
                    <div style={{ fontSize: 11.5, color: '#475569' }}>
                      {warehouseLocationLabelVi(row.quarantineLocationCode || 'QUARANTINE-01')}
                    </div>
                  </td>
                  <td style={{ padding: '6px 6px', minWidth: 74, textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700, whiteSpace: 'nowrap' }}>
                    {row.quantity}
                  </td>
                  <td style={{ padding: '6px 6px', minWidth: 92, paddingRight: 14, textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', lineHeight: 1.25 }}>
                    {showUnitCost ? (
                      <span data-testid="rma-cost-visible" style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{formatVnd(row.unitCost)}</span>
                    ) : (
                      <span data-testid="rma-cost-redacted" className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4, color: '#475569', fontSize: 12, fontWeight: 600 }}>
                        <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
                      </span>
                    )}
                  </td>
                  <td
                    className="ui-sticky-action"
                    style={{
                      padding: '6px 6px',
                      width: 92,
                      minWidth: 86,
                      maxWidth: 98,
                      position: 'sticky',
                      right: 0,
                      zIndex: 3,
                      background: '#ffffff',
                      boxShadow: '-4px 0 8px -4px rgba(15, 23, 42, 0.12)',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    <button
                      type="button"
                      data-testid="btn-open-disposition-drawer"
                      className="ui-btn ui-btn-secondary"
                      style={{ padding: '4px 8px', minHeight: 32, fontSize: 12, whiteSpace: 'nowrap' }}
                      disabled={statusUpper === 'COMPLETED'}
                      title={statusUpper === 'COMPLETED' ? 'Phiếu hàng hoàn này đã hoàn tất định đoạt' : undefined}
                      onClick={() => void onOpenDisposition(row)}
                    >
                      Phân loại
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </UiTableScroll>
    </div>

    <div className="ui-mobile-card-list rma-mobile-card-list" aria-label="Danh sách hàng hoàn RMA rút gọn">
      {items.map((row) => {
        const statusUpper = String(row.status ?? '').toUpperCase();
        const statusTone = statusUpper === 'COMPLETED' ? 'success' : statusUpper === 'INSPECTED' ? 'info' : 'warning';
        const condTone = row.condition === 'GOOD' ? 'success' : row.condition === 'DEFECTIVE' ? 'danger' : 'warning';
        const createdDate = formatShortDateVi(row.createdAt);
        return (
          <article key={row.id} className="ui-mobile-card" data-testid={`rma-mobile-card-${row.returnCode}`}>
            <div className="ui-mobile-card-head">
              <div>
                <div className="ui-mobile-card-title">
                  <span className="ui-code">{row.returnCode}</span>
                </div>
                <div className="ui-mobile-card-subtitle">
                  {createdDate ? `${createdDate} • ` : ''}{row.trackingNumber || row.exportReceiptCode || row.salesOrderCode || '—'}
                </div>
              </div>
              <UiBadge tone={statusTone}>{rmaStatusLabelVi(row.status)}</UiBadge>
            </div>
            <div className="ui-mobile-card-grid">
              <div className="ui-mobile-card-field">
                <span className="ui-mobile-card-label">Sản phẩm</span>
                <span className="ui-mobile-card-value">{row.productCode} — {row.productName}</span>
              </div>
              <div className="ui-mobile-card-field">
                <span className="ui-mobile-card-label">Tình trạng &amp; Định đoạt</span>
                <span className="ui-mobile-card-value">
                  <UiBadge tone={condTone}>{rmaConditionLabelVi(row.condition)}</UiBadge>{' '}
                  <span>{rmaDispositionLabelVi(row.disposition)}</span>
                </span>
              </div>
              <div className="ui-mobile-card-field">
                <span className="ui-mobile-card-label">Kho &amp; Vị trí</span>
                <span className="ui-mobile-card-value">
                  {formatWarehouseLabel(row.warehouseName, row.warehouseCode, row.warehouseId)} • {warehouseLocationLabelVi(row.quarantineLocationCode || 'QUARANTINE-01')}
                </span>
              </div>
              <div className="ui-mobile-card-field">
                <span className="ui-mobile-card-label">SL hoàn / Giá vốn</span>
                <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>
                  <strong>{row.quantity}</strong> • {showUnitCost ? formatVnd(row.unitCost) : 'Ẩn theo quyền'}
                </span>
              </div>
            </div>
            <div className="ui-mobile-card-actions">
              <button
                type="button"
                data-testid={`btn-mobile-disposition-${row.returnCode}`}
                className="ui-btn ui-btn-secondary"
                disabled={statusUpper === 'COMPLETED'}
                onClick={() => void onOpenDisposition(row)}
              >
                Phân loại định đoạt
              </button>
            </div>
          </article>
        );
      })}
    </div>
  </>
);
