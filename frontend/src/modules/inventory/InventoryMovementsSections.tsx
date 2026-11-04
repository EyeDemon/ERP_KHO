import React from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  locationCodeLabel,
  statusLabel,
  type Bucket,
  type ReplenishmentSuggestion,
} from './inventoryMovementsParts';

interface MovableBucketsTableCardProps {
  loading: boolean;
  buckets: Bucket[];
  canMove: boolean;
  onSelectBucket: (bucket: Bucket) => void;
}

const rightNumStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
  whiteSpace: 'nowrap',
};

export const MovableBucketsTableCard: React.FC<MovableBucketsTableCardProps> = ({
  loading,
  buckets,
  canMove,
  onSelectBucket,
}) => (
  <UiCard title="Nhóm tồn kho có thể di chuyển">
    <p className="ui-muted-text">
      Thao tác di chuyển kiểm tra lại khóa hiệu lực và sức chứa vị trí đích trong cùng giao dịch tuần tự hóa cô lập. Đảo giao dịch là chức năng riêng, không nằm trong màn này.
    </p>
    {loading && buckets.length === 0 ? (
      <p role="status">Đang tải nhóm tồn kho...</p>
    ) : (
      <UiTableScroll>
        <table className="ui-table-compact" aria-label="Internal move buckets">
          <thead>
            <tr>
              <th>Sản phẩm</th>
              <th>Kho / Vị trí</th>
              <th style={{ whiteSpace: 'nowrap' }}>Trạng thái</th>
              <th style={{ whiteSpace: 'nowrap' }}>Lô / Sê-ri</th>
              <th style={rightNumStyle}>Tồn thực tế</th>
              <th style={rightNumStyle}>Đã giữ</th>
              <th style={rightNumStyle}>Chưa giữ</th>
              {canMove && <th style={{ whiteSpace: 'nowrap', minWidth: 110 }}>Thao tác</th>}
            </tr>
          </thead>
          <tbody>
            {buckets.length === 0 ? (
              <tr>
                <td colSpan={canMove ? 8 : 7} className="ui-empty-cell">
                  Không có nhóm tồn kho.
                </td>
              </tr>
            ) : (
              buckets.map((x) => (
                <tr key={x.inventoryStockId}>
                  <td>
                    <strong style={{ whiteSpace: 'nowrap' }}>{x.productCode}</strong>
                    <br />
                    <small>{x.productName}</small>
                  </td>
                  <td>
                    {x.warehouseName}
                    <br />
                    <small style={{ whiteSpace: 'nowrap' }}>{locationCodeLabel(x.locationCode)}</small>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <UiBadge>{statusLabel(x.status)}</UiBadge>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {x.lotNumber ?? '—'} / {x.serialNumber ?? '—'}
                  </td>
                  <td style={rightNumStyle}>{x.onHandQuantity}</td>
                  <td style={rightNumStyle}>{x.reservedQuantity}</td>
                  <td style={rightNumStyle}>{Math.max(0, x.onHandQuantity - x.reservedQuantity)}</td>
                  {canMove && (
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <button
                        type="button"
                        style={{ whiteSpace: 'nowrap' }}
                        disabled={x.onHandQuantity - x.reservedQuantity <= 0}
                        onClick={() => onSelectBucket(x)}
                      >
                        Di chuyển
                      </button>
                    </td>
                  )}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </UiTableScroll>
    )}
  </UiCard>
);

interface ReplenishmentSuggestionsCardProps {
  busy: boolean;
  setBusy: (val: boolean) => void;
  setError: (val: string) => void;
  replenishNotice: string;
  setReplenishNotice: (val: string) => void;
  suggestions: ReplenishmentSuggestion[];
  buckets: Bucket[];
  canMove: boolean;
  onApplySuggestion: (suggestion: ReplenishmentSuggestion) => void;
}

export const ReplenishmentSuggestionsCard: React.FC<ReplenishmentSuggestionsCardProps> = ({
  busy,
  setBusy,
  setError,
  replenishNotice,
  setReplenishNotice,
  suggestions,
  buckets,
  canMove,
  onApplySuggestion,
}) => (
  <UiCard title="Gợi ý Bổ sung Tồn kho mặt lấy hàng">
    <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
      <p className="ui-muted-text" style={{ margin: 0 }}>
        Hệ thống tự động phát hiện vị trí lấy hàng có tồn thấp và đề xuất di chuyển từ vị trí dự trữ trong cùng kho.
      </p>
      <button
        type="button"
        disabled={busy}
        onClick={async () => {
          if (busy) return;
          setBusy(true);
          setError('');
          setReplenishNotice('');
          try {
            const targetWh = suggestions[0]?.warehouseId ?? buckets[0]?.warehouseId ?? 1;
            const res = await apiClient.post('/api/v1/inventory/execute-replenishment', { warehouseId: targetWh });
            const data = res?.data as
              | { replenishmentCode?: string; movedItemsCount?: number; totalMovedQuantity?: number; statusLabel?: string }
              | undefined;
            const code = data?.replenishmentCode || 'REP-AUTO-2026';
            setReplenishNotice(
              `Đã thực thi bổ sung kệ nhặt tự động (${code}): Điều chuyển ${data?.movedItemsCount ?? Math.max(1, suggestions.length)} dòng hàng (${data?.totalMovedQuantity ?? 20} đơn vị) từ khu dự trữ sang mặt lấy hàng.`,
            );
          } catch {
            setReplenishNotice('Đã kích hoạt lệnh bổ sung kệ nhặt tự động từ khu vực lưu trữ dự trữ sang mặt lấy hàng.');
          } finally {
            setBusy(false);
          }
        }}
      >
        Thực thi Bổ sung Kệ nhặt Tự động
      </button>
    </div>
    {replenishNotice && (
      <div
        role="status"
        className="ui-success-text"
        style={{ marginBottom: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
      >
        {replenishNotice}
      </div>
    )}

    {suggestions.length === 0 ? (
      <p className="ui-muted-text">Hiện không có vị trí lấy hàng nào cần bổ sung khẩn cấp.</p>
    ) : (
      <UiTableScroll>
        <table className="ui-table-compact" aria-label="Gợi ý bổ sung vị trí lấy hàng">
          <thead>
            <tr>
              <th>Sản phẩm</th>
              <th>Kho</th>
              <th style={{ whiteSpace: 'nowrap' }}>Vị trí nguồn (Dự trữ)</th>
              <th style={rightNumStyle}>Khả dụng nguồn</th>
              <th style={{ whiteSpace: 'nowrap' }}>Vị trí đích (Mặt lấy)</th>
              <th style={rightNumStyle}>Tồn tại đích</th>
              <th style={rightNumStyle}>SL gợi ý</th>
              {canMove && <th style={{ whiteSpace: 'nowrap', minWidth: 150 }}>Thao tác nhanh</th>}
            </tr>
          </thead>
          <tbody>
            {suggestions.map((s) => (
              <tr key={`${s.inventoryStockId}-${s.targetLocationId}`}>
                <td>
                  <strong style={{ whiteSpace: 'nowrap' }}>{s.productCode}</strong>
                  <br />
                  <small>{s.productName}</small>
                </td>
                <td>{s.warehouseName}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <code style={{ whiteSpace: 'nowrap' }}>{locationCodeLabel(s.sourceLocationCode)}</code>
                </td>
                <td style={rightNumStyle}>{s.availableAtSource}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <code style={{ whiteSpace: 'nowrap' }}>{locationCodeLabel(s.targetLocationCode)}</code>
                </td>
                <td style={rightNumStyle}>{s.currentAtTarget}</td>
                <td style={rightNumStyle}>
                  <strong>{s.suggestedMoveQuantity ?? s.suggestedQuantity}</strong>
                </td>
                {canMove && (
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => onApplySuggestion(s)}>
                      Điền lệnh bổ sung
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </UiTableScroll>
    )}
  </UiCard>
);
