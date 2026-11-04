import React from 'react';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiTableScroll,
} from '../../ui/ProductionUi';
import type { Investigation } from './inventoryReconciliationValidators';
import { numberFormat, statusLabels } from './inventoryReconciliationValidators';

interface ReconciliationInvestigationCardProps {
  investigating: boolean;
  investigationError: string;
  investigation: Investigation | null;
  bucketPageIndex: number;
  eventPageIndex: number;
  onRefreshAnchor: (warehouseId: number, productId: number) => void;
  onChangeBucketStatus: (status: string) => void;
  onChangeBucketPage: (direction: 'next' | 'previous') => void;
  onChangeEventStatus: (status: string) => void;
  onChangeEventPage: (direction: 'older' | 'newer') => void;
  onClose: () => void;
}

const rightNumStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
};

const formatTransactionType = (type: string): string => {
  if (type === 'Import') return 'Nhập kho';
  if (type === 'Export') return 'Xuất kho';
  if (type === 'Move' || type === 'InternalMove') return 'Dịch chuyển vị trí';
  if (type === 'HandlingUnitLpnMove') return 'Di chuyển kiện LPN';
  if (type === 'Adjustment') return 'Điều chỉnh';
  if (type === 'AdjustmentIncrease') return 'Điều chỉnh tăng';
  if (type === 'AdjustmentDecrease') return 'Điều chỉnh giảm';
  if (type === 'StatusChange') return 'Đổi trạng thái';
  if (type === 'TransferIn') return 'Chuyển kho vào';
  if (type === 'TransferOut') return 'Chuyển kho ra';
  if (type === 'Reversal') return 'Đảo giao dịch';
  if (type === 'Ship') return 'Xuất giao hàng';
  return type.replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]');
};

const formatReferenceType = (refType?: string | null): string => {
  if (refType === 'ImportReceipt') return 'Phiếu nhập kho';
  if (refType === 'ExportReceipt') return 'Phiếu xuất kho';
  if (refType === 'HandlingUnitPalletSeed') return 'Khởi tạo kiện LPN';
  if (refType === 'HandlingUnitLpnMove' || refType === '[HandlingUnitLpnMove]') return '[Di chuyển kiện LPN]';
  if (refType === 'VAS_DEKIT') return 'Lệnh rã bộ KIT';
  if (refType === 'QASeedReconcile') return 'Cân bằng đối soát QA';
  if (refType === 'InternalMove') return 'Lệnh dịch chuyển nội bộ';
  return (refType ?? '—').replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]');
};

export const ReconciliationInvestigationCard: React.FC<ReconciliationInvestigationCardProps> = ({
  investigating,
  investigationError,
  investigation,
  bucketPageIndex,
  eventPageIndex,
  onRefreshAnchor,
  onChangeBucketStatus,
  onChangeBucketPage,
  onChangeEventStatus,
  onChangeEventPage,
  onClose,
}) => {
  if (!investigating && !investigationError && !investigation) return null;

  const statusVerdict = investigation?.snapshotComparisonStatus ?? investigation?.allStatusStatus;

  return (
    <UiCard title="Hồ sơ điều tra chênh lệch (chỉ đọc)">
      {investigating && <p role="status">Đang tải bằng chứng theo kho và sản phẩm...</p>}
      {investigationError && <p role="alert">{investigationError}</p>}
      {investigation && (
        <>
          <p>
            <strong>{investigation.productCode}</strong> — {investigation.productName} • {investigation.warehouseName}
          </p>
          <p className="ui-muted-text">
            Bằng chứng Ledger được cố định tại ID #{investigation.eventAnchorId}. Bucket tồn và số dư hiện tại là dữ liệu thời điểm truy vấn, không phải ảnh chụp quá khứ. Không sử dụng số chênh lệch này để tự sửa tồn kho hay tạo giao dịch đảo.
          </p>
          {investigation.ledgerHasEventsAfterAnchor && (
            <p role="status" className="ui-muted-text">
              Đã có giao dịch mới hơn mốc #{investigation.eventAnchorId}. Các trang Ledger và tổng kỳ vọng vẫn chỉ tính đến mốc cũ.
            </p>
          )}
          <button
            type="button"
            disabled={investigating}
            onClick={() => onRefreshAnchor(investigation.warehouseId, investigation.productId)}
          >
            Làm mới mốc Ledger
          </button>
          {investigation.evidenceAnchorsStale && (
            <p role="alert">
              Mốc bằng chứng đã cũ: có Ledger, bucket mới hoặc thay đổi tổng hợp bucket trong phạm vi đang xem. Kết luận so sánh với tồn hiện tại là Chưa xác định; số liệu lịch sử không cùng thời điểm với Balance. Hãy làm mới mốc để điều tra tiếp, không dùng chênh lệch này để sửa tồn.
            </p>
          )}
          {(investigation.negativePhysicalBucketCount ?? 0) > 0 && (
            <p role="alert">
              Phát hiện {investigation.negativePhysicalBucketCount} bucket có tồn vật lý âm. Kết luận chưa xác định; kiểm tra dữ liệu gốc và chính sách tồn âm, không tự sửa Ledger hoặc Balance.
            </p>
          )}
          {(investigation.invalidReservationBucketCount ?? 0) > 0 && (
            <p role="alert">
              Phát hiện {investigation.invalidReservationBucketCount} bucket có lượng giữ chỗ không hợp lệ (âm hoặc vượt tồn). Kết luận đối chiếu chưa xác định; cần kiểm tra dữ liệu gốc, không tự sửa số dư.
            </p>
          )}
          {investigation.availableLedgerExpectedIsPartial && (
            <p role="alert">
              Dữ liệu Ledger/Balance có lịch sử chưa phân loại, thiếu chứng từ gốc hoặc lượng giữ chỗ bất thường. Tổng Ledger AVAILABLE và chênh lệch bên dưới chưa đủ tin cậy, không dùng để sửa tồn.
            </p>
          )}
          <UiMetricGrid>
            <UiMetric label="Khả dụng · Số dư hiện tại" value={numberFormat.format(investigation.currentQuantity)} />
            <UiMetric label="Khả dụng · Ledger đến mốc" value={numberFormat.format(investigation.expectedQuantity)} />
            <UiMetric label="Khả dụng · Chênh lệch tham khảo" value={numberFormat.format(investigation.difference)} />
          </UiMetricGrid>
          {(investigation.eventsTruncated || investigation.bucketsTruncated) && (
            <p role="status" className="ui-muted-text">
              Bằng chứng đã giới hạn ({investigation.events.length}/{investigation.eventCount} sự kiện, {investigation.buckets.length}/{investigation.bucketCount} bucket). Không coi danh sách này là toàn bộ hồ sơ.
            </p>
          )}

          {Array.isArray(investigation.statusBreakdown) && (
            <>
              <h3>Đối chiếu theo 8 trạng thái tồn kho</h3>
              <p className="ui-muted-text">
                Dòng Ledger nhập/xuất được tính có dấu theo trạng thái; đổi trạng thái trừ ở nguồn, cộng ở đích và không thay đổi tổng tồn toàn kho. Dữ liệu hiện tại có thể đã biến động sau mốc #{investigation.eventAnchorId}; số lệch chỉ phục vụ điều tra, không phải lệnh sửa tồn.
              </p>
              {investigation.historyInsufficientForNonAvailableStock && (
                <p role="alert">
                  Có tồn kho ngoài trạng thái Khả dụng nhưng không tìm thấy lịch sử Ledger đến mốc đối chiếu. Không thể xác nhận tồn dự kiến hoặc mức lệch cần xử lý; cần đối chiếu chứng từ và lịch sử phát sinh trước khi điều chỉnh tồn kho.
                </p>
              )}
              {(investigation.unclassifiedStockBucketCount ?? 0) > 0 && (
                <p role="alert">
                  Có {investigation.unclassifiedStockBucketCount} bucket tồn với trạng thái ngoài tám trạng thái được hỗ trợ. Tổng tồn vẫn bao gồm số lượng các bucket này, nhưng không thể xác định số dư dự kiến hoặc kết luận khớp. Cần xác minh dữ liệu gốc; không tự điều chỉnh tồn kho.
                </p>
              )}
              {(investigation.unclassifiedLedgerEventCount ?? 0) > 0 && (
                <p role="alert">
                  Có {investigation.unclassifiedLedgerEventCount} giao dịch Ledger thiếu hoặc không hỗ trợ phân loại trạng thái. Không thể kết luận số dư kỳ vọng hay chênh lệch theo từng trạng thái; cần xác minh dữ liệu gốc trước khi sửa tồn.
                </p>
              )}
              {statusVerdict && (
                <p role="status">
                  Kết luận 8 trạng thái:{' '}
                  <UiBadge tone={statusVerdict === 'Indeterminate' ? 'warning' : statusVerdict === 'Match' ? 'success' : 'danger'}>
                    {statusVerdict === 'Indeterminate' ? 'Chưa xác định' : statusVerdict === 'Match' ? 'Khớp tất cả' : 'Lệch theo trạng thái'}
                  </UiBadge>
                </p>
              )}
              {statusVerdict === 'Mismatch' && investigation.allStatusDifference === 0 && (
                <p role="alert">
                  Lệch dù tổng bằng 0: các trạng thái tồn kho có chênh lệch bù trừ. Kiểm tra từng dòng trạng thái bên dưới; không tự điều chỉnh tồn kho.
                </p>
              )}
              <UiMetricGrid>
                <UiMetric label="Tổng tồn mọi trạng thái" value={numberFormat.format(investigation.allStatusCurrentQuantity ?? 0)} />
                <UiMetric label="Tổng đang giữ mọi trạng thái" value={numberFormat.format(investigation.allStatusReservedQuantity ?? 0)} />
                <UiMetric
                  label="Tổng Ledger các trạng thái (tham khảo)"
                  value={
                    investigation.evidenceAnchorsStale || investigation.allStatusExpectedQuantity == null
                      ? 'Chưa xác định'
                      : numberFormat.format(investigation.allStatusExpectedQuantity)
                  }
                />
              </UiMetricGrid>
              <UiTableScroll>
                <table aria-label="Đối chiếu từng trạng thái tồn kho theo Ledger">
                  <thead>
                    <tr>
                      <th>Trạng thái</th>
                      <th style={rightNumStyle}>Bucket</th>
                      <th style={rightNumStyle}>Tồn hiện tại</th>
                      <th style={rightNumStyle}>Đang giữ</th>
                      <th style={rightNumStyle}>Ledger nhập/xuất ròng</th>
                      <th style={rightNumStyle}>Chuyển trạng thái vào</th>
                      <th style={rightNumStyle}>Chuyển trạng thái ra</th>
                      <th style={rightNumStyle}>Ledger dự kiến</th>
                      <th style={rightNumStyle}>Lệch tham khảo</th>
                    </tr>
                  </thead>
                  <tbody>
                    {investigation.statusBreakdown.map((status) => (
                      <tr key={status.status}>
                        <td><strong>{statusLabels[status.status] ?? status.status}</strong></td>
                        <td style={rightNumStyle}>{status.bucketCount}</td>
                        <td style={rightNumStyle}>{numberFormat.format(status.currentQuantity)}</td>
                        <td style={rightNumStyle}>{numberFormat.format(status.reservedQuantity)}</td>
                        <td style={rightNumStyle}>{numberFormat.format(status.directLedgerNetQuantity)}</td>
                        <td style={rightNumStyle}>+{numberFormat.format(status.statusChangeInQuantity)}</td>
                        <td style={rightNumStyle}>-{numberFormat.format(status.statusChangeOutQuantity)}</td>
                        <td style={rightNumStyle}>{status.expectedQuantity == null ? 'Chưa xác định' : numberFormat.format(status.expectedQuantity)}</td>
                        <td style={rightNumStyle}>{status.difference == null ? 'Chưa xác định' : numberFormat.format(status.difference)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>
            </>
          )}

          <h3>Bucket tồn {statusLabels[investigation.bucketStatus ?? 'Available'] ?? 'không xác định'} hiện tại ({investigation.bucketCount})</h3>
          <label htmlFor="investigation-bucket-status">Trạng thái bucket cần xem</label>
          <select
            id="investigation-bucket-status"
            value={investigation.bucketStatus ?? 'Available'}
            disabled={investigating}
            onChange={(event) => onChangeBucketStatus(event.target.value)}
          >
            {Object.entries(statusLabels).map(([status, label]) => (
              <option key={status} value={status}>{label}</option>
            ))}
          </select>
          <p className="ui-muted-text">
            Bucket là số dư hiện tại theo trạng thái đã chọn; các sự kiện Ledger bên dưới có bộ lọc riêng theo trạng thái. Đổi trạng thái bucket không thay đổi mốc Ledger đã chọn.
          </p>
          {investigation.allStatusBucketAggregateChanged && (
            <p role="status" className="ui-muted-text">
              Tổng số lượng hoặc phân bổ trạng thái bucket đã đổi so với mốc ban đầu. Kết luận đối chiếu bị vô hiệu hóa; hãy làm mới mốc.
            </p>
          )}
          {investigation.allStatusBucketHasRowsAfterAnchor && (
            <p role="status" className="ui-muted-text">
              Có bucket mới ở một trong tám trạng thái sau mốc toàn kho/sản phẩm #{investigation.allStatusBucketAnchorId}. Kết luận đối chiếu đã bị vô hiệu hóa.
            </p>
          )}
          {investigation.bucketHasRowsAfterAnchor && (
            <p role="status" className="ui-muted-text">
              Đã xuất hiện bucket mới sau mốc ID #{investigation.bucketAnchorId}. Phân trang đang giữ mốc cũ; chọn Làm mới mốc Ledger để bắt đầu lại.
            </p>
          )}
          <UiTableScroll>
            <table aria-label="Bucket tồn phục vụ điều tra chênh lệch">
              <thead>
                <tr>
                  <th style={rightNumStyle}>ID bucket</th>
                  <th>Vị trí</th>
                  <th>Lô</th>
                  <th>Sê-ri</th>
                  <th style={rightNumStyle}>Tồn</th>
                  <th style={rightNumStyle}>Đã giữ</th>
                </tr>
              </thead>
              <tbody>
                {investigation.buckets.length === 0 ? (
                  <tr><td colSpan={6}>Không có bucket {statusLabels[investigation.bucketStatus ?? 'Available']}.</td></tr>
                ) : (
                  investigation.buckets.map((b) => (
                    <tr key={b.inventoryStockId}>
                      <td style={rightNumStyle}>#{b.inventoryStockId}</td>
                      <td>{b.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : (b.locationCode ?? '—')}</td>
                      <td>{b.lotNumber ?? '—'}</td>
                      <td>{b.serialNumber ?? '—'}</td>
                      <td style={rightNumStyle}>{numberFormat.format(b.quantity)}</td>
                      <td style={rightNumStyle}>{numberFormat.format(b.reservedQuantity)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
          <nav className="reconciliation-pagination" aria-label="Phân trang bucket tồn">
            <button type="button" disabled={investigating || bucketPageIndex === 0} onClick={() => onChangeBucketPage('previous')}>Bucket trước</button>
            <span>Trang bucket {bucketPageIndex + 1} — tối đa 100 bucket/trang, tổng {investigation.bucketCount}</span>
            <button type="button" disabled={investigating || !investigation.bucketsTruncated || !investigation.nextBucketAfterId} onClick={() => onChangeBucketPage('next')}>Bucket sau</button>
          </nav>

          <h3>Sự kiện Ledger {statusLabels[investigation.eventStatus ?? 'Available'] ?? 'không xác định'} ({investigation.eventCount})</h3>
          <label htmlFor="investigation-event-status">Trạng thái Ledger cần xem</label>
          <select
            id="investigation-event-status"
            value={investigation.eventStatus ?? 'Available'}
            disabled={investigating}
            onChange={(event) => onChangeEventStatus(event.target.value)}
          >
            {Object.entries(statusLabels).map(([status, label]) => (
              <option key={status} value={status}>{label}</option>
            ))}
          </select>
          <p className="ui-muted-text">
            Giao dịch đổi trạng thái xuất hiện ở lịch sử nguồn (âm) và lịch sử đích (dương), nhưng chỉ tính một lần trong tổng tồn mọi trạng thái. Lịch sử cố định theo ID Ledger; bucket/số dư hiện tại không phải snapshot lịch sử.
          </p>
          <UiTableScroll>
            <table aria-label="Sự kiện Ledger phục vụ điều tra chênh lệch">
              <thead>
                <tr>
                  <th style={rightNumStyle}>ID giao dịch</th>
                  <th>Loại</th>
                  <th>Vị trí</th>
                  <th>Lô/sê-ri</th>
                  <th style={rightNumStyle}>Số lượng có dấu</th>
                  <th>Chứng từ</th>
                </tr>
              </thead>
              <tbody>
                {investigation.events.length === 0 ? (
                  <tr><td colSpan={6}>Không có sự kiện Ledger tại mốc này.</td></tr>
                ) : (
                  investigation.events.map((e) => (
                    <tr key={e.transactionId}>
                      <td style={rightNumStyle}>#{e.transactionId}</td>
                      <td>
                        {formatTransactionType(e.transactionType)}
                        {e.transactionType === 'StatusChange' && (
                          <span className="reconciliation-product-name">
                            {statusLabels[e.fromInventoryStatus ?? ''] ?? 'Không rõ'} → {statusLabels[e.toInventoryStatus ?? ''] ?? 'Không rõ'}
                          </span>
                        )}
                      </td>
                      <td>{e.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : (e.locationCode ?? '—')}</td>
                      <td>{e.lotNumber ?? '—'} / {e.serialNumber ?? '—'}</td>
                      <td style={rightNumStyle}>{e.signedQuantity == null ? 'Chưa xác định' : numberFormat.format(e.signedQuantity)}</td>
                      <td>{formatReferenceType(e.referenceType)}{e.referenceId != null ? ` #${e.referenceId}` : ''}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
          <nav className="reconciliation-pagination" aria-label="Phân trang sự kiện Ledger">
            <button type="button" disabled={investigating || eventPageIndex === 0} onClick={() => onChangeEventPage('newer')}>Sự kiện mới hơn</button>
            <span>Trang Ledger {eventPageIndex + 1} — tối đa 50 sự kiện/trang, tổng {investigation.eventCount}</span>
            <button type="button" disabled={investigating || !investigation.eventsTruncated || !investigation.nextEventBeforeId} onClick={() => onChangeEventPage('older')}>Sự kiện cũ hơn</button>
          </nav>
        </>
      )}
      <button type="button" onClick={onClose}>Đóng hồ sơ</button>
    </UiCard>
  );
};
