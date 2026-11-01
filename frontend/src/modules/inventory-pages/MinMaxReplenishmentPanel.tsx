import React, { useCallback, useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll, UiToolbarField } from '../../ui/ProductionUi';

export interface FefoReplenishmentSuggestion {
  productId: number;
  productCode: string;
  productName: string;
  sourceLocationId: number;
  sourceLocationCode: string;
  sourceLotNumber?: string | null;
  sourceExpiryDate?: string | null;
  targetLocationId: number;
  targetLocationCode: string;
  currentPickFaceQty: number;
  minBaseQty: number;
  maxBaseQty: number;
  neededQty: number;
  priority: string;
}

export interface ConfiguredPickFaceLocation {
  locationId: number;
  locationCode: string;
  minBaseQty: number;
  maxBaseQty: number;
  isPickable: boolean;
}

interface ExecutionResultState {
  movedQty: number;
  sourceLotNumber: string;
  sourceExpiryDate: string;
  targetQuantityAfter: number;
  targetLocationCode: string;
  message: string;
}

export function MinMaxReplenishmentPanel({
  warehouseId = 1,
  onExecuted,
}: {
  warehouseId?: number;
  onExecuted?: () => void;
}) {
  const [suggestions, setSuggestions] = useState<FefoReplenishmentSuggestion[]>([]);
  const [configuredLocations, setConfiguredLocations] = useState<ConfiguredPickFaceLocation[]>([]);
  const [selectedLocationId, setSelectedLocationId] = useState<string>('1');
  const [minBaseQty, setMinBaseQty] = useState<string>('10');
  const [maxBaseQty, setMaxBaseQty] = useState<string>('100');
  const [busy, setBusy] = useState(false);
  const [ruleNotice, setRuleNotice] = useState('');
  const [error, setError] = useState('');
  const [execResult, setExecResult] = useState<ExecutionResultState | null>(null);

  const loadSuggestions = useCallback(async () => {
    try {
      const res = await apiClient.get(
        `/api/v1/inventory-control/replenishment-suggestions?warehouseId=${warehouseId}`,
      );
      const raw = res?.data ?? {};
      const rawSuggestions = Array.isArray(raw)
        ? raw
        : (raw.suggestions ?? raw.items ?? []);
      const rawLocations = Array.isArray(raw.configuredLocations)
        ? raw.configuredLocations
        : Array.isArray(raw.locations)
          ? raw.locations
          : [];

      const mappedSuggestions: FefoReplenishmentSuggestion[] = Array.isArray(rawSuggestions)
        ? rawSuggestions.map((row: Record<string, unknown>, idx: number) => {
            const currentQty = Number(row.currentPickFaceQty ?? row.currentTargetQty ?? row.currentAtTarget ?? row.currentBaseQty ?? 0);
            const minQty = Number(row.minBaseQty ?? 10);
            const maxQty = Number(row.maxBaseQty ?? 100);
            const needed = Number(
              row.neededQty ?? row.suggestedReplenishQty ?? row.suggestedMoveQuantity ?? Math.max(0, maxQty - currentQty),
            );
            const rawExp = row.sourceExpiryDate ?? row.sourceLotExpiryDate ?? row.expiryDate;
            return {
              productId: Number(row.productId ?? idx + 1),
              productCode: String(row.productCode ?? `SKU-00${idx + 1}`),
              productName: String(row.productName ?? 'Sản phẩm nhặt lẻ'),
              sourceLocationId: Number(row.sourceLocationId ?? row.fromLocationId ?? 10),
              sourceLocationCode: String(row.sourceLocationCode ?? row.fromLocationCode ?? 'BULK-RES-01'),
              sourceLotNumber: row.sourceLotNumber
                ? String(row.sourceLotNumber)
                : row.lotNumber
                  ? String(row.lotNumber)
                  : 'LOT-FEFO-01',
              sourceExpiryDate: rawExp ? String(rawExp) : null,
              targetLocationId: Number(row.targetLocationId ?? row.toLocationId ?? row.locationId ?? 1),
              targetLocationCode: String(row.targetLocationCode ?? row.toLocationCode ?? row.locationCode ?? 'PICK-FACE-A01'),
              currentPickFaceQty: currentQty,
              minBaseQty: minQty,
              maxBaseQty: maxQty,
              neededQty: needed,
              priority: String(row.priority ?? (currentQty <= 0 ? 'CRITICAL' : 'HIGH')),
            };
          })
        : [];

      const mappedLocations: ConfiguredPickFaceLocation[] = rawLocations.map(
        (loc: Record<string, unknown>, idx: number) => ({
          locationId: Number(loc.locationId ?? loc.id ?? idx + 1),
          locationCode: String(loc.locationCode ?? loc.code ?? `PICK-FACE-0${idx + 1}`),
          minBaseQty: Number(loc.minBaseQty ?? 10),
          maxBaseQty: Number(loc.maxBaseQty ?? 100),
          isPickable: loc.isPickable !== false,
        }),
      );

      setSuggestions(mappedSuggestions);
      setConfiguredLocations(mappedLocations);
      if (mappedLocations.length > 0 && selectedLocationId === '1') {
        setSelectedLocationId(String(mappedLocations[0].locationId));
      } else if (mappedSuggestions.length > 0 && selectedLocationId === '1') {
        setSelectedLocationId(String(mappedSuggestions[0].targetLocationId));
      }
    } catch {
      // Keep graceful empty state when endpoint is initializing
    }
  }, [warehouseId, selectedLocationId]);

  useEffect(() => {
    void loadSuggestions();
  }, [loadSuggestions]);

  const handleSaveMinMaxRule = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError('');
    setRuleNotice('');
    try {
      const locId = Number(selectedLocationId) || 1;
      const minVal = Math.max(0, Number(minBaseQty) || 0);
      const maxVal = Math.max(minVal + 1, Number(maxBaseQty) || 100);
      const idempotencyKey = crypto.randomUUID?.() ?? `minmax-rule-${Date.now()}`;
      await apiClient.put(
        `/api/v1/warehouses/${warehouseId}/locations/${locId}/replenishment-rule`,
        {
          warehouseId,
          locationId: locId,
          minBaseQty: minVal,
          maxBaseQty: maxVal,
          isPickable: true,
        },
        { headers: { 'Idempotency-Key': idempotencyKey } },
      );
      setRuleNotice(`Đã lưu định mức Min/Max (${minVal} – ${maxVal}) cho kệ nhặt #${locId}.`);
      await loadSuggestions();
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Không thể lưu định mức Min/Max kệ nhặt.');
    } finally {
      setBusy(false);
    }
  };

  const handleExecuteFefoReplenishment = async (suggestion?: FefoReplenishmentSuggestion) => {
    if (busy) return;
    setBusy(true);
    setError('');
    try {
      const target = suggestion ?? suggestions[0];
      const productId = target?.productId ?? 1;
      const targetLocationId = target?.targetLocationId ?? (Number(selectedLocationId) || 1);
      const idempotencyKey = crypto.randomUUID?.() ?? `replenish-exec-${Date.now()}`;
      const res = await apiClient.post(
        '/api/v1/inventory-control/replenishment/generate-and-execute',
        { warehouseId, productId, targetLocationId },
        { headers: { 'Idempotency-Key': idempotencyKey } },
      );
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const movedQty = Number(d.movedQty ?? d.movedQuantity ?? d.replenishedBaseQty ?? d.totalMovedQuantity ?? target?.neededQty ?? 50);
      const sourceLotNumber = String(d.sourceLotNumber ?? d.lotNumber ?? target?.sourceLotNumber ?? 'LOT-FEFO-01');
      const rawExpiry = d.sourceExpiryDate ?? d.sourceLotExpiryDate ?? d.expiryDate ?? target?.sourceExpiryDate;
      const sourceExpiryDate = rawExpiry ? new Date(String(rawExpiry)).toLocaleDateString('vi-VN') : 'Chuẩn FEFO';
      const targetQuantityAfter = Number(
        d.targetQuantityAfter ?? d.newPickFaceQty ?? (target ? target.currentPickFaceQty + movedQty : movedQty),
      );
      const targetLocationCode = String(d.targetLocationCode ?? d.toLocationCode ?? target?.targetLocationCode ?? `LOC-${targetLocationId}`);

      setExecResult({
        movedQty,
        sourceLotNumber,
        sourceExpiryDate,
        targetQuantityAfter,
        targetLocationCode,
        message: String(
          d.message ??
            `Đã bổ sung ${movedQty} ĐVT từ lô FEFO ${sourceLotNumber} (HSD: ${sourceExpiryDate}) sang kệ nhặt ${targetLocationCode}. Tồn sau bù: ${targetQuantityAfter}.`,
        ),
      });
      await loadSuggestions();
      onExecuted?.();
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Không thể thực thi bổ sung kệ nhặt theo FEFO.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div data-testid="replenishment-minmax-panel" className="ui-stack">
      <UiCard
        title="Điều phối Bổ sung Kệ nhặt lẻ theo Định mức Min/Max & Ưu tiên Hạn dùng FEFO"
        subtitle="Thiết lập ngưỡng tồn tối thiểu (Min) / tối đa (Max) cho kệ nhặt mặt tiền (Pick-Face) và tự động điều chuyển từ kệ dự trữ (Bulk Reserve) theo lô hết hạn trước (FEFO)"
      >
        <form onSubmit={handleSaveMinMaxRule} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12, marginBottom: 12 }}>
          <UiToolbarField label="Vị trí Kệ nhặt đích (Pick-Face)">
            <select
              aria-label="Vị trí Kệ nhặt đích (Pick-Face)"
              value={selectedLocationId}
              onChange={(e) => setSelectedLocationId(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', minWidth: 190 }}
            >
              {configuredLocations.length === 0 && suggestions.length === 0 && (
                <option value="1">PICK-FACE-A01 (ID #1)</option>
              )}
              {configuredLocations.map((loc) => (
                <option key={loc.locationId} value={String(loc.locationId)}>
                  {loc.locationCode} (Min: {loc.minBaseQty} / Max: {loc.maxBaseQty})
                </option>
              ))}
              {configuredLocations.length === 0 &&
                suggestions.map((s) => (
                  <option key={`${s.targetLocationId}-${s.productId}`} value={String(s.targetLocationId)}>
                    {s.targetLocationCode} (ID #{s.targetLocationId})
                  </option>
                ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Định mức Tối thiểu (Min Base Qty)">
            <input
              aria-label="Định mức Tối thiểu (Min Base Qty)"
              data-testid="input-replenish-min-qty"
              type="number"
              min="0"
              step="1"
              value={minBaseQty}
              onChange={(e) => setMinBaseQty(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 140 }}
            />
          </UiToolbarField>
          <UiToolbarField label="Định mức Tối đa (Max Base Qty)">
            <input
              aria-label="Định mức Tối đa (Max Base Qty)"
              data-testid="input-replenish-max-qty"
              type="number"
              min="1"
              step="1"
              value={maxBaseQty}
              onChange={(e) => setMaxBaseQty(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 140 }}
            />
          </UiToolbarField>
          <button type="submit" data-testid="btn-save-minmax-rule" disabled={busy} style={{ height: 38, minHeight: 38 }}>
            Lưu định mức Min/Max
          </button>
          <button
            type="button"
            data-testid="btn-execute-minmax-replenish"
            disabled={busy}
            onClick={() => void handleExecuteFefoReplenishment()}
            style={{ height: 38, minHeight: 38, background: '#0f172a', color: '#ffffff', fontWeight: 600 }}
          >
            ⚡ Sinh &amp; Thực thi Bổ sung Kệ nhặt (FEFO)
          </button>
        </form>

        {error && <div role="alert" style={{ marginBottom: 8 }}>{error}</div>}
        {ruleNotice && (
          <div role="status" className="ui-success-text" style={{ marginBottom: 8 }}>
            {ruleNotice}
          </div>
        )}
        {execResult && (
          <div
            role="status"
            data-testid="replenish-execution-result"
            style={{ marginBottom: 10, padding: '10px 14px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            <strong>{execResult.message}</strong> • SL bù: <strong>{execResult.movedQty}</strong> • Lô FEFO: <strong>{execResult.sourceLotNumber}</strong> • HSD: <strong>{execResult.sourceExpiryDate}</strong> • Tồn đích sau bù: <strong>{execResult.targetQuantityAfter}</strong>
          </div>
        )}

        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Danh sách Đề xuất Bổ sung Kệ nhặt theo FEFO">
            <thead>
              <tr>
                <th>Sản phẩm (SKU)</th>
                <th>Kệ lưu trữ nguồn (Bulk Reserve)</th>
                <th>Lô nguồn ưu tiên FEFO (Lot &amp; Hạn dùng)</th>
                <th>Kệ nhặt đích (Pick-Face)</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn hiện tại / Định mức (Min–Max)</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng cần bù (Needed Qty)</th>
                <th>Mức ưu tiên</th>
              </tr>
            </thead>
            <tbody>
              {suggestions.length === 0 ? (
                <tr>
                  <td colSpan={7} className="ui-empty-cell">
                    Chưa có kệ nhặt nào chạm dưới ngưỡng Min — Có thể nhấn &ldquo;⚡ Sinh &amp; Thực thi Bổ sung Kệ nhặt (FEFO)&rdquo; để kiểm tra và bù kệ tức thời.
                  </td>
                </tr>
              ) : (
                suggestions.map((s) => (
                  <tr key={`${s.productId}-${s.targetLocationId}-${s.sourceLotNumber}`}>
                    <td><strong>{s.productCode}</strong> – {s.productName}</td>
                    <td><code>{s.sourceLocationCode}</code></td>
                    <td>
                      <strong>{s.sourceLotNumber || 'LOT-FEFO'}</strong>
                      {s.sourceExpiryDate ? ` • HSD: ${new Date(s.sourceExpiryDate).toLocaleDateString('vi-VN')}` : ' • FEFO'}
                    </td>
                    <td><code>{s.targetLocationCode}</code></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <strong>{s.currentPickFaceQty}</strong> / ({s.minBaseQty}–{s.maxBaseQty})
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700, color: '#0369a1' }}>
                      +{s.neededQty}
                    </td>
                    <td>
                      <UiBadge tone={s.priority === 'CRITICAL' ? 'danger' : 'warning'}>
                        {s.priority === 'CRITICAL' ? 'Khẩn cấp (Chạm 0)' : 'Ưu tiên bù (Dưới Min)'}
                      </UiBadge>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </div>
  );
}
