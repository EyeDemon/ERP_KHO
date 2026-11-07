import React from 'react';
import apiClient from '../../services/apiClient';
import { roundQty4 } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { warehouseLocationTypeLabelVi } from '../../ui/vietnameseEnumLabels';
import {
  type AbcInterleavingData,
  formatPutawayRuleLabel,
  type Location,
  message,
  PUTAWAY_HARD_GUARD_CODES,
  PUTAWAY_RANKING_CODES,
  states,
  type Task,
  taskTypes,
} from './PutawayTaskTypes';

export function PutawayOverviewCard({ tasks }: { tasks: Task[] }) {
  const openOrAssignedCount = tasks.filter(x => x.status === 'Open' || x.status === 'Assigned').length;
  const inProgressCount = tasks.filter(x => x.status === 'InProgress').length;
  const completedCount = tasks.filter(x => x.status === 'Completed').length;
  const exceptionCount = tasks.filter(x => x.status === 'Exception').length;
  const totalRemainingQuantity = roundQty4(tasks.reduce((sum, x) => sum + Math.max(0, x.requiredBaseQuantity - x.movedBaseQuantity), 0));

  return (
    <UiCard title="Tổng quan tiến độ cất hàng" subtitle="Theo dõi số lượng nhiệm vụ chờ phân công, đang thực hiện và ngoại lệ cần xử lý">
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', gap: 10 }}>
        {[
          { label: 'Tổng nhiệm vụ', value: tasks.length },
          { label: 'Chờ / Đã phân công', value: openOrAssignedCount },
          { label: 'Đang thực hiện cất hàng', value: inProgressCount },
          { label: 'Đã hoàn tất', value: completedCount },
          { label: 'Ngoại lệ cần xử lý', value: exceptionCount },
          { label: 'Lượng còn cần cất', value: totalRemainingQuantity },
        ].map(metric => (
          <div key={metric.label} style={{ border: '1px solid #e2e8f0', background: '#f8fafc', padding: '8px 10px', borderRadius: 6, display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569' }}>{metric.label}</span>
            <strong style={{ fontSize: 18, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{metric.value}</strong>
          </div>
        ))}
      </div>
    </UiCard>
  );
}

export function PutawayTaskListCard({
  tasks,
  warehouseLocations,
  detail,
}: {
  tasks: Task[];
  warehouseLocations: Location[];
  detail: (id: number) => Promise<void>;
}) {
  return (
    <UiCard title="Danh sách nhiệm vụ" subtitle="Chọn nhiệm vụ theo phiếu nhập để xem lộ trình kệ đích và xác nhận cất hàng">
      {tasks.length === 0 ? (
        <p>Chưa có nhiệm vụ cất hàng phù hợp.</p>
      ) : (
        <UiTableScroll>
          <table className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
            <thead>
              <tr>
                <th style={{ width: '32%', padding: '6px 6px', fontSize: 11, whiteSpace: 'nowrap' }}>Phiếu nhập</th>
                <th style={{ width: '28%', padding: '6px 6px', fontSize: 11, whiteSpace: 'nowrap' }}>Lộ trình</th>
                <th style={{ width: '22%', padding: '6px 6px', fontSize: 11, whiteSpace: 'nowrap' }}>Trạng thái</th>
                <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 11, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
              </tr>
            </thead>
            <tbody>
              {tasks.map(task => {
                const rawSourceLocationCode = task.items?.[0]?.sourceLocationCode || (task as { fromLocationCode?: string }).fromLocationCode || 'RECEIVING';
                const sourceLocationCode = rawSourceLocationCode.trim().toUpperCase() === 'RECEIVING' ? 'Bãi tiếp nhận' : rawSourceLocationCode;
                const targetLocationCode =
                  (task as { toLocationCode?: string; recommendedLocationCode?: string }).toLocationCode ||
                  (task as { toLocationCode?: string; recommendedLocationCode?: string }).recommendedLocationCode ||
                  (task.items?.[0] as { confirmedLocationCode?: string; recommendedLocationCode?: string; toLocationCode?: string } | undefined)?.confirmedLocationCode ||
                  (task.items?.[0] as { confirmedLocationCode?: string; recommendedLocationCode?: string; toLocationCode?: string } | undefined)?.recommendedLocationCode ||
                  (task.items?.[0] as { confirmedLocationCode?: string; recommendedLocationCode?: string; toLocationCode?: string } | undefined)?.toLocationCode ||
                  warehouseLocations.find(l => l.locationType === 'Storage')?.code ||
                  'Kệ lưu trữ đích';
                return (
                  <tr key={task.id}>
                    <td style={{ padding: '6px 6px', fontSize: 11.5, verticalAlign: 'middle' }}>
                      <button type="button" style={{ whiteSpace: 'nowrap', padding: '5px 10px', minHeight: 30, fontSize: 11.5, fontWeight: 600, lineHeight: 1.2 }} onClick={() => void detail(task.id)}>
                        {task.receiptCode}
                      </button>
                      <div style={{ fontSize: 11, color: '#475569', marginTop: 4, lineHeight: 1.25 }}>
                        {task.warehouseName}
                        {(task.taskType === 'PutbackTask' || task.sourceType === 'PutbackTask') && <div style={{ marginTop: 3 }}><UiBadge tone="warning"><span style={{ whiteSpace: 'nowrap' }}>{taskTypes.PutbackTask}</span></UiBadge></div>}
                        {task.isCrossDock && <div style={{ marginTop: 3 }}><UiBadge tone="success"><span style={{ whiteSpace: 'nowrap' }}>CROSS-DOCK ({task.crossDockAllocatedQty ?? task.movedBaseQuantity})</span></UiBadge></div>}
                      </div>
                    </td>
                    <td style={{ padding: '6px 6px', fontSize: 11, lineHeight: 1.3, verticalAlign: 'middle' }}>
                      <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{sourceLocationCode}</span>
                      <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {targetLocationCode}</span>
                    </td>
                    <td style={{ padding: '6px 6px', fontSize: 11, verticalAlign: 'middle' }}>
                      <UiBadge tone={task.status === 'Completed' ? 'success' : task.status === 'Exception' || task.status === 'Cancelled' ? 'danger' : task.status === 'InProgress' || task.status === 'Assigned' ? 'info' : 'neutral'}>
                        <span style={{ whiteSpace: 'nowrap', lineHeight: 1.2, fontSize: 11 }}>{states[task.status] ?? 'Không xác định'}</span>
                      </UiBadge>
                    </td>
                    <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
                      <strong>{task.movedBaseQuantity}</strong> / {task.requiredBaseQuantity}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
      )}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0', fontSize: 12.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
        <span>Hiển thị {tasks.length === 0 ? 0 : 1}–{tasks.length} / Tổng {tasks.length} nhiệm vụ cất hàng</span>
        <span>Trang 1 / 1 (Cỡ trang: 20)</span>
      </div>
    </UiCard>
  );
}

export function PutawayWorkspaceAndSlottingCards({
  tasks, selected, setSelected, detail, warehouseLocations, locations,
  workspaceTaskId, setWorkspaceTaskId, workspaceFromLocId, setWorkspaceFromLocId,
  workspaceToLocId, setWorkspaceToLocId, workspaceQty, setWorkspaceQty,
  setDestination, setQuantity, busy, slottingBusy, evaluateSlotting,
  executeCrossDock, evaluateAbcSlottingInterleaving, directedSuggestions,
  setError, setSlottingEvaluationResult, abcInterleavingData, crossDockNotice, slottingEvaluationResult,
}: {
  tasks: Task[]; selected: Task | null; setSelected: React.Dispatch<React.SetStateAction<Task | null>>;
  detail: (id: number) => Promise<void>; warehouseLocations: Location[]; locations: Record<number, Location[]>;
  workspaceTaskId: string; setWorkspaceTaskId: React.Dispatch<React.SetStateAction<string>>;
  workspaceFromLocId: string; setWorkspaceFromLocId: React.Dispatch<React.SetStateAction<string>>;
  workspaceToLocId: string; setWorkspaceToLocId: React.Dispatch<React.SetStateAction<string>>;
  workspaceQty: string; setWorkspaceQty: React.Dispatch<React.SetStateAction<string>>;
  setDestination: React.Dispatch<React.SetStateAction<Record<number, number>>>;
  setQuantity: React.Dispatch<React.SetStateAction<Record<number, number | string>>>;
  busy: string; slottingBusy: boolean; evaluateSlotting: () => Promise<void>;
  executeCrossDock: () => Promise<void>; evaluateAbcSlottingInterleaving: () => Promise<void>;
  directedSuggestions: Record<number, { recommendedBinCode: string; reasonText: string }>;
  setError: React.Dispatch<React.SetStateAction<string>>;
  setSlottingEvaluationResult: React.Dispatch<React.SetStateAction<string>>;
  abcInterleavingData: AbcInterleavingData | null; crossDockNotice: string; slottingEvaluationResult: string;
}) {
  return (
    <>
      <UiCard title="Điều phối cất hàng lên kệ" subtitle="Chọn nhanh nhiệm vụ phiếu nhập, vị trí nguồn, vị trí kệ đích và số lượng thực cất">
        <div className="putaway-dispatch-form-grid">
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 0 }}>
            <span>Phiếu nhập / Nhiệm vụ cất hàng</span>
            <select aria-label="Chọn phiếu nhập cất hàng" style={{ minHeight: 44, width: '100%' }} value={workspaceTaskId} onChange={e => { setWorkspaceTaskId(e.target.value); if (e.target.value) void detail(Number(e.target.value)); else setSelected(null); }}>
              <option value="">Chọn phiếu nhập</option>
              {tasks.map(t => <option key={t.id} value={String(t.id)}>{t.receiptCode} ({states[t.status] ?? t.status})</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 0 }}>
            <span>Vị trí nguồn (Bãi tiếp nhận / đệm)</span>
            <select aria-label="Chọn vị trí nguồn cất hàng" style={{ minHeight: 44, width: '100%', fontSize: 12.5, paddingLeft: 8, paddingRight: 24 }} value={workspaceFromLocId} onChange={e => setWorkspaceFromLocId(e.target.value)}>
              <option value="">Chọn vị trí nguồn</option>
              {(warehouseLocations.filter(l => ['receiving', 'staging'].includes(l.locationType?.toLowerCase())).length > 0 ? warehouseLocations.filter(l => ['receiving', 'staging'].includes(l.locationType?.toLowerCase())) : warehouseLocations).map(loc => <option key={loc.id} value={String(loc.id)}>{loc.code} — {warehouseLocationTypeLabelVi(loc.locationType).replace('Khu tập kết xuất hàng', 'Khu tập kết xuất')}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 0 }}>
            <span>Vị trí đích (Kệ lưu trữ)</span>
            <select aria-label="Chọn vị trí đích cất hàng" style={{ minHeight: 44, width: '100%', fontSize: 12.5, paddingLeft: 8, paddingRight: 24 }} value={workspaceToLocId} onChange={e => { setWorkspaceToLocId(e.target.value); const firstItem = selected?.items?.[0]; if (firstItem && e.target.value) setDestination(v => ({ ...v, [firstItem.id]: Number(e.target.value) })); }}>
              <option value="">Chọn vị trí đích</option>
              {(warehouseLocations.filter(l => l.locationType?.toLowerCase() === 'storage').length > 0 ? warehouseLocations.filter(l => l.locationType?.toLowerCase() === 'storage') : [...warehouseLocations, ...Object.values(locations).flat()]).map(loc => <option key={loc.id} value={String(loc.id)}>{loc.code} — {warehouseLocationTypeLabelVi(loc.locationType).replace('Khu tập kết xuất hàng', 'Khu tập kết xuất')}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Số lượng thực cất</span>
            <input aria-label="Số lượng thực cất điều phối" style={{ minHeight: 44 }} inputMode="decimal" role="spinbutton" min="0.0001" step="0.0001" value={workspaceQty} onChange={e => { setWorkspaceQty(e.target.value); const firstItem = selected?.items?.[0]; if (firstItem) setQuantity(v => ({ ...v, [firstItem.id]: e.target.value === '' ? '' : roundQty4(Number(e.target.value)) })); }} />
          </label>
        </div>
      </UiCard>

      <UiCard title="Gợi ý vị trí cất hàng tự động" subtitle="Tối ưu vị trí kệ đích theo nguyên tắc Ưu tiên Hạn dùng, sức chứa và điều phối chuyến kép">
        <div className="ui-stack" style={{ gap: 10 }}>
          <div className="ui-inline-wrap" style={{ marginTop: 2 }}>
            <button type="button" style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8', minHeight: 40 }} disabled={!!busy || slottingBusy} onClick={() => void evaluateSlotting()}>
              Chấm điểm Tối ưu Xếp dỡ &amp; Khớp Vị trí Kệ
            </button>
            <button type="button" data-testid="btn-execute-cross-dock" style={{ minHeight: 40 }} disabled={!!busy || slottingBusy || tasks.length === 0} onClick={() => void executeCrossDock()}>
              Thực thi Cross-Dock (Bãi Nhận → Khu vực Tập kết Xuất hàng)
            </button>
            <button type="button" data-testid="btn-evaluate-abc-interleaving" style={{ minHeight: 40 }} disabled={!!busy || slottingBusy} onClick={() => void evaluateAbcSlottingInterleaving()}>
              Phân tích Vị trí Động ABC (Dynamic Slotting) &amp; Ghép cặp Chuyến kép (Task Interleaving)
            </button>
            <button
              type="button" className="ui-btn-secondary" style={{ minHeight: 40 }} disabled={!!busy || slottingBusy}
              onClick={() => {
                setError('');
                const dynamicCodes = Array.from(new Set([...Object.values(locations).flat().map(l => l.code), ...Object.values(directedSuggestions).map(s => s.recommendedBinCode), ...(selected?.items?.map(i => i.sourceLocationCode) ?? [])].filter(Boolean)));
                const targetLocationCodes = dynamicCodes.length > 0 ? dynamicCodes.slice(0, 4) : warehouseLocations.slice(0, 4).map(l => l.code);
                void apiClient.post('/api/v1/warehouses/locations/barcode-sheet', { warehouseId: selected?.warehouseId ?? tasks[0]?.warehouseId ?? 1, locationCodes: targetLocationCodes })
                  .then(() => setSlottingEvaluationResult(`Đã tạo nhãn mã vạch vị trí kệ (${targetLocationCodes.join(', ')}) sẵn sàng in.`))
                  .catch(e => setError(message(e)));
              }}
            >
              In Mã vạch Vị trí Kệ
            </button>
          </div>
          {abcInterleavingData && (
            <div role="region" aria-label="Kết quả Tối ưu vị trí ABC và Ghép cặp Chuyến kép" data-testid="epic90-1-abc-slotting-interleaving-panel" style={{ marginTop: 4, padding: '10px 14px', borderRadius: 8, background: '#f0fdf4', color: '#065f46', border: '1px solid #86efac', fontSize: 13, lineHeight: 1.5 }}>
              <div style={{ fontWeight: 700, marginBottom: 4 }}>Tối ưu Vị trí Động ABC (Dynamic ABC Slotting) &amp; Ghép cặp Chuyến kép (Task Interleaving): {abcInterleavingData.evaluationId}</div>
              <div style={{ marginBottom: 4 }}>{abcInterleavingData.statusLabel}</div>
              {abcInterleavingData.reslottingRecommendations && abcInterleavingData.reslottingRecommendations.length > 0 && (
                <div style={{ marginBottom: 4 }}><strong>Đề xuất Đảo kệ Động (ABC Velocity):</strong>{' '}{abcInterleavingData.reslottingRecommendations.slice(0, 2).map((rec, idx) => <span key={idx} style={{ display: 'inline-block', marginRight: 10 }}>[{rec.abcClass}] <code>{rec.productCode}</code> ({rec.fromBinCode} → <strong>{rec.recommendedBinCode}</strong> • Tiết kiệm {rec.estimatedTravelSavingMeters}m)</span>)}</div>
              )}
              {abcInterleavingData.interleavedTaskPairs && abcInterleavingData.interleavedTaskPairs.length > 0 && (
                <div><strong>Chu trình Chuyến kép (Dual-Command Cycle):</strong>{' '}{abcInterleavingData.interleavedTaskPairs.slice(0, 1).map(pair => <span key={pair.cycleCode}><code>{pair.cycleCode}</code>: {pair.interleavedRouteVi} • Giảm di chuyển rỗng: <strong>{pair.estimatedDeadheadSavedPercent}%</strong></span>)}</div>
              )}
            </div>
          )}
          {crossDockNotice && <div role="status" data-testid="cross-dock-status-banner" style={{ marginTop: 4, padding: '8px 12px', borderRadius: 8, background: '#eff6ff', color: '#1e3a8a', border: '1px solid #93c5fd', fontSize: 13, lineHeight: 1.5, fontWeight: 600 }}>{crossDockNotice}</div>}
          {slottingEvaluationResult && <div role="status" style={{ marginTop: 4, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>{slottingEvaluationResult}</div>}
          <details style={{ marginTop: 4, padding: '8px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <summary style={{ cursor: 'pointer', fontSize: 13, fontWeight: 600, color: '#334155' }}>Quy tắc gợi ý vị trí kệ tự động (Ưu tiên Hạn dùng &amp; Sức chứa) — Chi tiết quy tắc &amp; kỹ thuật hệ thống</summary>
            <div className="ui-stack" style={{ gap: 8, marginTop: 8 }}>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, alignItems: 'center' }}><span style={{ fontWeight: 600 }}>Tiêu chí xếp hạng (5 mức):</span>{PUTAWAY_RANKING_CODES.map(code => <UiBadge key={code} tone="success">{formatPutawayRuleLabel(code)}</UiBadge>)}</div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, alignItems: 'center' }}><span style={{ fontWeight: 600 }}>Ràng buộc cứng (4 mức):</span>{PUTAWAY_HARD_GUARD_CODES.map(code => <UiBadge key={code}>{formatPutawayRuleLabel(code)}</UiBadge>)}</div>
              <div data-testid="putaway-ledger-pair-panel" style={{ marginTop: 4, padding: '8px 12px', borderRadius: 8, background: '#ffffff', border: '1px solid #cbd5e1', fontSize: 13 }}>
                <strong>Bút toán Sổ cái Đối ứng 2 Dòng:</strong> Chân nguồn <code>-Q (Bãi tiếp nhận)</code> • Chân đích <code>+Q (Kệ lưu trữ đích)</code> (hoặc <code>Khu vực tập kết xuất</code> khi Cross-Dock) — Bảo toàn tổng tồn kho toàn kho (<code>Δ = 0</code>).
              </div>
            </div>
          </details>
        </div>
      </UiCard>
    </>
  );
}
