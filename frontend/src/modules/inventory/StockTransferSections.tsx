import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import {
  formatDateTimeVn,
  normalizeStatus,
  statusLabel,
  statusTone,
  type InTransitItem,
  type NetworkRebalancingResult,
  type SealVerificationResult,
  type Transfer,
} from './stockTransfersShared';
export { StockTransferDraftModal } from '../inventory/StockTransferCreateAndConvertModals';

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
