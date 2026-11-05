import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { currentUserId } from '../../services/authorization';
import { roundQty4, sanitizeScannerCode } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import type { AlternateBinItem, PickingTask } from './pickingTasksTypes';
import { failureMessage, lineStates, sourceTypeLabels, states, tone } from './pickingTasksTypes';

interface PickingTasksGridProps {
  tasks: PickingTask[];
  selected: PickingTask | null;
  busy: string;
  canAssign: boolean;
  canExecute: boolean;
  canShortPick: boolean;
  canOverride: boolean;
  detail: (id: number) => Promise<void>;
  mutate: (key: string, path: string, body: object) => Promise<void>;
  setError: (msg: string) => void;
  isHandheldViewport?: boolean;
}

export function PickingTasksGrid({
  tasks,
  selected,
  busy,
  canAssign,
  canExecute,
  canShortPick,
  canOverride,
  detail,
  mutate,
  setError,
  isHandheldViewport = false,
}: PickingTasksGridProps) {
  const [scan, setScan] = useState<Record<number, { location: string; product: string; quantity: string }>>({});
  const [shortReason, setShortReason] = useState<Record<number, string>>({});
  const [resolution, setResolution] = useState<Record<number, string>>({});
  const [resolutionNote, setResolutionNote] = useState<Record<number, string>>({});
  const [alternateBinsByLine, setAlternateBinsByLine] = useState<Record<number, AlternateBinItem[]>>({});
  const [alternateBinsLoadingLine, setAlternateBinsLoadingLine] = useState<number | null>(null);

  const suggestAlternateBins = async (taskId: number, lineId: number, productId: number) => {
    setAlternateBinsLoadingLine(lineId);
    setError('');
    try {
      const res = await apiClient.get(`/api/v1/picking-tasks/${taskId}/alternate-bins`, { params: { taskLineId: lineId, productId } });
      const raw = res?.data?.items ?? res?.data?.alternateBins ?? res?.data;
      const list = Array.isArray(raw)
        ? raw.map((b: Record<string, unknown>) => ({
            locationCode: String(b.locationCode ?? b.binCode ?? ''),
            lotNumber: String(b.lotNumber ?? b.lotCode ?? ''),
            expiryDate: String(b.expiryDate ?? ''),
            availableQty: Number(b.availableQty ?? b.availableQuantity ?? 0),
          }))
        : [];
      setAlternateBinsByLine(prev => ({ ...prev, [lineId]: list }));
    } catch (err) {
      setError(failureMessage(err));
      setAlternateBinsByLine(prev => ({ ...prev, [lineId]: [] }));
    } finally {
      setAlternateBinsLoadingLine(null);
    }
  };

  const taskListCard = (
      <UiCard title="Danh sách nhiệm vụ lấy hàng">
        {tasks.length === 0 ? (
          <p>Chưa có nhiệm vụ Picking phù hợp.</p>
        ) : (
          <UiTableScroll>
            <table aria-label="Danh sách nhiệm vụ Picking" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
              <thead>
                <tr>
                  <th style={{ width: '30%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Nhiệm vụ</th>
                  <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Lộ trình</th>
                  <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
                  <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
                </tr>
              </thead>
              <tbody>
                {tasks.map(task => {
                  const sourceLabel = task.sourceCode ?? (sourceTypeLabels[task.sourceType] ?? task.sourceType);
                  const pickLocationLabel = task.lines?.[0]?.sourceLocationCode ?? 'BIN-PICK';
                  const warehouseDisplay = formatWarehouseLabel(task.warehouseName, undefined, task.warehouseId);
                  return (
                    <tr key={task.id}>
                      <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                        <button type="button" style={{ whiteSpace: 'nowrap', padding: '4px 8px', minHeight: 28, fontSize: 11, lineHeight: 1.2 }} onClick={() => void detail(task.id)}>
                          {task.taskCode}
                        </button>
                        <div style={{ fontSize: 10.5, color: '#475569', marginTop: 3, lineHeight: 1.2 }}>{warehouseDisplay}</div>
                      </td>
                      <td style={{ padding: '6px 4px', fontSize: 10.5, lineHeight: 1.25, verticalAlign: 'middle' }}>
                        <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{sourceLabel}</span>
                        <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {pickLocationLabel}</span>
                      </td>
                      <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                        <UiBadge tone={tone(task.status)}>
                          <span style={{ whiteSpace: 'normal', lineHeight: 1.2, fontSize: 10.5, display: 'inline-block', maxWidth: 'none', textAlign: 'center' }}>
                            {states[task.status] ?? task.status}
                          </span>
                        </UiBadge>
                      </td>
                      <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
                        <strong>{task.pickedQuantity}</strong> / {task.requestedQuantity}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
  );

  const selectedDetailCard = selected ? (
        <UiCard title={`Chi tiết ${selected.taskCode}`}>
          <div className="ui-inline-wrap">
            <UiBadge tone={tone(selected.status)}>{states[selected.status] ?? selected.status}</UiBadge>
            <span className="ui-muted-text">
              {selected.sourceCode ?? (sourceTypeLabels[selected.sourceType] ?? selected.sourceType)} • {formatWarehouseLabel(selected.warehouseName, undefined, selected.warehouseId)}
            </span>
            <span className="ui-muted-text">Tiến độ {selected.pickedQuantity}/{selected.requestedQuantity}</span>
          </div>
          <p className="ui-muted-text">Xác thực vị trí kệ nguồn, mã sản phẩm và số lượng lấy thực tế theo phân bổ tồn kho.</p>

          <div className="ui-inline-wrap">
            {canAssign && selected.status === 'Open' && (
              <button type="button" disabled={!!busy} onClick={() => void mutate(`picking-assign-${selected.id}`, `/api/v1/picking-tasks/${selected.id}/assign`, { assignedUserId: currentUserId(), rowVersion: selected.rowVersion })}>Nhận nhiệm vụ</button>
            )}
            {canExecute && selected.status === 'Assigned' && (
              <button type="button" disabled={!!busy} onClick={() => void mutate(`picking-start-${selected.id}`, `/api/v1/picking-tasks/${selected.id}/start`, { rowVersion: selected.rowVersion })}>Bắt đầu Picking</button>
            )}
            {canExecute && ['InProgress', 'Resolved'].includes(selected.status) && (
              <button type="button" disabled={!!busy} onClick={() => void mutate(`picking-complete-${selected.id}`, `/api/v1/picking-tasks/${selected.id}/complete`, { rowVersion: selected.rowVersion })}>Hoàn tất Picking</button>
            )}
          </div>

          <div className="ui-stack">
            <div data-testid="s-shape-pick-route-banner" style={{ padding: '8px 12px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e3a8a', fontSize: 12.5, fontWeight: 600 }}>
              Lộ trình nhặt hàng tối ưu hình chữ S (Dãy → Kệ → Tầng → Ô)
            </div>
            {[...(selected.lines ?? [])].sort((a, b) => (a.sourceLocationCode || '').localeCompare(b.sourceLocationCode || '')).map(line => (
              <div className="ui-subcard" key={line.id}>
                <div className="ui-subcard-title">{line.productCode} — {line.productName}</div>
                <div className="ui-inline-wrap">
                  <UiBadge tone={tone(line.status)}>{lineStates[line.status] ?? line.status}</UiBadge>
                  <UiBadge>Vị trí: {line.sourceLocationCode}</UiBadge>
                  <span>Đã lấy <strong>{line.pickedQuantity}</strong> / {line.requestedQuantity} • Còn {line.remainingQuantity}</span>
                  <button type="button" data-testid="suggest-alternate-bin-btn" disabled={!!busy || alternateBinsLoadingLine === line.id} onClick={() => void suggestAlternateBins(selected.id, line.id, line.productId)}>
                    {alternateBinsLoadingLine === line.id ? 'Đang tìm kệ thay thế...' : 'Gợi ý Kệ khác (Hết hàng kệ này)'}
                  </button>
                  {alternateBinsByLine[line.id] && alternateBinsByLine[line.id].length > 0 && (
                    <div data-testid={`alternate-fefo-bins-${line.id}`} style={{ marginTop: 8, padding: '8px 10px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1', display: 'flex', flexDirection: 'column', gap: 6 }}>
                      <strong style={{ fontSize: 12, color: '#0f172a' }}>Danh sách Kệ Thay thế Ưu tiên Hạn dùng có Tồn Khả dụng:</strong>
                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                        {alternateBinsByLine[line.id].map(alt => (
                          <div key={`${alt.locationCode}-${alt.lotNumber}`} style={{ display: 'inline-flex', alignItems: 'center', gap: 8, padding: '4px 8px', borderRadius: 6, background: '#ffffff', border: '1px solid #93c5fd', fontSize: 12 }}>
                            <span><strong>{alt.locationCode}</strong> • Lô: {alt.lotNumber || 'Lô tiêu chuẩn'} • HSD: {alt.expiryDate || 'Không giới hạn'} • Tồn: <strong>{alt.availableQty}</strong></span>
                            <button type="button" onClick={() => setScan(v => ({ ...v, [line.id]: { ...(v[line.id] ?? { product: line.productCode, quantity: String(line.remainingQuantity) }), location: alt.locationCode } }))}>
                              Chọn kệ thay thế
                            </button>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>

                {canExecute && selected.status === 'InProgress' && ['Open', 'InProgress'].includes(line.status) && line.remainingQuantity > 0 && (
                  <div className="ui-inline-wrap">
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Quét location
                      <input aria-label={`Location barcode ${line.productCode}`} value={scan[line.id]?.location ?? ''} onChange={e => setScan(v => ({ ...v, [line.id]: { ...(v[line.id] ?? { product: '', quantity: '' }), location: e.target.value } }))} />
                    </label>
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Quét sản phẩm
                      <input aria-label={`Product barcode ${line.productCode}`} value={scan[line.id]?.product ?? ''} onChange={e => setScan(v => ({ ...v, [line.id]: { ...(v[line.id] ?? { location: '', quantity: '' }), product: e.target.value } }))} />
                    </label>
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Số lượng
                      <input aria-label={`Pick quantity ${line.productCode}`} type="number" min="0" step="any" value={scan[line.id]?.quantity ?? ''} onChange={e => setScan(v => ({ ...v, [line.id]: { ...(v[line.id] ?? { location: '', product: '' }), quantity: e.target.value } }))} />
                    </label>
                    <button
                      type="button"
                      disabled={!!busy || !sanitizeScannerCode(scan[line.id]?.location ?? '') || !sanitizeScannerCode(scan[line.id]?.product ?? '') || !Number.isFinite(Number(scan[line.id]?.quantity)) || Number(scan[line.id]?.quantity) <= 0}
                      onClick={() => void mutate(`picking-pick-${selected.id}-${line.id}`, `/api/v1/picking-tasks/${selected.id}/pick`, {
                        taskLineId: line.id,
                        locationBarcode: sanitizeScannerCode(scan[line.id]?.location ?? ''),
                        productBarcode: sanitizeScannerCode(scan[line.id]?.product ?? ''),
                        quantity: roundQty4(Number(scan[line.id]?.quantity)),
                        rowVersion: selected.rowVersion,
                      })}
                    >
                      Xác nhận Pick
                    </button>
                  </div>
                )}

                {canShortPick && selected.status === 'InProgress' && ['Open', 'InProgress'].includes(line.status) && line.remainingQuantity > 0 && (
                  <div className="ui-inline-wrap">
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Lý do Short Pick
                      <input aria-label={`Short pick reason ${line.productCode}`} value={shortReason[line.id] ?? ''} onChange={e => setShortReason(v => ({ ...v, [line.id]: e.target.value }))} />
                    </label>
                    <button
                      type="button"
                      disabled={!!busy || !(shortReason[line.id] ?? '').trim()}
                      onClick={() => void mutate(`picking-short-${selected.id}-${line.id}`, `/api/v1/picking-tasks/${selected.id}/report-short-pick`, {
                        taskLineId: line.id,
                        actualPickedQuantity: line.pickedQuantity,
                        reason: shortReason[line.id],
                        rowVersion: selected.rowVersion,
                      })}
                    >
                      Báo Short Pick
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>

          {(selected.shortPicks ?? []).filter(item => item.status === 'Open').map(item => (
            <div className="ui-subcard" key={item.id}>
              <div className="ui-subcard-title">Short Pick #{item.id}</div>
              <p>Dòng #{item.pickingTaskLineId} • Thiếu <strong>{item.shortageQuantity}</strong> • {item.reason}</p>
              {canShortPick && (
                <div className="ui-inline-wrap">
                  <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Phương án
                    <select aria-label={`Short pick resolution ${item.id}`} value={resolution[item.id] ?? ''} onChange={e => setResolution(v => ({ ...v, [item.id]: e.target.value }))}>
                      <option value="">Chọn phương án</option>
                      <option value="AlternativeLocation">Vị trí khác</option>
                      <option value="Backorder">Tạo đơn bù sau (Backorder)</option>
                      <option value="CancelRemainder">Hủy phần thiếu</option>
                    </select>
                  </label>
                  <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Ghi chú
                    <input aria-label={`Short pick note ${item.id}`} value={resolutionNote[item.id] ?? ''} onChange={e => setResolutionNote(v => ({ ...v, [item.id]: e.target.value }))} />
                  </label>
                  <button
                    type="button"
                    disabled={!!busy || !resolution[item.id]}
                    onClick={() => void mutate(`picking-resolve-${selected.id}-${item.id}`, `/api/v1/picking-tasks/${selected.id}/short-picks/${item.id}/resolve`, {
                      resolution: resolution[item.id],
                      note: resolutionNote[item.id],
                      rowVersion: selected.rowVersion,
                    })}
                  >
                    Xử lý Short Pick
                  </button>
                </div>
              )}
              {canOverride && (
                <button type="button" disabled={!!busy || !(resolutionNote[item.id] ?? '').trim()} onClick={() => void mutate(`picking-override-${selected.id}-${item.id}`, `/api/v1/picking-tasks/${selected.id}/short-picks/${item.id}/override`, { reason: resolutionNote[item.id], rowVersion: selected.rowVersion })}>
                  Supervisor override
                </button>
              )}
            </div>
          ))}
        </UiCard>
  ) : null;

  return (
    <div
      className="ui-grid-2"
      style={{
        display: 'grid',
        gridTemplateColumns: selected ? 'repeat(auto-fit, minmax(min(100%, 420px), 1fr))' : 'minmax(0, 1fr)',
        gap: isHandheldViewport ? 8 : 16,
        alignItems: 'start',
      }}
    >
      {isHandheldViewport ? (
        <>
          {selectedDetailCard}
          {taskListCard}
        </>
      ) : (
        <>
          {taskListCard}
          {selectedDetailCard}
        </>
      )}
    </div>
  );
}
