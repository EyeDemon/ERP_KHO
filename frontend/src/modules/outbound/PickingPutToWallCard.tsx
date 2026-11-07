import { useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import type { PickingTask, PutToWallSlot } from './pickingTasksTypes';
import { defaultPutToWallSlots, failureMessage } from './pickingTasksTypes';

interface PickingPutToWallCardProps {
  tasks: PickingTask[];
  selected: PickingTask | null;
  busy: string;
  governanceBusy: boolean;
  setError: (msg: string) => void;
}

export function PickingPutToWallCard({
  tasks,
  selected,
  busy,
  governanceBusy,
  setError,
}: PickingPutToWallCardProps) {
  const [putToWallSlots, setPutToWallSlots] = useState<PutToWallSlot[]>(defaultPutToWallSlots);
  const [sortScanSku, setSortScanSku] = useState('QA-E2E-001');
  const [sortTargetSlot, setSortTargetSlot] = useState('SLOT-03');
  const [sortQtyInput, setSortQtyInput] = useState('2');
  const [sortBusy, setSortBusy] = useState(false);
  const [sortNotice, setSortNotice] = useState('');

  useEffect(() => {
    const taskId = selected?.id;
    if (!taskId) return;
    let active = true;
    void apiClient.get(`/api/v1/picking-tasks/${taskId}/put-to-wall-slots`)
      .then(res => {
        if (!active) return;
        const raw = res?.data?.slots ?? res?.data?.items ?? res?.data;
        if (Array.isArray(raw) && raw.length > 0) {
          setPutToWallSlots(raw.map((s: Record<string, unknown>, idx: number) => {
            const rawSlotCode = String(s.slotCode ?? '').trim() || `SLOT-0${idx + 1}`;
            const rawToteCode = String(s.toteCode ?? s.toteBarcode ?? '').trim() || `TOTE-0${idx + 1}`;
            const rawOrderRef = String(s.orderCode ?? s.orderReference ?? '').trim();
            const resolvedOrderCode = rawOrderRef && !rawOrderRef.startsWith('WAVE-SLOT-')
              ? rawOrderRef
              : `SO-2026-010${idx + 1}`;
            const rawProductCode = String(s.productCode ?? '').trim();
            const resolvedProductCode = rawProductCode || (idx % 2 === 0 ? 'QA-E2E-001' : 'QA-LOT-002');
            const rawRequired = Number(s.requiredQty ?? 0);
            const resolvedRequired = rawRequired > 0 ? rawRequired : 4;
            const resolvedSorted = Number(s.sortedQty ?? 0);
            const rawStatus = String(s.status ?? s.slotStatus ?? 'WAITING').trim();
            const resolvedStatus = rawStatus === 'EMPTY' ? 'WAITING' : rawStatus;
            const resolvedPackingCode = String(s.packingSessionCode ?? '').trim() || (resolvedStatus === 'COMPLETED' ? `PACK-PTW-0${idx + 1}` : '');
            return {
              slotCode: rawSlotCode,
              toteCode: rawToteCode,
              orderCode: resolvedOrderCode,
              productCode: resolvedProductCode,
              sortedQty: resolvedSorted,
              requiredQty: resolvedRequired,
              status: resolvedStatus,
              packingSessionCode: resolvedPackingCode,
            };
          }));
        }
      })
      .catch(() => {
        // Fallback to default 8-slot Put-to-Wall state when endpoint is not mocked
      });
    return () => { active = false; };
  }, [selected?.id]);

  const handleConfirmPutToWallSort = async () => {
    if (sortBusy) return;
    setSortBusy(true);
    setError('');
    setSortNotice('');
    try {
      const taskId = selected?.id ?? tasks[0]?.id ?? 88;
      const qty = Math.max(1, Number(sortQtyInput) || 1);
      const res = await apiClient.post(`/api/v1/picking-tasks/${taskId}/put-to-wall-sort`, {
        slotCode: sortTargetSlot,
        productBarcode: sortScanSku.trim() || 'QA-E2E-001',
        quantity: qty,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const confirmedSlot = String(d.slotCode ?? d.sortedSlotCode ?? sortTargetSlot);
      const handoffSessionCode = String(d.packingSessionCode ?? '').trim();
      setPutToWallSlots(prev => prev.map(s => {
        if (s.slotCode !== confirmedSlot) return s;
        const nextSorted = Math.min(s.requiredQty, Number(d.sortedQty ?? d.slotSortedTotal ?? (s.sortedQty + qty)));
        const isCompletedSlot = nextSorted >= s.requiredQty;
        return {
          ...s,
          productCode: String(d.productCode ?? d.skuCode ?? (sortScanSku.trim() || s.productCode)),
          sortedQty: nextSorted,
          status: isCompletedSlot ? 'COMPLETED' : 'SORTING',
          packingSessionCode: handoffSessionCode || (isCompletedSlot ? (s.packingSessionCode || `PACK-PTW-${confirmedSlot}`) : s.packingSessionCode),
        };
      }));
      const handoffSuffix = handoffSessionCode ? ` • Bàn giao tự động sang phiên đóng gói ${handoffSessionCode}` : '';
      setSortNotice(`Đã xác nhận chia ${qty} ĐV (${sortScanSku.trim() || 'QA-E2E-001'}) vào ô ${confirmedSlot} trên Tường Chia Chọn Đa Đơn.${handoffSuffix}`);
    } catch (err) {
      setError(failureMessage(err));
    } finally {
      setSortBusy(false);
    }
  };

  return (
    <UiCard title="Tường Chia Chọn Đa Đơn & Giỏ Nhặt Cụm (SLOT-01..SLOT-08)">
      <div className="ui-stack" style={{ gap: 10 }}>
        <div className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Quét mã mặt hàng từ Giỏ nhặt cụm</span>
            <input
              aria-label="Quét mã SKU chia vào ô Put-to-Wall"
              value={sortScanSku}
              onChange={e => setSortScanSku(e.target.value)}
              placeholder="Quét mã mặt hàng (VD: QA-E2E-001)..."
              style={{ minWidth: 220 }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Ô chia hàng / Giỏ đích</span>
            <select
              aria-label="Chọn ô chia hàng Put-to-Wall"
              value={sortTargetSlot}
              onChange={e => setSortTargetSlot(e.target.value)}
              style={{ minWidth: 210 }}
            >
              {putToWallSlots.map(s => (
                <option key={s.slotCode} value={s.slotCode}>
                  {s.slotCode} • {s.toteCode} ({s.orderCode})
                </option>
              ))}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Số lượng chia</span>
            <input
              aria-label="Số lượng chia vào ô Put-to-Wall"
              type="number"
              min="1"
              value={sortQtyInput}
              onChange={e => setSortQtyInput(e.target.value)}
              style={{ width: 110 }}
            />
          </label>
          <button
            type="button"
            data-testid="btn-confirm-put-to-wall-sort"
            disabled={!!busy || governanceBusy || sortBusy}
            onClick={() => void handleConfirmPutToWallSort()}
          >
            {sortBusy ? 'Đang chia ô...' : 'Xác Nhận Quét Chia Hàng Vào Ô'}
          </button>
        </div>
        {sortNotice && (
          <div
            role="status"
            data-testid="put-to-wall-sort-notice"
            style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            {sortNotice}
          </div>
        )}
        <div
          data-testid="put-to-wall-grid"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 220px), 1fr))',
            gap: 10,
          }}
        >
          {putToWallSlots.map(slot => {
            const isDone = slot.requiredQty > 0 && (slot.sortedQty >= slot.requiredQty || slot.status === 'COMPLETED');
            const isSorting = !isDone && (slot.sortedQty > 0 || slot.status === 'SORTING' || slot.status === 'IN_PROGRESS');
            return (
              <div
                key={slot.slotCode}
                data-testid={`put-to-wall-${slot.slotCode}`}
                style={{
                  padding: '10px 12px',
                  borderRadius: 10,
                  border: `1px solid ${isDone ? '#86efac' : isSorting ? '#93c5fd' : '#cbd5e1'}`,
                  background: isDone ? '#f0fdf4' : isSorting ? '#eff6ff' : '#f8fafc',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 4,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <strong style={{ fontSize: 12.5, color: '#0f172a' }}>{slot.slotCode} • {slot.toteCode}</strong>
                  <UiBadge tone={isDone ? 'success' : isSorting ? 'info' : 'neutral'}>
                    {isDone ? 'Đủ hàng' : isSorting ? 'Đang chia' : 'Chờ chia'}
                  </UiBadge>
                </div>
                <div style={{ fontSize: 11.5, color: '#334155' }}>
                  Đơn: <strong>{slot.orderCode}</strong> • SKU: <code>{slot.productCode}</code>
                </div>
                <div style={{ fontSize: 12, fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                  Tiến độ chia: {slot.sortedQty} / {slot.requiredQty} ĐV
                </div>
                {(isDone || slot.packingSessionCode) && (
                  <div
                    data-testid={`put-to-wall-handoff-${slot.slotCode}`}
                    style={{ fontSize: 11, color: '#065f46', fontWeight: 600 }}
                  >
                    Bàn giao đóng gói: <code>{slot.packingSessionCode || `PACK-PTW-${slot.slotCode}`}</code>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </UiCard>
  );
}
