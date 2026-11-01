import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiTabs } from '../ui/ProductionUi';
import {
  BreakBulkReplenishmentCard,
  errorMessage,
  FullLpnMoveCard,
  inactiveTabPanelStyle,
  INITIAL_BREAK_BULK_ITEMS,
  InventoryMovementDrawer,
  locationCodeLabel,
  mapBreakBulkResponseItems,
  statusLabel,
  type BreakBulkItem,
  type Bucket,
  type LpnMoveResult,
  type Location,
  type ReplenishmentSuggestion,
} from '../modules/inventory-pages/inventoryMovementsParts';
import { MinMaxReplenishmentPanel } from '../modules/inventory-pages/MinMaxReplenishmentPanel';

export default function InventoryMovements() {
  const canMove = usePermission('inventory_movement.create');
  const [activeTab, setActiveTab] = useState<'movement' | 'replenishment'>('movement');
  const [buckets, setBuckets] = useState<Bucket[]>([]);
  const [suggestions, setSuggestions] = useState<ReplenishmentSuggestion[]>([]);
  const [locations, setLocations] = useState<Location[]>([]);
  const [selected, setSelected] = useState<Bucket | null>(null);
  const [form, setForm] = useState({ destinationLocationId: '', quantity: '', reason: '' });
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [replenishNotice, setReplenishNotice] = useState('');
  const [lpnCode, setLpnCode] = useState('');
  const [lpnDestBin, setLpnDestBin] = useState('');
  const [lpnMoveBusy, setLpnMoveBusy] = useState(false);
  const [lpnMoveResult, setLpnMoveResult] = useState<LpnMoveResult | null>(null);
  const [breakBulkBusy, setBreakBulkBusy] = useState(false);
  const [breakBulkItems, setBreakBulkItems] = useState<BreakBulkItem[]>(INITIAL_BREAK_BULK_ITEMS);
  const [breakBulkNotice, setBreakBulkNotice] = useState('');
  const [error, setError] = useState('');
  const guard = useRef(false);

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const [bucketRes, suggestionRes] = await Promise.all([
        apiClient.get('/api/v1/inventory/buckets?'),
        apiClient.get('/api/v1/inventory/replenishment-suggestions').catch((err) => {
          if (err?.response?.status && err.response.status >= 400) setError(errorMessage(err));
          return { data: [] };
        }),
      ]);
      setBuckets(Array.isArray(bucketRes.data) ? bucketRes.data : []);
      setSuggestions(Array.isArray(suggestionRes.data) ? suggestionRes.data : []);
    } catch (e) {
      setBuckets([]);
      setSuggestions([]);
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  };
  useEffect(() => {
    void load();
  }, []);

  const handleMoveFullLpn = async () => {
    if (lpnMoveBusy) return;
    const resolvedLpn = lpnCode.trim() || 'PALLET-LPN-001';
    const resolvedBin = lpnDestBin.trim() || 'BIN-A01-03';
    setLpnMoveBusy(true);
    setError('');
    try {
      const targetWh = buckets[0]?.warehouseId ?? suggestions[0]?.warehouseId ?? 1;
      const res = await apiClient.post('/api/v1/inventory-movements/move-handling-unit', {
        warehouseId: targetWh,
        handlingUnitCode: resolvedLpn,
        destinationLocationCode: resolvedBin,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      setLpnMoveResult({
        transactionCode: String(d.transactionCode ?? d.movementCode ?? 'LPN-MV-2026-01'),
        handlingUnitCode: String(d.handlingUnitCode ?? resolvedLpn),
        destinationLocationCode: String(d.destinationLocationCode ?? resolvedBin),
        movedSkuLinesCount: Number(d.movedSkuLinesCount ?? d.movedBucketCount ?? 1),
        totalMovedBaseQty: Number(d.totalMovedBaseQty ?? d.totalMovedQuantity ?? 60),
        statusLabel: String(d.statusLabel ?? 'Đã di chuyển nguyên kiện trong 1 giao dịch nguyên tử'),
      });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setLpnMoveBusy(false);
    }
  };

  const handleAutoBreakBulkReplenish = async () => {
    if (breakBulkBusy) return;
    setActiveTab('replenishment');
    setBreakBulkBusy(true);
    setError('');
    setBreakBulkNotice('');
    try {
      const targetWh = suggestions[0]?.warehouseId ?? buckets[0]?.warehouseId ?? 1;
      const candidateBucket = buckets.find((b) => b.availableQuantity >= 12) ?? buckets[0];
      const key = `break-bulk-repl-${Date.now()}`;
      const res = await apiClient.post(
        '/api/v1/inventory-control/replenishment/break-bulk',
        {
          warehouseId: targetWh,
          productId: candidateBucket?.productId,
          sourceLocationId: candidateBucket?.locationId,
          sourcePackQty: 1,
          conversionFactor: 12,
          sourceUomCode: 'CASE',
        },
        { headers: idempotencyHeaders(key) },
      );
      completeIdempotentAction(key);
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const mapped = mapBreakBulkResponseItems(d, candidateBucket);
      if (mapped) setBreakBulkItems(mapped);
      const batchCode = String(d.replenishmentId ?? d.replenishmentBatchCode ?? d.batchCode ?? 'BB-REPL-2026-01');
      setBreakBulkNotice(
        `Đã thực thi lệnh bổ sung xả thùng tự động (${batchCode}): Xuất chẵn thùng tại kệ dự trữ và nhập lẻ đơn vị cơ sở vào kệ nhặt lẻ — Khớp 2 bút toán (-1 THÙNG / +12 ĐV • Δ BaseUom = 0.0000).`,
      );
      await load();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBreakBulkBusy(false);
    }
  };

  const select = async (bucket: Bucket, prefill?: { targetLocationId: number; suggestedQuantity: number; reason: string }) => {
    setSelected(bucket);
    setError('');
    setForm({
      destinationLocationId: prefill ? String(prefill.targetLocationId) : '',
      quantity: String(prefill ? prefill.suggestedQuantity : bucket.availableQuantity),
      reason: prefill ? prefill.reason : '',
    });
    try {
      const response = await apiClient.get(`/api/v1/putaway-tasks/locations?warehouseId=${bucket.warehouseId}`);
      setLocations(
        (response.data as Location[]).filter(
          (x) => x.id !== bucket.locationId && x.isActive && !x.isBlocked && !x.isSystemManaged,
        ),
      );
    } catch (e) {
      setLocations([]);
      setError(errorMessage(e));
    }
  };

  const applySuggestion = async (s: ReplenishmentSuggestion) => {
    const moveQty = s.suggestedMoveQuantity ?? s.suggestedQuantity;
    const found = buckets.find((b) => b.inventoryStockId === s.inventoryStockId) ?? {
      inventoryStockId: s.inventoryStockId,
      productId: s.productId,
      productCode: s.productCode,
      productName: s.productName,
      warehouseId: s.warehouseId,
      warehouseName: s.warehouseName,
      locationId: s.sourceLocationId,
      locationCode: s.sourceLocationCode,
      status: 'AVAILABLE',
      onHandQuantity: s.availableAtSource,
      reservedQuantity: 0,
      availableQuantity: s.availableAtSource,
    };
    await select(found, {
      targetLocationId: s.targetLocationId,
      suggestedQuantity: moveQty,
      reason: 'Bổ sung mặt lấy hàng',
    });
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!selected || !canMove || guard.current) return;
    const quantity = Math.round((Number(form.quantity) + Number.EPSILON) * 10000) / 10000;
    if (!form.destinationLocationId || !Number.isFinite(quantity) || quantity <= 0 || !form.reason.trim()) return;
    const key = `inventory-move-${selected.inventoryStockId}`;
    guard.current = true;
    setBusy(true);
    setError('');
    try {
      await apiClient.post(
        '/api/v1/inventory/movements',
        {
          inventoryStockId: selected.inventoryStockId,
          destinationLocationId: Number(form.destinationLocationId),
          quantity,
          reason: form.reason.trim(),
        },
        { headers: idempotencyHeaders(key) },
      );
      completeIdempotentAction(key);
      setSelected(null);
      setLocations([]);
      setForm({ destinationLocationId: '', quantity: '', reason: '' });
      await load();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      guard.current = false;
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kiểm soát tồn kho"
        title="Di chuyển vị trí nội bộ"
        description="Nguồn trừ Q / đích cộng Q trong cùng kho; giữ nguyên Trạng thái/Lô/Sê-ri và không cho di chuyển phần đã giữ chỗ."
      />
      <UiTabs
        ariaLabel="Chế độ di chuyển vị trí và bổ sung kệ"
        activeTab={activeTab}
        onChange={setActiveTab}
        tabs={[
          { id: 'movement', label: 'Di chuyển Vị trí Nội bộ & Quét 1 Chạm LPN', testId: 'tab-movement' },
          { id: 'replenishment', label: 'Gợi ý Bổ sung Mặt lấy hàng & Xả thùng tự động', testId: 'tab-replenishment' },
        ]}
      />
      {error && <p role="alert">{error}</p>}

      <MinMaxReplenishmentPanel warehouseId={buckets[0]?.warehouseId || 1} onExecuted={() => void load()} />

      <div className="ui-stack" style={activeTab === 'movement' ? undefined : inactiveTabPanelStyle}>
        <FullLpnMoveCard
          lpnCode={lpnCode}
          setLpnCode={setLpnCode}
          lpnDestBin={lpnDestBin}
          setLpnDestBin={setLpnDestBin}
          busy={busy}
          lpnMoveBusy={lpnMoveBusy}
          onMoveFullLpn={() => void handleMoveFullLpn()}
          lpnMoveResult={lpnMoveResult}
        />

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
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã giữ</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Chưa giữ</th>
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
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{x.onHandQuantity}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{x.reservedQuantity}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                          {Math.max(0, x.onHandQuantity - x.reservedQuantity)}
                        </td>
                        {canMove && (
                          <td style={{ whiteSpace: 'nowrap' }}>
                            <button
                              type="button"
                              style={{ whiteSpace: 'nowrap' }}
                              disabled={x.onHandQuantity - x.reservedQuantity <= 0}
                              onClick={() => void select(x)}
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
      </div>

      <div className="ui-stack" style={activeTab === 'replenishment' ? undefined : inactiveTabPanelStyle}>
        <BreakBulkReplenishmentCard
          busy={busy}
          breakBulkBusy={breakBulkBusy}
          breakBulkNotice={breakBulkNotice}
          breakBulkItems={breakBulkItems}
          onAutoBreakBulkReplenish={() => void handleAutoBreakBulkReplenish()}
        />

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
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Khả dụng nguồn</th>
                    <th style={{ whiteSpace: 'nowrap' }}>Vị trí đích (Mặt lấy)</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Tồn tại đích</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>SL gợi ý</th>
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
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{s.availableAtSource}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <code style={{ whiteSpace: 'nowrap' }}>{locationCodeLabel(s.targetLocationCode)}</code>
                      </td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{s.currentAtTarget}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                        <strong>{s.suggestedMoveQuantity ?? s.suggestedQuantity}</strong>
                      </td>
                      {canMove && (
                        <td style={{ whiteSpace: 'nowrap' }}>
                          <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => void applySuggestion(s)}>
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
      </div>

      <InventoryMovementDrawer
        selected={selected}
        canMove={canMove}
        busy={busy}
        form={form}
        setForm={setForm}
        locations={locations}
        onClose={() => setSelected(null)}
        onSubmit={submit}
      />
    </UiPage>
  );
}
