import type { FormEvent, RefObject } from 'react';
import { ArrowRight, Plus, X } from 'lucide-react';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import {
  formatDateTimeVn,
  normalizeStatus,
  statusLabel,
  statusTone,
  type InTransitItem,
  type Line,
  type NetworkRebalancingResult,
  type Product,
  type SealVerificationResult,
  type Transfer,
  type Warehouse,
} from './stockTransfersShared';

const statusBannerStyle = {
  marginTop: 8,
  padding: '8px 12px',
  borderRadius: 8,
  background: '#ecfdf5',
  color: '#065f46',
  border: '1px solid #a7f3d0',
  fontSize: 13,
  lineHeight: 1.5,
  whiteSpace: 'normal' as const,
  wordBreak: 'break-word' as const,
};

export function StockTransferOverviewCard(props: {
  items: Transfer[];
  actionInFlight: boolean;
  createInFlight: boolean;
  governanceBusy: boolean;
  sealVerification: SealVerificationResult | null;
  networkRebalancing: NetworkRebalancingResult | null;
  onVerifySeal: () => void;
  onSimulateRebalancing: () => void;
}) {
  const { items, actionInFlight, createInFlight, governanceBusy, sealVerification, networkRebalancing, onVerifySeal, onSimulateRebalancing } = props;
  const draftCount = items.filter((item) => normalizeStatus(item.status) === 'Draft').length;
  const approvedCount = items.filter((item) => normalizeStatus(item.status) === 'Approved').length;
  const inTransitCount = items.filter((item) => normalizeStatus(item.status) === 'InTransit').length;
  const completedCount = items.filter((item) => ['Received', 'Completed'].includes(normalizeStatus(item.status))).length;
  const closedExceptionCount = items.filter((item) => ['Cancelled', 'Returned'].includes(normalizeStatus(item.status))).length;

  return (
    <UiCard title="Tổng quan Chu trình Điều chuyển Nội bộ & SLA Vận chuyển">
      <UiMetricGrid>
        <UiMetric value={items.length} label={`Tổng phiếu trên trang: ${items.length}`} />
        <UiMetric value={draftCount} label={`Phiếu nháp: ${draftCount}`} />
        <UiMetric value={approvedCount} label={`Chờ xuất kho (Đã duyệt): ${approvedCount}`} />
        <UiMetric value={inTransitCount} label={`Đang vận chuyển: ${inTransitCount}`} />
        <UiMetric value={completedCount} label={`Đã nhận / Hoàn tất: ${completedCount}`} />
        <UiMetric value={closedExceptionCount} label={`Đã hủy / Hoàn trả: ${closedExceptionCount}`} />
      </UiMetricGrid>
      <div
        role="note"
        style={{ marginTop: 10, padding: '8px 12px', borderRadius: 8, background: '#f0f9ff', border: '1px solid #bae6fd', color: '#0c4a6e', fontSize: 13, lineHeight: 1.5 }}
      >
        Cam kết SLA điều chuyển nội bộ: Phiếu đã duyệt cần xuất kho nguồn đúng hạn; lô hàng đang vận chuyển liên kho cần đối chiếu niêm phong HU và xác nhận nhập kho đích đầy đủ.
      </div>
      <div className="ui-inline-wrap" style={{ marginTop: 10 }}>
        <button type="button" disabled={actionInFlight || createInFlight || governanceBusy} onClick={onVerifySeal}>
          Xác minh Niêm phong HU &amp; Đối chiếu Hàng đang Đi đường
        </button>
        <button type="button" disabled={actionInFlight || createInFlight || governanceBusy} onClick={onSimulateRebalancing}>
          Mô phỏng Tái cân bằng Tồn kho Mạng lưới &amp; Bổ sung Liên kho
        </button>
      </div>
      {networkRebalancing && (
        <div role="status" style={statusBannerStyle}>
          {`${networkRebalancing.simulationId} • ${networkRebalancing.status} (${networkRebalancing.networkSurplusShortageStatus} / ${networkRebalancing.replenishmentExceptionTriage})`}
        </div>
      )}
      {sealVerification && (
        <div role="status" style={statusBannerStyle}>
          Mã niêm phong: <strong>{sealVerification.sealVerificationId}</strong> ({sealVerification.transferCode}) — Trạng thái:{' '}
          <strong>{sealVerification.sealStatus}</strong>
        </div>
      )}
    </UiCard>
  );
}

export function StockTransferInTransitCard(props: {
  inTransitLoading: boolean;
  inTransitItems: InTransitItem[];
  onOpenDetail: (id: number) => void;
}) {
  const { inTransitLoading, inTransitItems, onOpenDetail } = props;
  return (
    <UiCard title="Tồn kho đang vận chuyển giữa các kho">
      {inTransitLoading ? (
        <p role="status">Đang tải tồn đang vận chuyển...</p>
      ) : inTransitItems.length === 0 ? (
        <UiEmptyState title="Không có hàng hóa nào đang vận chuyển." />
      ) : (
        <UiTableScroll>
          <table aria-label="Tồn kho đang vận chuyển">
            <thead>
              <tr>
                <th>Mã phiếu</th>
                <th>Kho nguồn → Kho đích</th>
                <th>Sản phẩm</th>
                <th>ĐVT</th>
                <th className="ui-number" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL đã xuất</th>
                <th className="ui-number" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL đang vận chuyển</th>
                <th>Thời điểm xuất</th>
                <th className="ui-number" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thời gian vận chuyển</th>
              </tr>
            </thead>
            <tbody>
              {inTransitItems.map((row) => (
                <tr key={`${row.transferId}-${row.productId}`}>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button type="button" onClick={() => onOpenDetail(row.transferId)}>
                      <strong style={{ whiteSpace: 'nowrap' }}>{row.transferCode}</strong>
                    </button>
                  </td>
                  <td>{row.sourceWarehouseName} → {row.destinationWarehouseName}</td>
                  <td><strong style={{ whiteSpace: 'nowrap' }}>{row.productCode}</strong> — {row.productName}</td>
                  <td>{row.unitName === 'QA_UNIT' || row.unitName === 'QA-PCS' ? 'THÙNG' : row.unitName}</td>
                  <td className="ui-number" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{row.dispatchedQuantity}</td>
                  <td className="ui-number" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    <UiBadge tone="warning">{row.inTransitQuantity}</UiBadge>
                  </td>
                  <td>{row.dispatchedAt ? formatDateTimeVn(row.dispatchedAt) : '—'}</td>
                  <td className="ui-number" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{row.hoursInTransit}h</td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
}

export function StockTransferListCard(props: {
  loading: boolean;
  items: Transfer[];
  page: number;
  totalPages: number;
  onSetPage: (p: number) => void;
  onOpenDetail: (id: number) => void;
}) {
  const { loading, items, page, totalPages, onSetPage, onOpenDetail } = props;
  return (
    <UiCard title="Danh sách phiếu điều chuyển">
      {loading && items.length === 0 ? (
        <p role="status">Đang tải...</p>
      ) : items.length === 0 ? (
        <UiEmptyState title="Chưa có phiếu điều chuyển phù hợp." />
      ) : (
        <UiTableScroll>
          <table aria-label="Danh sách phiếu điều chuyển">
            <thead>
              <tr>
                <th>Mã phiếu</th>
                <th>Luồng kho</th>
                <th>Trạng thái</th>
                <th>Ngày tạo</th>
                <th style={{ paddingRight: 10, whiteSpace: 'nowrap' }}>Hành động</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const normalized = normalizeStatus(item.status);
                return (
                  <tr key={item.id}>
                    <td style={{ whiteSpace: 'nowrap' }}><strong style={{ whiteSpace: 'nowrap' }}>{item.code}</strong></td>
                    <td>
                      <span className="transfer-flow" style={{ display: 'block', lineHeight: 1.25 }}>
                        <span style={{ display: 'block', color: '#475569' }}>{item.sourceWarehouseName} →</span>
                        <span style={{ display: 'block', fontWeight: 600, color: '#0f172a' }}>{item.destinationWarehouseName}</span>
                      </span>
                    </td>
                    <td><UiBadge tone={statusTone(normalized)}>{statusLabel[normalized]}</UiBadge></td>
                    <td>{formatDateTimeVn(item.createdAt)}</td>
                    <td style={{ paddingRight: 6, whiteSpace: 'nowrap' }}>
                      <button
                        type="button"
                        style={{ whiteSpace: 'nowrap', padding: '4px 6px', minHeight: 28, fontSize: 11 }}
                        aria-label={`Xem chi tiết ${item.code}`}
                        onClick={() => onOpenDetail(item.id)}
                      >
                        Xem chi tiết
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
      )}
      <div className="ui-pagination" aria-label="Phân trang điều chuyển">
        <button type="button" disabled={page <= 1} onClick={() => onSetPage(page - 1)}>Trước</button>
        <span>Trang {page} / {totalPages} • Tổng {items.length} phiếu điều chuyển</span>
        <button type="button" disabled={page >= totalPages} onClick={() => onSetPage(page + 1)}>Sau</button>
      </div>
    </UiCard>
  );
}

export function StockTransferDraftModal(props: {
  draftDialogRef: RefObject<HTMLFormElement | null>;
  editingId: number | null;
  editingRevision: number | null;
  draftConflict: boolean;
  createInFlight: boolean;
  error: string;
  sourceId: number | '';
  setSourceId: (v: number | '') => void;
  destinationId: number | '';
  setDestinationId: (v: number | '') => void;
  note: string;
  setNote: (v: string) => void;
  lines: Line[];
  setLines: (v: Line[]) => void;
  warehouses: Warehouse[];
  products: Product[];
  onCloseDraftEditor: () => void;
  onReloadConflictedDraft: () => void;
  onSubmitCreate: (event: FormEvent) => void;
}) {
  const {
    draftDialogRef, editingId, editingRevision, draftConflict, createInFlight, error,
    sourceId, setSourceId, destinationId, setDestinationId, note, setNote,
    lines, setLines, warehouses, products, onCloseDraftEditor, onReloadConflictedDraft, onSubmitCreate,
  } = props;
  const labelStyle = { display: 'flex', flexDirection: 'column' as const, gap: 6, alignItems: 'stretch', justifyContent: 'flex-start', height: 'auto', margin: 0 };

  return (
    <div className="transfer-modal" role="presentation">
      <form
        ref={draftDialogRef}
        className="transfer-dialog"
        style={{ maxWidth: 720, width: '100%' }}
        role="dialog"
        aria-modal="true"
        aria-labelledby="transfer-create-title"
        onSubmit={onSubmitCreate}
        onKeyDown={(event) => {
          if (event.key === 'Escape' && !createInFlight) {
            event.stopPropagation();
            onCloseDraftEditor();
          }
        }}
      >
        <div className="dialog-title">
          <h2 id="transfer-create-title">{editingId === null ? 'Tạo phiếu điều chuyển' : 'Chỉnh sửa phiếu nháp'}</h2>
          <button type="button" aria-label="Đóng tạo phiếu" disabled={createInFlight} onClick={onCloseDraftEditor}>
            <X aria-hidden="true" />
          </button>
        </div>

        {error && <p role="alert" className="transfer-error">{error}</p>}
        {editingId !== null && editingRevision !== null && (
          <p className="ui-muted-text">Phiên bản nháp: {editingRevision}. Hệ thống từ chối lưu nếu người khác đã chỉnh sửa phiếu.</p>
        )}
        {editingId !== null && draftConflict && (
          <p role="status" className="ui-muted-text">
            Phiếu đã thay đổi ở phiên khác. Tải lại dữ liệu mới nhất trước khi chỉnh sửa tiếp.
            <button type="button" disabled={createInFlight} onClick={onReloadConflictedDraft}>
              Tải lại phiên bản mới
            </button>
          </p>
        )}
        <div className="warehouse-pair" style={{ display: 'grid', gridTemplateColumns: '1fr auto 1fr', alignItems: 'start', gap: 12, margin: '16px 0' }}>
          <label style={labelStyle}>
            <span>Kho nguồn</span>
            <select
              aria-label="Kho nguồn"
              required
              disabled={createInFlight}
              value={sourceId}
              style={{ marginTop: 0 }}
              onChange={(event) => {
                const nextSource = Number(event.target.value) || '';
                if (nextSource !== sourceId) setDestinationId('');
                setSourceId(nextSource);
              }}
            >
              <option value="">Chọn kho</option>
              {warehouses.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </label>
          <ArrowRight aria-hidden="true" style={{ marginTop: 28 }} />
          <label style={labelStyle}>
            <span>Kho đích</span>
            <select
              aria-label="Kho đích"
              required
              disabled={createInFlight}
              value={destinationId}
              style={{ marginTop: 0 }}
              onChange={(event) => setDestinationId(Number(event.target.value) || '')}
            >
              <option value="">Chọn kho</option>
              {warehouses.filter((w) => w.id !== sourceId).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </label>
        </div>

        <label style={{ ...labelStyle, margin: '12px 0' }}>
          <span>Ghi chú</span>
          <textarea aria-label="Ghi chú điều chuyển" maxLength={500} disabled={createInFlight} style={{ marginTop: 0 }} value={note} onChange={(event) => setNote(event.target.value)} />
        </label>

        <div className="line-header">
          <h3>Sản phẩm</h3>
          <button type="button" disabled={createInFlight} onClick={() => setLines([...lines, { productId: '', quantity: '', note: '' }])}>
            <Plus size={16} aria-hidden="true" /> Thêm dòng
          </button>
        </div>

        {lines.map((line, index) => (
          <div className="transfer-line" key={index}>
            <select
              aria-label={`Sản phẩm dòng ${index + 1}`}
              required
              disabled={createInFlight}
              value={line.productId}
              onChange={(event) => setLines(lines.map((entry, row) => (row === index ? { ...entry, productId: Number(event.target.value) || '' } : entry)))}
            >
              <option value="">Chọn sản phẩm</option>
              {products.map((product) => <option key={product.id} value={product.id}>{product.code} - {product.name}</option>)}
            </select>
            <input
              aria-label={`Số lượng dòng ${index + 1}`}
              required
              inputMode="decimal"
              role="spinbutton"
              min="0.0001"
              step="0.0001"
              disabled={createInFlight}
              placeholder="Số lượng"
              value={line.quantity}
              onChange={(event) => setLines(lines.map((entry, row) => (row === index ? { ...entry, quantity: event.target.value === '' ? '' : Number(event.target.value) } : entry)))}
            />
            <input
              aria-label={`Ghi chú dòng ${index + 1}`}
              placeholder="Ghi chú dòng (tùy chọn)"
              maxLength={500}
              disabled={createInFlight}
              value={line.note}
              onChange={(event) => setLines(lines.map((entry, row) => (row === index ? { ...entry, note: event.target.value } : entry)))}
            />
            <button type="button" aria-label={`Xóa dòng ${index + 1}`} disabled={createInFlight} onClick={() => setLines(lines.filter((_, rowIndex) => rowIndex !== index))}>
              <X size={17} aria-hidden="true" />
            </button>
          </div>
        ))}

        <div className="dialog-actions">
          <button type="button" disabled={createInFlight} onClick={onCloseDraftEditor}>Đóng</button>
          <button className="ui-primary-button" type="submit" disabled={createInFlight || (editingId !== null && draftConflict)}>
            {createInFlight ? (editingId === null ? 'Đang tạo phiếu...' : 'Đang lưu thay đổi...') : (editingId === null ? 'Tạo phiếu' : 'Lưu thay đổi')}
          </button>
        </div>
      </form>
    </div>
  );
}
