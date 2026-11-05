import React, { useEffect, useState } from 'react';
import { Lock } from 'lucide-react';
import { usePermission } from '../../services/authorization';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiSortTh, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { ImportReceiptRowActions } from './ImportReceiptRowActions';
import {
  type ImportReceipt,
  type Warehouse,
  checkCompactViewport,
  formatVnDateTime,
  mapCreatedByDisplayName,
  statusLabel,
  statusTone,
} from './ImportReceiptTypes';

export interface ImportReceiptKpiCardProps {
  receipts: ImportReceipt[];
}

export const ImportReceiptKpiCard: React.FC<ImportReceiptKpiCardProps> = ({ receipts }) => {
  const receivingCount = receipts.filter(
    r => r.status === 'Draft' || r.status === 'Received' || r.status.startsWith('Discrepancy'),
  ).length;
  const qcCount = receipts.filter(
    r => r.status === 'QcPending' || r.status === 'QcCompleted' || r.status === 'ReadyToPost',
  ).length;
  const postedCount = receipts.filter(r => r.status === 'Posted' || r.status === 'Approved').length;
  const cancelledCount = receipts.filter(r => r.status === 'Cancelled').length;

  return (
    <div className="import-kpi-strip" style={{ order: 1, minWidth: 0, maxWidth: '100%' }}>
      <UiMetricGrid>
        <UiMetric
          label="Tổng số phiếu nhập"
          value={receipts.length}
          subtitle="Toàn bộ chứng từ tiếp nhận"
          isZero={receipts.length === 0}
        />
        <UiMetric
          label="Đang soạn / Nhận tại cửa"
          value={receivingCount}
          subtitle="Nháp, đã nhận hoặc xử lý sai lệch"
          isZero={receivingCount === 0}
        />
        <UiMetric
          label="Chờ kiểm định / Chờ duyệt"
          value={qcCount}
          subtitle="Chờ QC hoặc sẵn sàng ghi sổ"
          isZero={qcCount === 0}
        />
        <UiMetric
          label="Đã ghi sổ tồn kho"
          value={postedCount}
          subtitle={
            cancelledCount > 0
              ? `Đã hạch toán sổ cái • Đã hủy: ${cancelledCount}`
              : 'Đã hạch toán sổ cái & giá vốn'
          }
          isZero={postedCount === 0}
        />
      </UiMetricGrid>
    </div>
  );
};

export interface ImportReceiptListCardProps {
  receipts: ImportReceipt[];
  warehouses: Warehouse[];
  selectedReceipt: ImportReceipt | null;
  listWarehouseFilter: string;
  setListWarehouseFilter: (val: string) => void;
  listStatusFilter: string;
  setListStatusFilter: (val: string) => void;
  listSearchFilter: string;
  setListSearchFilter: (val: string) => void;
  printLoadingId: number | null;
  workflowId: number | null;
  setWorkflowId: (val: number | null) => void;
  approvingId: number | null;
  userId: number | null;
  canObserve: boolean;
  canCancel: boolean;
  canApprove: boolean;
  canApproveQc: boolean;
  canPost: boolean;
  canReceive: boolean;
  receivingDestLocation: string;
  handleViewDetails: (id: number) => Promise<void>;
  openPrintPreview: (id: number, trigger: HTMLButtonElement) => Promise<void>;
  observeReceipt: (id: number, receipt?: ImportReceipt) => Promise<void>;
  handleCancel: (id: number) => Promise<void>;
  handleApprove: (id: number) => Promise<void>;
  runWorkflow: (id: number, command: 'receive' | 'qc-disposition' | 'post', receipt?: ImportReceipt) => Promise<void>;
  fetchData: () => Promise<void>;
  setError: (val: string) => void;
  setSuccessMsg: (val: string) => void;
}

type ReceiptSortKey = 'code' | 'createdAt' | 'status';

export const ImportReceiptListCard: React.FC<ImportReceiptListCardProps> = ({
  receipts, warehouses, selectedReceipt, listWarehouseFilter, setListWarehouseFilter, listStatusFilter,
  setListStatusFilter, listSearchFilter, setListSearchFilter, printLoadingId, workflowId, setWorkflowId,
  approvingId, userId, canObserve, canCancel, canApprove, canApproveQc, canPost, canReceive,
  receivingDestLocation, handleViewDetails, openPrintPreview, observeReceipt, handleCancel,
  handleApprove, runWorkflow, fetchData, setError, setSuccessMsg,
}) => {
  const [sortKey, setSortKey] = useState<ReceiptSortKey>('createdAt');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const canReadCost = usePermission('inventory.cost.read');
  const [isMobileViewport, setIsMobileViewport] = useState<boolean>(() => checkCompactViewport(768));
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState<boolean>(false);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const syncViewport = () => setIsMobileViewport(checkCompactViewport(768));
    const mq = typeof window.matchMedia === 'function' ? window.matchMedia('(max-width: 768px)') : null;
    window.addEventListener('resize', syncViewport);
    mq?.addEventListener?.('change', syncViewport);
    return () => { window.removeEventListener('resize', syncViewport); mq?.removeEventListener?.('change', syncViewport); };
  }, []);

  const toggleSort = (key: ReceiptSortKey) => {
    if (sortKey === key) setSortDir(d => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortKey(key); setSortDir('asc'); }
  };

  const filteredReceipts = receipts.filter(r => {
    const rowWhLabel = formatWarehouseLabel(r.warehouseName, undefined, r.warehouseId);
    if (listWarehouseFilter && rowWhLabel !== listWarehouseFilter && r.warehouseName !== listWarehouseFilter && String(r.warehouseId) !== listWarehouseFilter) return false;
    if (listStatusFilter && r.status !== listStatusFilter) return false;
    if (listSearchFilter.trim()) {
      const q = listSearchFilter.trim().toLowerCase();
      const hay = `${r.code} ${r.supplierCode ?? ''} ${r.supplierName ?? ''} ${rowWhLabel} ${r.warehouseName ?? ''}`.toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });

  const sortedReceipts = [...filteredReceipts].sort((a, b) => {
    const mult = sortDir === 'asc' ? 1 : -1;
    if (sortKey === 'code') return a.code.localeCompare(b.code, 'vi') * mult;
    if (sortKey === 'status') return statusLabel(a.status).localeCompare(statusLabel(b.status), 'vi') * mult;
    return String(a.createdAt || '').localeCompare(String(b.createdAt || '')) * mult;
  });

  const warehouseNames = Array.from(
    new Set([
      ...receipts.map(r => formatWarehouseLabel(r.warehouseName, undefined, r.warehouseId)).filter(Boolean),
      ...warehouses.map(w => formatWarehouseLabel(w.name, (w as { code?: string }).code, w.id)).filter(Boolean),
    ]),
  );

  const computeReceiptTotalVnd = (r: ImportReceipt): number | null => {
    if (r.totalAmount !== null && r.totalAmount !== undefined && Number.isFinite(Number(r.totalAmount))) {
      return Number(r.totalAmount);
    }
    const lines = r.details || [];
    const hasLinePricing = lines.some(d => d.lineTotalAmount != null || d.unitPrice != null);
    if (!hasLinePricing) return null;
    return lines.reduce((sum, d) => {
      if (d.lineTotalAmount != null && Number.isFinite(Number(d.lineTotalAmount))) return sum + Number(d.lineTotalAmount);
      const qty = Number(d.receivedQuantity || d.expectedQuantity || 0);
      const price = Number(d.unitPrice || 0);
      const taxFactor = 1 + Number(d.taxPercent || 0) / 100;
      return sum + qty * price * taxFactor;
    }, 0);
  };

  const sharedActionProps = {
    printLoadingId, workflowId, setWorkflowId, approvingId, userId,
    canObserve, canCancel, canApprove, canApproveQc, canPost, canReceive,
    receivingDestLocation, handleViewDetails, openPrintPreview, observeReceipt,
    handleCancel, handleApprove, runWorkflow, fetchData, setError, setSuccessMsg,
  };

  const activeFilterCount = (listWarehouseFilter ? 1 : 0) + (listStatusFilter ? 1 : 0);
  const maskedCostBadge = (
    <span className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4, color: '#475569', fontSize: 12, fontWeight: 600 }}>
      <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
    </span>
  );

  const renderStatusOptions = () => (
    <>
      <option value="">Tất cả trạng thái</option>
      <option value="Draft">Nháp</option>
      <option value="Received">Đã nhận hàng</option>
      <option value="QcPending">Chờ kiểm định QC</option>
      <option value="ReadyToPost">Sẵn sàng ghi sổ</option>
      <option value="Approved">Đã duyệt</option>
      <option value="Posted">Đã ghi nhận tồn kho</option>
      <option value="Cancelled">Đã hủy</option>
    </>
  );

  const renderMobileCards = () => (
    <div className="ui-mobile-card-list" data-testid="import-receipts-mobile-cards" style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 8, width: '100%', maxWidth: '100%' }}>
      {sortedReceipts.map(r => {
        const totalQty = (r.details || []).reduce((s, d) => s + Number(d.receivedQuantity || d.expectedQuantity || 0), 0);
        const totalVnd = computeReceiptTotalVnd(r);
        return (
          <article
            key={r.id}
            data-testid="import-receipt-mobile-card"
            className="ui-mobile-card"
            style={{ padding: 12, borderRadius: 10, border: '1px solid #cbd5e1', background: '#ffffff', display: 'flex', flexDirection: 'column', gap: 8, minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <span className="ui-code" style={{ whiteSpace: 'nowrap', fontSize: 14, fontWeight: 700, color: '#0f172a' }}>{r.code}</span>
              <UiBadge tone={statusTone(r.status)}>{statusLabel(r.status)}</UiBadge>
            </div>
            <div style={{ fontSize: 12.5, color: '#475569', display: 'flex', flexWrap: 'wrap', gap: 6, fontVariantNumeric: 'tabular-nums' }}>
              <span>{r.supplierName || 'Chưa gắn NCC'}</span>
              <span>•</span>
              <span>{formatWarehouseLabel(r.warehouseName, undefined, r.warehouseId)}</span>
              <span>•</span>
              <span>{formatVnDateTime(r.createdAt)}</span>
              {totalQty > 0 && (
                <>
                  <span>•</span>
                  <strong>{totalQty} sản phẩm</strong>
                </>
              )}
            </div>
            <div data-testid="import-receipt-mobile-total" style={{ fontSize: 12.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums', display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <span style={{ color: '#475569' }}>Tổng tiền:</span>
              {canReadCost && totalVnd !== null ? <strong>{`${Math.round(totalVnd).toLocaleString('vi-VN')} ₫`}</strong> : maskedCostBadge}
            </div>
            <ImportReceiptRowActions receipt={r} isMobile={true} {...sharedActionProps} />
          </article>
        );
      })}
    </div>
  );

  if (isMobileViewport) {
    return (
      <section aria-label="Danh sách phiếu nhập" style={{ order: 2, minWidth: 0, maxWidth: '100%', display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div className="import-compact-bar" style={{ display: 'flex', flexDirection: 'row', flexWrap: 'nowrap', alignItems: 'center', gap: 6, width: '100%' }}>
          <input
            type="search"
            aria-label="Tìm mã phiếu hoặc nhà cung cấp"
            placeholder="Tìm mã phiếu, NCC..."
            value={listSearchFilter}
            onChange={e => setListSearchFilter(e.target.value)}
            style={{ flex: '1 1 auto', minWidth: 0, height: 40, minHeight: 40, padding: '6px 10px', fontSize: 13, boxSizing: 'border-box' }}
          />
          <button
            type="button"
            data-testid="btn-toggle-mobile-filters"
            className="ui-btn ui-btn-secondary"
            aria-expanded={mobileFiltersOpen}
            onClick={() => setMobileFiltersOpen(v => !v)}
            style={{ flex: '0 0 auto', flexShrink: 0, height: 40, minHeight: 40, padding: '0 12px', fontSize: 12.5, fontWeight: 600, whiteSpace: 'nowrap' }}
          >
            Bộ lọc{activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}
          </button>
        </div>
        {mobileFiltersOpen && (
          <div data-testid="import-mobile-filters-panel" style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 8, padding: '8px 10px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <select aria-label="Lọc phiếu nhập theo kho" value={listWarehouseFilter} onChange={e => setListWarehouseFilter(e.target.value)} style={{ width: '100%' }}>
              <option value="">Tất cả kho</option>
              {warehouseNames.map(wh => <option key={wh} value={wh}>{wh}</option>)}
            </select>
            <select aria-label="Lọc phiếu nhập theo trạng thái" value={listStatusFilter} onChange={e => setListStatusFilter(e.target.value)} style={{ width: '100%' }}>
              {renderStatusOptions()}
            </select>
          </div>
        )}
        {sortedReceipts.length === 0 ? (
          <UiEmptyState title="Chưa có phiếu nhập" detail="Chưa có chứng từ nhập kho phù hợp với bộ lọc hiện tại." />
        ) : (
          renderMobileCards()
        )}
      </section>
    );
  }

  return (
    <div style={{ order: 2, minWidth: 0, maxWidth: '100%' }}>
      <UiCard
        title="Danh sách phiếu nhập"
        subtitle="Theo dõi trạng thái phiếu nhập, ghi nhận số lượng thực tế và duyệt ghi sổ tồn kho"
      >
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end', marginBottom: 12, padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0', maxWidth: '100%', boxSizing: 'border-box' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: '1 1 190px', minWidth: 185, maxWidth: '100%' }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Kho nhận</span>
            <select aria-label="Lọc phiếu nhập theo kho" value={listWarehouseFilter} onChange={e => setListWarehouseFilter(e.target.value)} style={{ width: '100%', minWidth: 185 }}>
              <option value="">Tất cả kho</option>
              {warehouseNames.map(wh => <option key={wh} value={wh}>{wh}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: '1 1 195px', minWidth: 190, maxWidth: '100%' }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Trạng thái phiếu</span>
            <select aria-label="Lọc phiếu nhập theo trạng thái" value={listStatusFilter} onChange={e => setListStatusFilter(e.target.value)} style={{ width: '100%', minWidth: 190 }}>
              {renderStatusOptions()}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: '1 1 220px', minWidth: 210, maxWidth: '100%' }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Tìm mã phiếu / Nhà cung cấp</span>
            <input type="search" aria-label="Tìm mã phiếu hoặc nhà cung cấp" placeholder="Tìm mã phiếu, NCC..." value={listSearchFilter} onChange={e => setListSearchFilter(e.target.value)} style={{ width: '100%', minWidth: 210 }} />
          </label>
        </div>

        {sortedReceipts.length === 0 ? (
          <UiEmptyState title="Chưa có phiếu nhập" detail="Chưa có chứng từ nhập kho phù hợp với bộ lọc hiện tại." />
        ) : (
          <div className="ui-desktop-table-wrap">
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Danh sách phiếu nhập" style={{ width: '100%', minWidth: 0 }}>
                <thead>
                  <tr>
                    <UiSortTh label="Mã phiếu & Ngày" active={sortKey === 'code'} direction={sortDir} onToggle={() => toggleSort('code')} className="ui-sticky-id" />
                    <UiSortTh label="Trạng thái" active={sortKey === 'status'} direction={sortDir} onToggle={() => toggleSort('status')} />
                    <th>Kho &amp; Nhà cung cấp</th>
                    <th style={{ textAlign: 'right', whiteSpace: 'nowrap', minWidth: 112 }}>SL / Tổng tiền</th>
                    <th className="ui-sticky-action" style={{ width: 128, maxWidth: 136 }}>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedReceipts.map(r => {
                    const totalQty = (r.details || []).reduce((s, d) => s + Number(d.receivedQuantity || d.expectedQuantity || 0), 0);
                    const totalVnd = computeReceiptTotalVnd(r);
                    const creatorName = mapCreatedByDisplayName(
                      r.createdByName || (selectedReceipt?.id === r.id ? selectedReceipt.createdByName : ''),
                      Boolean(r.createdBy),
                    );
                    const supplierDisplay = r.supplierCode
                      ? `${r.supplierCode} - ${r.supplierName}`
                      : r.supplierName || '—';
                    return (
                      <tr key={r.id}>
                        <td className="ui-sticky-id" style={{ whiteSpace: 'nowrap', background: '#ffffff' }}>
                          <div><span className="ui-code" style={{ whiteSpace: 'nowrap', fontWeight: 700 }}>{r.code}</span></div>
                          <div style={{ fontSize: 11.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
                            {formatVnDateTime(r.createdAt)}
                          </div>
                        </td>
                        <td style={{ whiteSpace: 'nowrap' }}>
                          <UiBadge tone={statusTone(r.status)}>
                            <span>{statusLabel(r.status)}</span>
                          </UiBadge>
                        </td>
                        <td style={{ whiteSpace: 'normal', minWidth: 165, lineHeight: 1.35 }}>
                          <div style={{ fontWeight: 600, color: '#0f172a' }}>
                            {formatWarehouseLabel(r.warehouseName, undefined, r.warehouseId)}
                          </div>
                          <div style={{ fontSize: 12, color: '#475569' }}>
                            {supplierDisplay} • {creatorName}
                          </div>
                        </td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', paddingRight: 12 }}>
                          <div style={{ fontWeight: 600, color: '#0f172a' }}>{totalQty > 0 ? `${totalQty} sản phẩm` : '—'}</div>
                          <div style={{ fontSize: 11.5, color: '#475569' }}>
                            {canReadCost && totalVnd !== null
                              ? `${Math.round(totalVnd).toLocaleString('vi-VN')} ₫`
                              : canReadCost
                                ? '—'
                                : maskedCostBadge}
                          </div>
                        </td>
                        <td className="ui-sticky-action" style={{ background: '#ffffff', width: 128, maxWidth: 136, overflow: 'visible' }}>
                          <ImportReceiptRowActions receipt={r} isMobile={false} {...sharedActionProps} />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        )}

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, justifyContent: 'space-between', alignItems: 'center', marginTop: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0', fontSize: 12.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
          <span>Hiển thị {sortedReceipts.length === 0 ? 0 : 1}–{sortedReceipts.length} / Tổng {sortedReceipts.length} phiếu nhập</span>
          <span>Trang 1 / 1 (Cỡ trang: 20)</span>
        </div>
      </UiCard>
    </div>
  );
};
