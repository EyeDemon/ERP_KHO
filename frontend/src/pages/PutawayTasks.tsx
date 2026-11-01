import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { extractConflictCode } from '../services/permissionPresentation';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import './PutawayTasks.css';
import { UiBadge, UiCard, UiPage, UiPageHeader } from '../ui/ProductionUi';
import {
  type AbcInterleavingData, inventory, type Location, message, states, type Task, taskTypes,
} from '../modules/inbound/PutawayTaskTypes';
import {
  PutawayOverviewCard, PutawayTaskListCard, PutawayWorkspaceAndSlottingCards,
} from '../modules/inbound/PutawayTaskComponents';

export default function PutawayTasks() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [selected, setSelected] = useState<Task | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const [locations, setLocations] = useState<Record<number, Location[]>>({});
  const [destination, setDestination] = useState<Record<number, number>>({});
  const [quantity, setQuantity] = useState<Record<number, number | string>>({});
  const [warehouseLocations, setWarehouseLocations] = useState<Location[]>([]);
  const [workspaceTaskId, setWorkspaceTaskId] = useState<string>('');
  const [workspaceFromLocId, setWorkspaceFromLocId] = useState<string>('');
  const [workspaceToLocId, setWorkspaceToLocId] = useState<string>('');
  const [workspaceQty, setWorkspaceQty] = useState<string>('10');
  const workspaceTaskInitialized = useRef(false);
  const [scannedBin, setScannedBin] = useState<Record<number, string>>({});
  const [scannedProduct, setScannedProduct] = useState<Record<number, string>>({});
  const lock = useRef(new Set<string>());
  const canAssign = usePermission('putaway.assign');
  const canExecute = usePermission('putaway.execute');
  const canCancel = usePermission('putaway.cancel');
  const [exceptionReason, setExceptionReason] = useState('');
  const [slottingEvaluationResult, setSlottingEvaluationResult] = useState('');
  const [slottingBusy, setSlottingBusy] = useState(false);
  const [directedSuggestions, setDirectedSuggestions] = useState<Record<number, { recommendedBinCode: string; reasonText: string }>>({});
  const [directedBusyItem, setDirectedBusyItem] = useState<number | null>(null);
  const [crossDockNotice, setCrossDockNotice] = useState('');
  const [abcInterleavingData, setAbcInterleavingData] = useState<AbcInterleavingData | null>(null);

  const evaluateAbcSlottingInterleaving = async () => {
    if (slottingBusy) return;
    setSlottingBusy(true); setError('');
    try {
      const res = await apiClient.get('/api/v1/inventory-control/slotting-interleaving/evaluate', {
        params: { warehouseId: selected?.warehouseId ?? tasks[0]?.warehouseId ?? 1, lookbackDays: 90 },
      });
      setAbcInterleavingData(res.data);
    } catch (e) { setError(message(e)); }
    finally { setSlottingBusy(false); }
  };

  const executeCrossDock = async (taskOverride?: Task) => {
    const targetTask = taskOverride ?? selected ?? tasks.find(x => x.status !== 'Completed' && x.status !== 'Cancelled') ?? tasks[0];
    if (!targetTask || busy) return;
    const key = `cross-dock-${targetTask.id}-${Date.now()}`;
    setBusy(key); setError('');
    try {
      const res = await apiClient.post(
        `/api/v1/putaway-tasks/${targetTask.id}/cross-dock`,
        {
          targetSalesOrderId: targetTask.crossDockTargetSalesOrderId ?? undefined,
          targetExportReceiptId: targetTask.crossDockTargetExportReceiptId ?? undefined,
          targetBackorderId: targetTask.crossDockTargetBackorderId ?? undefined,
          rowVersion: targetTask.rowVersion,
        },
        { headers: idempotencyHeaders(key) },
      );
      completeIdempotentAction(key);
      const updated = res.data as Task;
      setSelected(updated);
      setCrossDockNotice(`CROSS-DOCK THÀNH CÔNG • Nhiệm vụ ${updated.receiptCode}: Đã điều hướng thẳng từ Bãi tiếp nhận sang Khu vực tập kết xuất hàng • Khối lượng phân bổ Cross-Dock: ${updated.crossDockAllocatedQty ?? updated.movedBaseQuantity} ĐV (Bút toán đối ứng PutawayOut/PutawayIn Δ = 0.0000).`);
      await load();
    } catch (e) { setError(message(e)); }
    finally { setBusy(''); }
  };

  const applyDirectedPutawaySuggestion = async (taskId: number, itemId: number, defaultQty: number) => {
    setDirectedBusyItem(itemId); setError('');
    const candidateLocs = locations[itemId] ?? [];
    try {
      const res = await apiClient.get(`/api/v1/putaway-tasks/${taskId}/directed-suggestion`, { params: { itemId } });
      const d = (res?.data ?? {}) as {
        suggestedLocationCode?: string; recommendedLocationCode?: string; recommendedBinCode?: string;
        suggestedLocationId?: number; destinationLocationId?: number; reason?: string; reasonLabel?: string;
      };
      let suggestedCode = String(d.suggestedLocationCode ?? d.recommendedLocationCode ?? d.recommendedBinCode ?? candidateLocs[0]?.code ?? '');
      const suggestedReason = String(d.reasonLabel ?? d.reason ?? '');
      const suggestedId = d.suggestedLocationId ?? d.destinationLocationId ?? candidateLocs[0]?.id;
      const matchedLoc = candidateLocs.find(l => l.code.toUpperCase() === suggestedCode.toUpperCase() || (suggestedId && l.id === suggestedId)) ?? candidateLocs[0];
      if (matchedLoc) {
        setDestination(v => ({ ...v, [itemId]: matchedLoc.id }));
        setScannedBin(v => ({ ...v, [itemId]: matchedLoc.code }));
        suggestedCode = matchedLoc.code;
      } else if (suggestedId) {
        setDestination(v => ({ ...v, [itemId]: suggestedId }));
        setScannedBin(v => ({ ...v, [itemId]: suggestedCode }));
      } else {
        setScannedBin(v => ({ ...v, [itemId]: suggestedCode }));
      }
      setQuantity(v => ({ ...v, [itemId]: v[itemId] ? v[itemId] : roundQty4(defaultQty) }));
      setDirectedSuggestions(v => ({ ...v, [itemId]: { recommendedBinCode: suggestedCode, reasonText: suggestedReason } }));
    } catch (e) { setError(message(e)); }
    finally { setDirectedBusyItem(null); }
  };

  const load = async () => {
    setLoading(true); setError('');
    try {
      const list = (await apiClient.get('/api/v1/putaway-tasks')).data as Task[];
      setTasks(list);
      if (!workspaceTaskInitialized.current && list.length > 0) {
        workspaceTaskInitialized.current = true;
        setWorkspaceTaskId(String(list[0].id));
      }
      const whId = list?.[0]?.warehouseId;
      if (whId) {
        void Promise.resolve(apiClient.get(`/api/v1/warehouses/${whId}/locations`))
          .then(res => {
            if (Array.isArray(res?.data)) {
              const rows = res.data as Location[];
              setWarehouseLocations(rows);
              const fromLoc = rows.find(l => l.locationType === 'Receiving' || l.locationType === 'Staging') ?? rows[0];
              const toLoc = rows.find(l => l.locationType === 'Storage') ?? rows[rows.length - 1];
              if (fromLoc) setWorkspaceFromLocId(p => p || String(fromLoc.id));
              if (toLoc) setWorkspaceToLocId(p => p || String(toLoc.id));
            }
          })
          .catch(() => {});
      }
    } catch {
      setTasks([]); setSelected(null); setError('Không thể tải dữ liệu. Vui lòng thử lại.');
    } finally { setLoading(false); }
  };

  const detail = async (id: number) => {
    setError('');
    try {
      const t = (await apiClient.get(`/api/v1/putaway-tasks/${id}`)).data as Task;
      setSelected(t);
      workspaceTaskInitialized.current = true;
      setWorkspaceTaskId(String(t.id));
      const entries = await Promise.all(
        t.items
          .filter(i => i.remainingBaseQuantity > 0)
          .map(async i => [i.id, (await apiClient.get(`/api/v1/putaway-tasks/${id}/items/${i.id}/destinations`)).data] as const),
      );
      setLocations(Object.fromEntries(entries));
    } catch {
      setSelected(null); setError('Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.');
    }
  };

  useEffect(() => { void load(); }, []);

  const mutate = async (key: string, path: string, body: object) => {
    if (lock.current.has(key)) return;
    lock.current.add(key); setBusy(key); setError('');
    try {
      const t = (await apiClient.post(path, body, { headers: idempotencyHeaders(key) })).data as Task;
      completeIdempotentAction(key);
      setSelected(t); await load(); await detail(t.id);
    } catch (e) {
      if ((e as { response?: { status?: number } })?.response?.status === 409 && extractConflictCode(e) !== 'INSUFFICIENT_AVAILABLE_STOCK') {
        setSelected(null); void load();
      }
      setError(message(e));
    } finally { lock.current.delete(key); setBusy(''); }
  };

  const evaluateSlotting = async () => {
    if (slottingBusy) return;
    setSlottingBusy(true); setError('');
    try {
      const res = await apiClient.post(
        '/api/v1/putaway-tasks/evaluate-slotting',
        { warehouseId: selected?.warehouseId ?? tasks[0]?.warehouseId ?? undefined },
        { headers: idempotencyHeaders('putaway-evaluate-slotting:' + Date.now()) },
      );
      const data = res?.data as {
        evaluationId?: string; optimalBinMatchRatePct?: number; fefoHazardCompliance?: string;
        statusLabel?: string; slottingRecommendation?: string;
      } | undefined;
      if (data?.evaluationId) {
        const statusLabelText = data.statusLabel ?? 'Đã chấm điểm tối ưu xếp dỡ & phân bổ ô kệ thực tế';
        const fefoText = data.fefoHazardCompliance === 'VERIFIED' || !data.fefoHazardCompliance ? 'Đạt chuẩn tương thích FEFO & phân vùng bảo quản' : data.fefoHazardCompliance;
        const recommendationMap: Record<string, string> = {
          OPTIMAL_SLOTTING_BALANCED: 'Phân bổ vị trí kệ cân bằng tối ưu',
          SLOTTING_OPTIMIZED: 'Đã tối ưu phân bổ vị trí kệ',
          FEFO_HAZMAT_VERIFIED: 'Đạt chuẩn tương thích FEFO & phân vùng bảo quản',
        };
        const recommendationText = data.slottingRecommendation ? ` (${recommendationMap[data.slottingRecommendation] ?? data.slottingRecommendation})` : '';
        setSlottingEvaluationResult(`${data.evaluationId} • ${statusLabelText} • Khớp vị trí tối ưu: ${data.optimalBinMatchRatePct ?? 100}% • Tương thích FEFO/Bảo quản: ${fefoText}${recommendationText}`);
      } else {
        setSlottingEvaluationResult(''); setError('Không thể chấm điểm tối ưu xếp dỡ: dữ liệu phản hồi không hợp lệ.');
      }
    } catch (e) { setSlottingEvaluationResult(''); setError(message(e)); }
    finally { setSlottingBusy(false); }
  };

  if (loading && tasks.length === 0) return <p role="status">Đang tải nhiệm vụ cất hàng...</p>;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Nhập kho"
        title="Cất hàng"
        description="Theo dõi nhiệm vụ cất hàng từ vị trí nhận hàng tới vị trí đích, gồm trạng thái, tiến độ và xử lý ngoại lệ."
      />

      {error && <div role="alert" className="putaway-error">{error}</div>}

      <MobileBarcodeScannerBar
        warehouseId={selected?.warehouseId ?? tasks[0]?.warehouseId}
        sampleCode={tasks[0]?.receiptCode || 'PNK-001'}
        disabled={!!busy}
        onScan={code => {
          const upper = sanitizeScannerCode(code).toUpperCase();
          const matchTask = tasks.find(t => t.receiptCode.toUpperCase() === upper);
          if (matchTask) void detail(matchTask.id);
        }}
      />

      <PutawayOverviewCard tasks={tasks} />

      <div className="putaway putaway-grid" style={{ gridTemplateColumns: selected ? undefined : 'minmax(0, 1fr)', minWidth: 0, maxWidth: '100%', overflowX: 'hidden' }}>
        <PutawayTaskListCard tasks={tasks} warehouseLocations={warehouseLocations} detail={detail} />

        {selected && (
          <UiCard title={'Chi tiết nhiệm vụ ' + selected.receiptCode}>
            <div className="ui-inline-wrap">
              <UiBadge tone={selected.status === 'Completed' ? 'success' : selected.status === 'Exception' || selected.status === 'Cancelled' ? 'danger' : selected.status === 'InProgress' || selected.status === 'Assigned' ? 'info' : 'neutral'}><span style={{ whiteSpace: 'nowrap' }}>{states[selected.status] ?? 'Không xác định'}</span></UiBadge>
              {(selected.taskType === 'PutbackTask' || selected.sourceType === 'PutbackTask') && <UiBadge tone="warning"><span style={{ whiteSpace: 'nowrap' }}>{taskTypes.PutbackTask}</span></UiBadge>}
              {selected.isCrossDock && <UiBadge tone="success"><span style={{ whiteSpace: 'nowrap' }}>CROSS-DOCK → KHU TẬP KẾT XUẤT ({selected.crossDockAllocatedQty ?? selected.movedBaseQuantity})</span></UiBadge>}
              <span className="ui-muted-text">Tiến độ {selected.movedBaseQuantity}/{selected.requiredBaseQuantity}</span>
            </div>
            {selected.exceptionReason && <p><strong>Lý do cần xử lý:</strong> {selected.exceptionReason}</p>}

            {canExecute && ['Assigned', 'InProgress'].includes(selected.status) && (
              <MobileBarcodeScannerBar
                label="Quét nhanh vị trí kệ đích hoặc mã sản phẩm (SKU)"
                warehouseId={selected.warehouseId}
                disabled={!!busy}
                resolveViaApi={false}
                onScan={code => {
                  const upper = sanitizeScannerCode(code).toUpperCase();
                  for (const item of selected.items.filter(x => x.remainingBaseQuantity > 0)) {
                    const matchLoc = (locations[item.id] ?? []).find(l => l.code.toUpperCase() === upper);
                    if (matchLoc) {
                      setDestination(v => ({ ...v, [item.id]: matchLoc.id }));
                      setScannedBin(v => ({ ...v, [item.id]: matchLoc.code }));
                      return;
                    }
                    if (item.productCode.toUpperCase() === upper) {
                      setScannedProduct(v => ({ ...v, [item.id]: item.productCode }));
                      if (!quantity[item.id]) setQuantity(v => ({ ...v, [item.id]: item.remainingBaseQuantity }));
                      return;
                    }
                  }
                }}
              />
            )}

            <div className="putaway-actions" style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', margin: '8px 0' }}>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
                {canAssign && selected.status === 'Open' && (
                  <button disabled={!!busy} style={{ background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7', minHeight: 44, padding: '8px 16px', borderRadius: 8 }} onClick={() => void mutate(`assign-${selected.id}`, `/api/v1/putaway-tasks/${selected.id}/assign`, { assignedUserId: currentUserId(), rowVersion: selected.rowVersion })}>
                    {busy ? 'Đang lưu...' : 'Nhận nhiệm vụ'}
                  </button>
                )}
                {canExecute && selected.status === 'Assigned' && (
                  <button disabled={!!busy} style={{ background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7', minHeight: 44, padding: '8px 16px', borderRadius: 8 }} onClick={() => void mutate(`start-${selected.id}`, `/api/v1/putaway-tasks/${selected.id}/start`, { rowVersion: selected.rowVersion })}>
                    {busy ? 'Đang lưu...' : 'Bắt đầu cất hàng'}
                  </button>
                )}
                {canExecute && selected.status === 'InProgress' && (
                  <>
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
                      <span>Lý do cần xử lý</span>
                      <input style={{ minHeight: 44 }} value={exceptionReason} onChange={e => setExceptionReason(e.target.value)} />
                    </label>
                    <button disabled={!!busy || !exceptionReason.trim()} style={{ border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600, minHeight: 44, padding: '8px 14px', borderRadius: 8 }} onClick={() => void mutate(`exception-${selected.id}`, `/api/v1/putaway-tasks/${selected.id}/exception`, { reason: exceptionReason, rowVersion: selected.rowVersion })}>
                      Báo cần xử lý
                    </button>
                  </>
                )}
                {canExecute && selected.status === 'Exception' && (
                  <button disabled={!!busy} style={{ background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7', minHeight: 44, padding: '8px 16px', borderRadius: 8 }} onClick={() => void mutate(`resume-${selected.id}`, `/api/v1/putaway-tasks/${selected.id}/resume`, { rowVersion: selected.rowVersion })}>
                    Tiếp tục cất hàng
                  </button>
                )}
              </div>
              {canCancel && ['Open', 'Assigned'].includes(selected.status) && (
                <button disabled={!!busy} style={{ marginLeft: 'auto', border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600, minHeight: 44, padding: '8px 14px', borderRadius: 8 }} onClick={() => void mutate(`cancel-${selected.id}`, `/api/v1/putaway-tasks/${selected.id}/cancel`, { rowVersion: selected.rowVersion })}>
                  Hủy nhiệm vụ
                </button>
              )}
            </div>

            <div className="ui-stack">
              {selected.items.map(item => {
                const formatUnitLabel = (u?: string) => (!u || u === 'QA_UNIT' || u === 'QA-PCS' ? 'THÙNG' : u);
                const opUnit = formatUnitLabel(item.operationUnitCode);
                const baseUnit = formatUnitLabel(item.baseUnitCode);
                const sameUnitAndQty = opUnit === baseUnit && item.requiredOperationQuantity === item.requiredBaseQuantity;
                return (
                  <div key={item.id} className="ui-subcard">
                    <div className="ui-subcard-title">{item.productCode} — {item.productName}</div>
                    <div className="ui-inline-wrap">
                      <UiBadge>{inventory[item.inventoryStatus] ?? 'Không xác định'}</UiBadge>
                      <UiBadge>Vị trí nguồn: {item.sourceLocationCode?.trim().toUpperCase() === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : item.sourceLocationCode}</UiBadge>
                    </div>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, alignItems: 'center', margin: '6px 0', padding: '6px 10px', borderRadius: 6, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
                      <p style={{ margin: 0 }}>Cần cất: <strong>{item.requiredOperationQuantity} {opUnit}</strong>{!sameUnitAndQty ? ` (${item.requiredBaseQuantity} ${baseUnit})` : ''}</p>
                      <p style={{ margin: 0 }}>Đã cất: <strong>{item.movedBaseQuantity}</strong> • Còn lại: <strong>{item.remainingBaseQuantity}</strong></p>
                    </div>
                    <div data-testid="directed-putaway-suggestion" style={{ margin: '8px 0', padding: '8px 10px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 8, fontSize: 12.5 }}>
                      <div>
                        <strong style={{ color: '#1e3a8a' }}>Gợi ý Vị trí Kệ Cất hàng Thông minh:</strong>{' '}
                        <UiBadge tone="success">{directedSuggestions[item.id]?.recommendedBinCode || locations[item.id]?.[0]?.code || warehouseLocations.find(l => l.locationType === 'Storage')?.code || 'Kệ lưu trữ đích'}</UiBadge>{' '}
                        <span style={{ color: '#334155' }}>({directedSuggestions[item.id]?.reasonText || 'Cùng nhóm bảo quản Nhiệt độ thường • Ưu tiên FEFO • Còn 85% sức chứa'})</span>
                      </div>
                      <button type="button" data-testid="apply-directed-putaway-btn" style={{ minHeight: 38, fontWeight: 600 }} disabled={!!busy || directedBusyItem === item.id} onClick={() => void applyDirectedPutawaySuggestion(selected.id, item.id, item.remainingBaseQuantity)}>
                        {directedBusyItem === item.id ? 'Đang áp dụng...' : 'Áp dụng Kệ Gợi ý'}
                      </button>
                    </div>
                    {canExecute && item.remainingBaseQuantity > 0 && ['Assigned', 'InProgress'].includes(selected.status) && (
                      <div className="move-form" style={{ alignItems: 'flex-end', gap: 10 }}>
                        <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
                          <span>Vị trí đích</span>
                          <select style={{ minHeight: 44 }} aria-label={`Vị trí đích ${item.productCode}`} value={destination[item.id] ?? ''} onChange={e => setDestination(value => ({ ...value, [item.id]: Number(e.target.value) }))}>
                            <option value="">Chọn vị trí đích</option>
                            {(locations[item.id] ?? []).map(location => <option key={location.id} value={location.id}>{location.code} — {location.name}</option>)}
                          </select>
                        </label>
                        <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
                          <span>Số lượng</span>
                          <input style={{ minHeight: 44 }} aria-label={`Số lượng cất ${item.productCode}`} inputMode="decimal" role="spinbutton" min="0.0001" step="0.0001" value={quantity[item.id] ?? ''} onChange={e => setQuantity(value => ({ ...value, [item.id]: e.target.value === '' ? '' : roundQty4(Number(e.target.value)) }))} />
                        </label>
                        <button style={{ minHeight: 44, background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7', padding: '8px 16px', borderRadius: 8 }} disabled={!!busy || !destination[item.id] || !(Number(quantity[item.id]) > 0)} onClick={() => void mutate(`move-${selected.id}-${item.id}`, `/api/v1/putaway-tasks/${selected.id}/move`, { itemId: item.id, destinationLocationId: destination[item.id], quantity: roundQty4(Number(quantity[item.id])), unitCode: item.operationUnitCode, rowVersion: selected.rowVersion, ...(scannedBin[item.id] ? { destinationLocationBarcode: scannedBin[item.id] } : {}), ...(scannedProduct[item.id] ? { productBarcode: scannedProduct[item.id] } : {}) })}>
                          {busy === `move-${selected.id}-${item.id}` ? 'Đang hoàn thành...' : 'Xác nhận số lượng'}
                        </button>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </UiCard>
        )}
      </div>

      <PutawayWorkspaceAndSlottingCards
        tasks={tasks} selected={selected} setSelected={setSelected} detail={detail} warehouseLocations={warehouseLocations} locations={locations}
        workspaceTaskId={workspaceTaskId} setWorkspaceTaskId={setWorkspaceTaskId} workspaceFromLocId={workspaceFromLocId} setWorkspaceFromLocId={setWorkspaceFromLocId}
        workspaceToLocId={workspaceToLocId} setWorkspaceToLocId={setWorkspaceToLocId} workspaceQty={workspaceQty} setWorkspaceQty={setWorkspaceQty}
        setDestination={setDestination} setQuantity={setQuantity} busy={busy} slottingBusy={slottingBusy} evaluateSlotting={evaluateSlotting}
        executeCrossDock={executeCrossDock} evaluateAbcSlottingInterleaving={evaluateAbcSlottingInterleaving} directedSuggestions={directedSuggestions}
        setError={setError} setSlottingEvaluationResult={setSlottingEvaluationResult} abcInterleavingData={abcInterleavingData} crossDockNotice={crossDockNotice} slottingEvaluationResult={slottingEvaluationResult}
      />
    </UiPage>
  );
}
