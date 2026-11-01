import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { InventoryTraceabilityExposureCards } from './InventoryTraceabilityExposureCards';
import {
  inventoryStatusLabel,
  locationCodeLabel,
  referenceTypeLabel,
  tone,
  transactionTypeLabel,
  type RecallTreeSimulationResult,
  type Result,
} from './inventoryTraceabilityShared';

export function TraceabilityRecallOverviewCard(props: {
  result: Result | null;
  recallSimulating: boolean;
  recallSimulation: RecallTreeSimulationResult | null;
  onSimulateRecallTree: () => void;
}) {
  const { result, recallSimulating, recallSimulation, onSimulateRecallTree } = props;
  return (
    <UiCard title="Tổng quan Chuỗi Truy xuất Nguồn gốc Lô / Sê-ri / Kiện hàng">
      <div className="ui-form-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, maxWidth: '100%' }}>
        <div className="ui-subcard">
          <strong>Nhóm tồn hiện tại &amp; Tổng lượng thực tế</strong>
          <span>
            {result?.currentBuckets.length ?? 0} nhóm tồn • Thực tế:{' '}
            {(result?.currentBuckets ?? []).reduce((sum, item) => sum + item.onHandQuantity, 0)} • Giữ chỗ:{' '}
            {(result?.currentBuckets ?? []).reduce((sum, item) => sum + item.reservedQuantity, 0)}
          </span>
        </div>
        <div className="ui-subcard">
          <strong>Định danh Lô / Sê-ri trong kết quả</strong>
          <span>
            {new Set((result?.currentBuckets ?? []).map((item) => item.lotNumber).filter(Boolean)).size} mã lô •{' '}
            {new Set((result?.currentBuckets ?? []).map((item) => item.serialNumber).filter(Boolean)).size} sê-ri
          </span>
        </div>
        <div className="ui-subcard">
          <strong>Sự kiện sổ cái &amp; Chuỗi đảo bất biến</strong>
          <span>
            {result?.events.length ?? 0} sự kiện •{' '}
            {(result?.events ?? []).filter((item) => item.transactionType === 'Reversal' || item.isReversed).length} giao dịch đảo/đã đảo
          </span>
        </div>
        <div className="ui-subcard">
          <strong>Chứng từ &amp; Chuỗi kiện hàng liên kết</strong>
          <span>
            {(result?.relatedDocuments ?? []).length} chứng từ • {(result?.shipmentHuEvidence ?? []).length} chuỗi kiện hàng
          </span>
        </div>
      </div>
      <div className="ui-inline-actions" style={{ marginTop: 8 }}>
        <button type="button" disabled={recallSimulating} onClick={onSimulateRecallTree}>
          Truy vết Nhanh Cây Phả hệ Lô/Sê-ri &amp; Khoanh vùng Thu hồi
        </button>
        {recallSimulation && (
          <span>
            Mã mô phỏng: <strong>{recallSimulation.simulationId}</strong> ({recallSimulation.query}) • Sẵn sàng khoanh vùng thu hồi:{' '}
            <strong>{recallSimulation.recallContainmentReady ? 'ĐÃ SẴN SÀNG' : 'CHƯA SẴN SÀNG'}</strong>
          </span>
        )}
      </div>
    </UiCard>
  );
}

export function InventoryTraceabilityResults(props: {
  result: Result;
  loading: boolean;
  bucketOffset: number;
  eventOffset: number;
  eventLimit: number;
  showExposureCards: boolean;
  onRequestTrace: (offset: number, nextEventOffset?: number, resetAnchor?: boolean) => void;
}) {
  const { result, loading, bucketOffset, eventOffset, eventLimit, showExposureCards, onRequestTrace } = props;
  return (
    <div className="ui-stack">
      <UiCard title="Nhóm tồn kho hiện tại">
        {result.bucketsTruncated && (
          <p role="status" className="ui-muted-text">
            Chỉ hiển thị 500 nhóm tồn của trang hiện tại. Vẫn còn dữ liệu khác; chọn “Trang sau” hoặc lọc thêm theo kho, sản phẩm, lô, sê-ri.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Nhóm tồn kho hiện tại phục vụ truy vết">
            <thead>
              <tr>
                <th>Sản phẩm</th>
                <th>Kho / Vị trí</th>
                <th>Trạng thái</th>
                <th>Lô / Sê-ri</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã giữ</th>
              </tr>
            </thead>
            <tbody>
              {result.currentBuckets.length === 0 ? (
                <tr><td colSpan={6} className="ui-empty-cell">Không còn nhóm tồn kho hiện tại khớp điều kiện.</td></tr>
              ) : (
                result.currentBuckets.map((x) => (
                  <tr key={x.inventoryStockId}>
                    <td><strong style={{ whiteSpace: 'nowrap' }}>{x.productCode}</strong><br /><small>{x.productName}</small></td>
                    <td>{x.warehouseName}<br /><small style={{ whiteSpace: 'nowrap' }}>{locationCodeLabel(x.locationCode)}</small></td>
                    <td style={{ whiteSpace: 'nowrap' }}><UiBadge>{inventoryStatusLabel(x.inventoryStatus)}</UiBadge></td>
                    <td>
                      {x.lotNumber ?? '—'} / {x.serialNumber ?? '—'}
                      {x.expiryDate && <><br /><small>HSD {new Date(x.expiryDate).toLocaleDateString('vi-VN')}</small></>}
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{x.onHandQuantity}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{x.reservedQuantity}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
        <nav aria-label="Phân trang nhóm tồn kho" className="ui-toolbar">
          <button type="button" disabled={loading || bucketOffset === 0} onClick={() => onRequestTrace(bucketOffset - 500)}>
            Trang trước
          </button>
          <span aria-live="polite">Trang {Math.floor(bucketOffset / 500) + 1}</span>
          <button
            type="button"
            disabled={loading || !result.bucketsTruncated || bucketOffset >= 50_000}
            onClick={() => onRequestTrace(bucketOffset + 500)}
          >
            Trang sau
          </button>
          {bucketOffset >= 50_000 && result.bucketsTruncated && (
            <span className="ui-muted-text">Đã tới giới hạn xem trang; hãy lọc chi tiết hơn.</span>
          )}
        </nav>
      </UiCard>

      {showExposureCards && <InventoryTraceabilityExposureCards result={result} />}

      <UiCard title="Dòng thời gian sổ cái bất biến">
        <p className="ui-muted-text">
          Sự kiện đảo giao dịch là dấu mốc hiệu chỉnh, không xóa giao dịch gốc. Lịch sử được cố định theo mốc lúc truy vết; chọn “Truy vết” để cập nhật sự kiện mới, tồn kho hiện tại vẫn được tải mới.
        </p>
        {result.eventsTruncated && (
          <p role="status" className="ui-muted-text">
            Chỉ lấy các sự kiện mới nhất trong giới hạn truy vấn và các sự kiện liên quan để đủ chuỗi đảo. Lịch sử còn dữ liệu cũ hơn; dùng “Sự kiện sau” để xem trang kế tiếp hoặc thu hẹp điều kiện tìm kiếm.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Dòng thời gian sổ cái phục vụ truy vết">
            <thead>
              <tr>
                <th>Thời gian</th>
                <th>Sự kiện</th>
                <th>Sản phẩm</th>
                <th>Vị trí / Trạng thái</th>
                <th>Lô / Sê-ri</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Số lượng</th>
                <th>Tham chiếu</th>
                <th>Chuỗi đảo giao dịch</th>
                <th>Người thực hiện / Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {result.events.length === 0 ? (
                <tr><td colSpan={9} className="ui-empty-cell">Không có sự kiện sổ cái khớp điều kiện.</td></tr>
              ) : (
                result.events.map((x) => (
                  <tr key={x.transactionId}>
                    <td>{new Date(x.transactionDate).toLocaleString('vi-VN')}</td>
                    <td>
                      <UiBadge tone={tone(x)}>{transactionTypeLabel(x.transactionType)}</UiBadge><br />
                      <small>#{x.transactionId}{x.isReversed ? ' • đã đảo' : ''}{x.reversalOfTransactionId ? ' • đảo #' + x.reversalOfTransactionId : ''}</small>
                    </td>
                    <td>{x.productCode}<br /><small>{x.warehouseName}</small></td>
                    <td>
                      {x.fromLocationCode && x.toLocationCode ? locationCodeLabel(x.fromLocationCode) + ' → ' + locationCodeLabel(x.toLocationCode) : locationCodeLabel(x.locationCode)}<br />
                      <small>{x.fromInventoryStatus && x.toInventoryStatus ? inventoryStatusLabel(x.fromInventoryStatus) + ' → ' + inventoryStatusLabel(x.toInventoryStatus) : inventoryStatusLabel(x.inventoryStatus)}</small>
                    </td>
                    <td>{x.lotNumber ?? '—'} / {x.serialNumber ?? '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{x.quantity}</td>
                    <td>{x.referenceType ? referenceTypeLabel(x.referenceType) + ' #' + (x.referenceId ?? '—') : '—'}</td>
                    <td>
                      {x.reversalOfTransactionId ? <>Gốc #{x.reversalOfTransactionId}<br /></> : null}
                      {x.correctiveTransactionId ? <>Hiệu chỉnh #{x.correctiveTransactionId}<br /></> : null}
                      {x.reversalTransactionId ? <>Dấu đảo #{x.reversalTransactionId}</> : null}
                      {!x.reversalOfTransactionId && !x.correctiveTransactionId && !x.reversalTransactionId ? '—' : null}
                    </td>
                    <td>
                      {x.createdByName || 'Người dùng #' + x.createdBy}<br />
                      {x.reasonCode && <small>Mã lý do: {x.reasonCode}<br /></small>}
                      <small>
                        {(x.note ?? '')
                          .replace(/\(100%\s*Match\)/gi, '(Khớp 100%)')
                          .replace(/\s*\(De-kitting\)/gi, '')
                          .replace(/\bRECEIVING\b/g, 'RCV-01')}
                      </small>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
        <nav aria-label="Phân trang sự kiện sổ cái" className="ui-toolbar">
          <button type="button" disabled={loading || eventOffset === 0} onClick={() => onRequestTrace(bucketOffset, eventOffset - eventLimit)}>
            Sự kiện trước
          </button>
          <span aria-live="polite">Trang sự kiện {Math.floor(eventOffset / eventLimit) + 1}</span>
          <button
            type="button"
            disabled={loading || !result.eventsTruncated || eventOffset >= 50_000}
            onClick={() => onRequestTrace(bucketOffset, eventOffset + eventLimit)}
          >
            Sự kiện sau
          </button>
          {eventOffset >= 50_000 && result.eventsTruncated && (
            <span className="ui-muted-text">Đã đến giới hạn xem lịch sử; hãy lọc thêm để thu hẹp kết quả.</span>
          )}
        </nav>
      </UiCard>
    </div>
  );
}
