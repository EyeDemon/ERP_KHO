import React, { useEffect, useState } from 'react';
import { MoreHorizontal } from 'lucide-react';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL } from '../../utils/warehouseLabel';
import { KitWorkOrderReversalModal } from './KitWorkOrderReversalModal';
import { DEFAULT_KIT_ORDERS, type KitWorkOrderRow } from './packingSessionsTypes';

export { DEFAULT_KIT_ORDERS, type KitWorkOrderRow };

interface PackingKittingBomSectionProps {
  kitOrders: KitWorkOrderRow[];
  kittingVasMode: 'PRE_KIT' | 'DEKIT_DISASSEMBLE';
  setKittingVasMode: (mode: 'PRE_KIT' | 'DEKIT_DISASSEMBLE') => void;
  kitPlannedQty: number;
  setKitPlannedQty: (qty: number) => void;
  kitTargetBin: string;
  setKitTargetBin: (bin: string) => void;
  kitEarliestExpiry: string;
  kitRolledUpCost: number | null;
  canReadCost: boolean;
  busy: string;
  governanceBusy: boolean;
  kittingVasResult: string;
  onExecuteKittingVas: (overrideMode?: 'PRE_KIT' | 'DEKIT_DISASSEMBLE') => Promise<void>;
}

const COMPACT_TH: React.CSSProperties = { padding: '6px 8px', fontSize: 12, whiteSpace: 'nowrap' };
const COMPACT_TD: React.CSSProperties = { padding: '6px 8px', fontSize: 12.5, verticalAlign: 'middle' };

export const PackingKittingBomSection: React.FC<PackingKittingBomSectionProps> = ({
  kitOrders, kittingVasMode, setKittingVasMode, kitPlannedQty, setKitPlannedQty, kitTargetBin,
  setKitTargetBin, kitEarliestExpiry, kitRolledUpCost, canReadCost, busy, governanceBusy,
  kittingVasResult, onExecuteKittingVas,
}) => {
  const [showBomEditor, setShowBomEditor] = useState(false);
  const [reversalTargetOrder, setReversalTargetOrder] = useState<KitWorkOrderRow | null>(null);
  const [reversalReason, setReversalReason] = useState('');
  const [openMenuOrderCode, setOpenMenuOrderCode] = useState<string | null>(null);

  useEffect(() => {
    if (!openMenuOrderCode) return undefined;
    const closeMenu = (ev: MouseEvent) => {
      const target = ev.target as HTMLElement | null;
      if (target?.closest('[data-kit-action-menu-wrap="true"]')) return;
      setOpenMenuOrderCode(null);
    };
    document.addEventListener('mousedown', closeMenu);
    return () => document.removeEventListener('mousedown', closeMenu);
  }, [openMenuOrderCode]);

  const openOrdersCount = kitOrders.filter((w) => w.status !== 'COMPLETED').length;
  const completedOrdersCount = kitOrders.filter((w) => w.status === 'COMPLETED').length;
  const effectiveKitCost = kitRolledUpCost ?? 500000;

  const handleConfirmReversal = async (e: React.FormEvent) => {
    e.preventDefault();
    if (reversalReason.trim().length < 10) return;
    await onExecuteKittingVas('DEKIT_DISASSEMBLE');
    setReversalTargetOrder(null);
    setReversalReason('');
  };

  return (
    <div data-testid="kit-work-orders-workbench" className="ui-stack">
      <UiMetricGrid>
        <UiMetric value={`${openOrdersCount} lệnh`} label="Lệnh lắp ráp đang mở" isZero={openOrdersCount === 0} />
        <UiMetric value={`${completedOrdersCount} lệnh (20 bộ)`} label="Bộ Kit đã hoàn thành" isZero={completedOrdersCount === 0} />
        <UiMetric value="2 linh kiện BOM" label="Định mức BOM đang áp dụng" />
        <UiMetric value={canReadCost ? `${effectiveKitCost.toLocaleString('vi-VN')} ₫` : 'Ẩn theo RBAC'} label="Giá vốn định mức 1 Bộ Kit" />
      </UiMetricGrid>

      <UiCard
        title="Khởi tạo lệnh lắp ráp bộ Kit"
        subtitle="Sản phẩm cha: QA-LOT-002 — Bộ Kit Quà tặng Doanh nghiệp • Đơn vị tính: Bộ"
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10, alignItems: 'end' }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12.5, fontWeight: 600 }}>
              Sản phẩm Bộ Kit (SKU)
              <select aria-label="Sản phẩm Bộ Kit" data-testid="select-kit-sku" defaultValue="QA-LOT-002">
                <option value="QA-LOT-002">QA-LOT-002 — Bộ Kit Quà tặng Doanh nghiệp</option>
              </select>
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12.5, fontWeight: 600 }}>
              Loại lệnh sản xuất
              <select aria-label="Chế độ Đóng bộ / Tách bộ Kitting VAS" data-testid="select-kitting-vas-mode" value={kittingVasMode} onChange={(e) => setKittingVasMode(e.target.value as 'PRE_KIT' | 'DEKIT_DISASSEMBLE')}>
                <option value="PRE_KIT">Lắp ráp bộ Kit</option>
                <option value="DEKIT_DISASSEMBLE">Tháo dỡ bộ Kit</option>
              </select>
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12.5, fontWeight: 600 }}>
              Số lượng kế hoạch (Bộ)
              <input type="number" min={1} aria-label="Số lượng kế hoạch Bộ Kit" data-testid="input-kit-planned-qty" value={kitPlannedQty} onChange={(e) => setKitPlannedQty(Math.max(1, Number(e.target.value) || 1))} />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12.5, fontWeight: 600 }}>
              Vị trí kệ nhận (Bin)
              <input type="text" aria-label="Vị trí kệ nhận Bộ Kit" data-testid="input-kit-target-bin" value={kitTargetBin} onChange={(e) => setKitTargetBin(e.target.value)} />
            </label>
          </div>

          <div data-testid="kit-bom-consumption-preview" style={{ padding: '10px 14px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e3a8a', fontSize: 13, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
            {`Xem trước tiêu hao (${kitPlannedQty} Bộ): Cần ${kitPlannedQty} Cái QA-SERIAL-001 (Tồn: 120 — Đủ) + ${kitPlannedQty} Cái QA-LOT-002-BOX (Tồn: 85 — Đủ) • Tổng giá vốn: ${
              canReadCost ? `${(kitPlannedQty * effectiveKitCost).toLocaleString('vi-VN')} ₫` : 'Ẩn theo RBAC'
            }`}
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
            <div className="ui-inline-wrap" style={{ fontSize: 12.5 }}>
              <span data-testid="packing-kit-earliest-expiry"><strong>Hạn dùng kế thừa:</strong> {kitEarliestExpiry}</span>
            </div>
            <button type="button" className="ui-btn ui-btn-primary" data-testid="btn-create-execute-kit" disabled={!!busy || governanceBusy} onClick={() => void onExecuteKittingVas()} style={{ background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 700, whiteSpace: 'nowrap' }}>
              Tạo lệnh lắp ráp
            </button>
          </div>

          {kittingVasResult ? (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {kittingVasResult}
            </div>
          ) : null}
        </div>
      </UiCard>

      <UiCard
        title="Định mức linh kiện cấu thành (BOM chuẩn cho 1 Bộ)"
        subtitle="Định mức linh kiện gốc cho 1 Bộ Kit chuẩn trước khi nhân theo số lượng lệnh sản xuất"
        actions={
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <UiBadge tone="info">
              {canReadCost ? `${effectiveKitCost.toLocaleString('vi-VN')} ₫ / Bộ` : 'Ẩn theo phân quyền'}
            </UiBadge>
            <button type="button" className="ui-btn ui-btn-secondary" data-testid="btn-edit-kit-bom" onClick={() => setShowBomEditor((prev) => !prev)} style={{ whiteSpace: 'nowrap', fontWeight: 600 }}>
              Sửa định mức BOM
            </button>
          </div>
        }
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          {showBomEditor && (
            <div data-testid="kit-bom-editor-panel" style={{ padding: '12px 14px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1', fontSize: 13 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                <strong>Cấu hình Định mức BOM • QA-LOT-002</strong>
                <button type="button" className="ui-btn-secondary" onClick={() => setShowBomEditor(false)} style={{ padding: '4px 10px' }}>Đóng</button>
              </div>
              <p className="ui-muted-text" style={{ margin: 0 }}>
                Định mức chuẩn gồm: 1 Cái Thiết bị định vị GPS Hợp chuẩn (450.000 ₫) + 1 Cái Vỏ hộp quà và Phụ kiện đóng gói (50.000 ₫).
              </p>
            </div>
          )}

          <UiTableScroll>
            <table className="ui-table-compact" data-testid="kit-bom-components-table" aria-label="Định mức linh kiện cấu thành (BOM Component Lines)">
              <thead>
                <tr>
                  <th>Mã linh kiện</th>
                  <th>Tên linh kiện cấu thành</th>
                  <th>Đơn vị tính</th>
                  <th style={{ textAlign: 'right' }}>Định mức / 1 Bộ</th>
                  <th style={{ textAlign: 'right' }}>Tồn khả dụng</th>
                  <th>Lô xuất (FEFO)</th>
                  <th>HSD sớm nhất</th>
                  <th style={{ textAlign: 'right' }}>Đơn giá vốn &amp; Thành tiền</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><strong>QA-SERIAL-001</strong></td>
                  <td>Thiết bị định vị GPS Hợp chuẩn</td>
                  <td className="cell-nowrap" style={{ whiteSpace: 'nowrap' }}><strong>Cái</strong></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>1 Cái</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>120 Cái</td>
                  <td><code>LOT-2026-A1</code></td>
                  <td>31/12/2026</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    {canReadCost ? '1 Cái × 450.000 ₫ = 450.000 ₫' : 'Ẩn theo phân quyền'}
                  </td>
                </tr>
                <tr>
                  <td><strong>QA-LOT-002-BOX</strong></td>
                  <td>Vỏ hộp quà và Phụ kiện đóng gói</td>
                  <td className="cell-nowrap" style={{ whiteSpace: 'nowrap' }}><strong>Cái</strong></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>1 Cái</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>85 Cái</td>
                  <td><code>LOT-2026-B2</code></td>
                  <td>15/06/2027</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    {canReadCost ? '1 Cái × 50.000 ₫ = 50.000 ₫' : 'Ẩn theo phân quyền'}
                  </td>
                </tr>
              </tbody>
            </table>
          </UiTableScroll>
        </div>
      </UiCard>

      <UiCard title="Danh sách lệnh lắp ráp bộ Kit">
        <UiTableScroll>
          <table className="ui-table-compact" data-testid="kit-work-orders-table" aria-label="Danh sách lệnh lắp ráp bộ Kit" style={{ width: '100%', minWidth: 0 }}>
            <thead>
              <tr>
                <th className="ui-sticky-id" style={COMPACT_TH}>Mã lệnh &amp; HSD</th>
                <th style={COMPACT_TH}>Trạng thái</th>
                <th style={COMPACT_TH}>Bộ Kit &amp; Kho</th>
                <th style={{ ...COMPACT_TH, textAlign: 'right' }}>SL &amp; Giá vốn</th>
                <th className="ui-sticky-action" style={{ ...COMPACT_TH, width: 132, maxWidth: 136, background: '#f1f5f9' }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {kitOrders.map((wo) => {
                const isCompleted = wo.status === 'COMPLETED';
                const rowUnitCost = isCompleted ? wo.rolledUpCost : (kitRolledUpCost ?? wo.rolledUpCost);
                const totalOrderCost = isCompleted
                  ? (wo.totalCost ?? wo.plannedQty * wo.rolledUpCost)
                  : (wo.totalCost ?? wo.plannedQty * rowUnitCost);
                const showRowCost = isCompleted ? canReadCost : (canReadCost && kitRolledUpCost !== null);
                return (
                  <tr key={wo.id} data-testid={`kit-wo-row-${wo.id}`}>
                    <td className="ui-sticky-id cell-nowrap" style={{ ...COMPACT_TD, background: '#ffffff' }}>
                      <div><strong style={{ whiteSpace: 'nowrap' }}>{wo.workOrderCode}</strong></div>
                      <div style={{ fontSize: 11.5, color: '#475569', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                        HSD: {wo.earliestExpiry}
                      </div>
                    </td>
                    <td className="cell-nowrap" style={{ ...COMPACT_TD, whiteSpace: 'nowrap' }}>
                      <UiBadge tone={isCompleted ? 'success' : 'warning'}>
                        {isCompleted ? 'Đã hoàn tất' : 'Chờ lắp ráp'}
                      </UiBadge>
                    </td>
                    <td style={{ ...COMPACT_TD, whiteSpace: 'normal', minWidth: 165, lineHeight: 1.35 }}>
                      <div style={{ fontWeight: 600, color: '#0f172a' }}>{wo.kitSku} — {wo.kitName}</div>
                      <div style={{ fontSize: 11.5, color: '#475569' }}>
                        {CANONICAL_WH01_LABEL} • {wo.orderType === 'ASSEMBLY' ? 'Lắp ráp' : 'Tháo dỡ'}
                      </div>
                    </td>
                    <td style={{ ...COMPACT_TD, textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                      <div style={{ fontWeight: 700, color: '#0f172a' }}>
                        {wo.completedQty} / {wo.plannedQty} {wo.uomLabel}
                      </div>
                      <div style={{ fontSize: 11.5, color: '#475569' }}>
                        {showRowCost
                          ? `${totalOrderCost.toLocaleString('vi-VN')} ₫ (${rowUnitCost.toLocaleString('vi-VN')} ₫/bộ)`
                          : 'Ẩn theo phân quyền'}
                      </div>
                    </td>
                    <td className="ui-sticky-action" style={{ ...COMPACT_TD, background: '#ffffff', whiteSpace: 'nowrap', width: 132, maxWidth: 136, overflow: 'visible' }}>
                      {isCompleted ? (
                        <div data-kit-action-menu-wrap="true" style={{ position: 'relative', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                          <button
                            type="button"
                            data-testid={`btn-reverse-kit-${wo.workOrderCode}`}
                            disabled={!!busy || governanceBusy}
                            onClick={() => { setOpenMenuOrderCode(null); setReversalTargetOrder(wo); setReversalReason(''); }}
                            style={{ whiteSpace: 'nowrap', border: '1px solid #dc2626', color: '#dc2626', background: '#ffffff', fontWeight: 600, padding: '5px 8px', fontSize: 12, borderRadius: 6 }}
                          >
                            Đảo lệnh
                          </button>
                          <button
                            type="button"
                            data-testid={`btn-kit-more-${wo.workOrderCode}`}
                            aria-label={`Thao tác khác cho lệnh ${wo.workOrderCode}`}
                            aria-haspopup="menu"
                            aria-expanded={openMenuOrderCode === wo.workOrderCode}
                            disabled={!!busy || governanceBusy}
                            onClick={() => setOpenMenuOrderCode((prev) => (prev === wo.workOrderCode ? null : wo.workOrderCode))}
                            style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', minWidth: 32, padding: '4px 6px', borderRadius: 6, border: '1px solid #cbd5e1', background: '#f8fafc', color: '#0f172a' }}
                          >
                            <MoreHorizontal size={15} strokeWidth={2} aria-hidden="true" />
                          </button>
                          {openMenuOrderCode === wo.workOrderCode && (
                            <div
                              role="menu"
                              aria-label={`Menu thao tác lệnh ${wo.workOrderCode}`}
                              style={{ position: 'absolute', right: 0, top: 'calc(100% + 4px)', zIndex: 40, minWidth: 190, padding: 4, borderRadius: 8, background: '#ffffff', border: '1px solid #cbd5e1', boxShadow: '0 10px 24px rgba(15, 23, 42, 0.16)' }}
                            >
                              <button
                                type="button"
                                role="menuitem"
                                data-testid="menu-item-kit-reverse"
                                onClick={() => { setOpenMenuOrderCode(null); setReversalTargetOrder(wo); setReversalReason(''); }}
                                style={{ width: '100%', textAlign: 'left', padding: '7px 10px', borderRadius: 6, border: 0, background: '#ffffff', color: '#b91c1c', fontWeight: 600, fontSize: 12.5, cursor: 'pointer' }}
                              >
                                Đảo lệnh lắp ráp
                              </button>
                            </div>
                          )}
                        </div>
                      ) : (
                        <button
                          type="button"
                          className="ui-btn ui-btn-primary"
                          data-testid={`btn-execute-kit-${wo.workOrderCode}`}
                          disabled={!!busy || governanceBusy}
                          onClick={() => void onExecuteKittingVas('PRE_KIT')}
                          style={{ whiteSpace: 'nowrap', background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 600, padding: '5px 10px', fontSize: 12, borderRadius: 6 }}
                        >
                          Thực thi
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <KitWorkOrderReversalModal
        order={reversalTargetOrder}
        canReadCost={canReadCost}
        reversalReason={reversalReason}
        setReversalReason={setReversalReason}
        busy={!!busy || governanceBusy}
        onClose={() => { setReversalTargetOrder(null); setReversalReason(''); }}
        onConfirm={handleConfirmReversal}
      />
    </div>
  );
};
