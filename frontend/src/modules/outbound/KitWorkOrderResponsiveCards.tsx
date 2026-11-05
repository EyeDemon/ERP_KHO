import React from 'react';
import { Lock, MoreHorizontal } from 'lucide-react';
import { UiBadge, UiTableScroll } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL } from '../../utils/warehouseLabel';
import type { KitWorkOrderRow } from './packingSessionsTypes';

export interface KitBomComponentItem {
  sku: string;
  name: string;
  uom: string;
  perKit: number;
  available: number;
  lotCode: string;
  earliestExpiry: string;
  unitCost: number;
}

export const KIT_BOM_COMPONENTS: KitBomComponentItem[] = [
  {
    sku: 'QA-SERIAL-001',
    name: 'Thiết bị định vị GPS Hợp chuẩn',
    uom: 'Cái',
    perKit: 1,
    available: 120,
    lotCode: 'LOT-2026-A1',
    earliestExpiry: '31/12/2026',
    unitCost: 450000,
  },
  {
    sku: 'QA-LOT-002-BOX',
    name: 'Vỏ hộp quà và Phụ kiện đóng gói',
    uom: 'Cái',
    perKit: 1,
    available: 85,
    lotCode: 'LOT-2026-B2',
    earliestExpiry: '15/06/2027',
    unitCost: 50000,
  },
];

export const MaskedCostInline: React.FC = () => (
  <span
    className="ui-cost-masked-badge"
    style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4, color: '#475569', fontSize: 12, fontWeight: 600 }}
  >
    <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
  </span>
);

export const KitBomConsumptionPreviewSection: React.FC<{
  kitPlannedQty: number;
  effectiveKitCost: number;
  canReadCost: boolean;
}> = ({ kitPlannedQty, effectiveKitCost, canReadCost }) => (
  <div
    data-testid="kit-bom-consumption-preview"
    style={{ padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1' }}
  >
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 8, fontSize: 12.5, fontWeight: 600, color: '#0f172a' }}>
      <span>Xem trước định mức tiêu hao ({kitPlannedQty} Bộ)</span>
      <span style={{ fontVariantNumeric: 'tabular-nums', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
        <span>Tổng giá vốn dự kiến:</span>
        {canReadCost ? <strong>{(kitPlannedQty * effectiveKitCost).toLocaleString('vi-VN')} ₫</strong> : <MaskedCostInline />}
      </span>
    </div>

    <div className="ui-desktop-table-wrap">
      <UiTableScroll>
        <table className="ui-table-compact" data-testid="kit-bom-preview-table" aria-label="Xem trước định mức tiêu hao linh kiện BOM">
          <thead>
            <tr>
              <th style={{ minWidth: 140, whiteSpace: 'nowrap' }}>Linh kiện</th>
              <th style={{ minWidth: 80, textAlign: 'right', whiteSpace: 'nowrap' }}>Định mức</th>
              <th style={{ minWidth: 84, textAlign: 'right', whiteSpace: 'nowrap' }}>Cần xuất</th>
              <th style={{ minWidth: 96, textAlign: 'right', whiteSpace: 'nowrap' }}>Tồn khả dụng</th>
              <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Trạng thái</th>
            </tr>
          </thead>
          <tbody>
            {KIT_BOM_COMPONENTS.map((item) => {
              const requiredQty = kitPlannedQty * item.perKit;
              const isSufficient = item.available >= requiredQty;
              return (
                <tr key={item.sku}>
                  <td>
                    <div><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.sku}</strong></div>
                    <div style={{ fontSize: 11.5, color: '#475569' }}>{item.name}</div>
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    {item.perKit} {item.uom}
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', fontWeight: 700 }}>
                    {requiredQty} {item.uom}
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    {item.available} {item.uom}
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <UiBadge tone={isSufficient ? 'success' : 'danger'}>
                      {isSufficient ? 'Đủ tồn' : 'Thiếu tồn'}
                    </UiBadge>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </UiTableScroll>
    </div>

    <div className="ui-mobile-card-list" aria-label="Thẻ xem trước tiêu hao BOM">
      {KIT_BOM_COMPONENTS.map((item) => {
        const requiredQty = kitPlannedQty * item.perKit;
        const isSufficient = item.available >= requiredQty;
        return (
          <article key={item.sku} className="ui-mobile-card" style={{ padding: '10px 12px', gap: 6 }}>
            <div className="ui-mobile-card-head">
              <div>
                <div className="ui-mobile-card-title ui-code" style={{ whiteSpace: 'nowrap' }}>{item.sku}</div>
                <div className="ui-mobile-card-subtitle">{item.name}</div>
              </div>
              <UiBadge tone={isSufficient ? 'success' : 'danger'}>{isSufficient ? 'Đủ tồn' : 'Thiếu tồn'}</UiBadge>
            </div>
            <div className="ui-mobile-card-grid">
              <div className="ui-mobile-card-field">
                <span className="ui-mobile-card-label">Định mức / Cần xuất</span>
                <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>
                  {item.perKit} {item.uom} • Cần {requiredQty} {item.uom}
                </span>
              </div>
              <div className="ui-mobile-card-field">
                <span className="ui-mobile-card-label">Tồn khả dụng</span>
                <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>
                  {item.available} {item.uom}
                </span>
              </div>
            </div>
          </article>
        );
      })}
    </div>
  </div>
);

export const KitBomComponentsMobileCards: React.FC<{ canReadCost: boolean }> = ({ canReadCost }) => (
  <div className="ui-mobile-card-list" aria-label="Danh sách định mức linh kiện BOM">
    {KIT_BOM_COMPONENTS.map((item) => (
      <article key={item.sku} className="ui-mobile-card">
        <div className="ui-mobile-card-head">
          <div>
            <div className="ui-mobile-card-title ui-code" style={{ whiteSpace: 'nowrap' }}>{item.sku}</div>
            <div className="ui-mobile-card-subtitle">{item.name}</div>
          </div>
          <UiBadge tone="info">{item.perKit} {item.uom} / Bộ</UiBadge>
        </div>
        <div className="ui-mobile-card-grid">
          <div className="ui-mobile-card-field">
            <span className="ui-mobile-card-label">Tồn khả dụng</span>
            <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>{item.available} {item.uom}</span>
          </div>
          <div className="ui-mobile-card-field">
            <span className="ui-mobile-card-label">Lô xuất ưu tiên hạn dùng</span>
            <span className="ui-mobile-card-value ui-code" style={{ whiteSpace: 'nowrap' }}>{item.lotCode} ({item.earliestExpiry})</span>
          </div>
          <div className="ui-mobile-card-field" style={{ gridColumn: '1 / -1' }}>
            <span className="ui-mobile-card-label">Giá vốn định mức</span>
            <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>
              {canReadCost ? `${item.perKit} ${item.uom} × ${item.unitCost.toLocaleString('vi-VN')} ₫ = ${item.unitCost.toLocaleString('vi-VN')} ₫` : <MaskedCostInline />}
            </span>
          </div>
        </div>
      </article>
    ))}
  </div>
);

export const KitWorkOrdersMobileCards: React.FC<{
  kitOrders: KitWorkOrderRow[];
  kitRolledUpCost: number | null;
  canReadCost: boolean;
  busy: boolean;
  onOpenReversal: (wo: KitWorkOrderRow) => void;
  onExecutePreKit: () => void;
}> = ({ kitOrders, kitRolledUpCost, canReadCost, busy, onOpenReversal, onExecutePreKit }) => (
  <div className="ui-mobile-card-list" aria-label="Danh sách thẻ lệnh lắp ráp bộ Kit">
    {kitOrders.map((wo) => {
      const isCompleted = wo.status === 'COMPLETED';
      const rowUnitCost = isCompleted ? wo.rolledUpCost : (kitRolledUpCost ?? wo.rolledUpCost);
      const totalOrderCost = wo.totalCost ?? wo.plannedQty * rowUnitCost;
      const showRowCost = isCompleted ? canReadCost : (canReadCost && kitRolledUpCost !== null);
      return (
        <article key={wo.id} className="ui-mobile-card" data-testid={`kit-wo-mobile-card-${wo.id}`}>
          <div className="ui-mobile-card-head">
            <div>
              <div className="ui-mobile-card-title ui-code" style={{ whiteSpace: 'nowrap' }}>{wo.workOrderCode}</div>
              <div className="ui-mobile-card-subtitle">{wo.kitSku} — {wo.kitName}</div>
            </div>
            <UiBadge tone={isCompleted ? 'success' : 'warning'}>
              {isCompleted ? 'Đã hoàn tất' : 'Chờ lắp ráp'}
            </UiBadge>
          </div>
          <div className="ui-mobile-card-grid">
            <div className="ui-mobile-card-field">
              <span className="ui-mobile-card-label">Kho &amp; Hạn dùng</span>
              <span className="ui-mobile-card-value">{CANONICAL_WH01_LABEL} • {wo.earliestExpiry}</span>
            </div>
            <div className="ui-mobile-card-field">
              <span className="ui-mobile-card-label">Số lượng &amp; Giá vốn</span>
              <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {wo.completedQty} / {wo.plannedQty} {wo.uomLabel} •{' '}
                {showRowCost ? `${totalOrderCost.toLocaleString('vi-VN')} ₫` : <MaskedCostInline />}
              </span>
            </div>
          </div>
          <div style={{ display: 'flex', flexDirection: 'row', flexWrap: 'nowrap', alignItems: 'center', gap: 8, width: '100%' }}>
            {isCompleted ? (
              <>
                <button
                  type="button"
                  className="ui-btn ui-btn-secondary"
                  disabled={busy}
                  onClick={() => onOpenReversal(wo)}
                  style={{ flex: '1 1 auto', minWidth: 0, minHeight: 44, whiteSpace: 'nowrap', borderColor: '#dc2626', color: '#dc2626', fontWeight: 600 }}
                >
                  Đảo lệnh
                </button>
                <details className="ui-row-more-menu" style={{ flex: '0 0 44px', width: 44 }}>
                  <summary
                    className="ui-more-summary"
                    aria-label="Thao tác khác"
                    style={{ width: 44, height: 44, minWidth: 44, minHeight: 44, padding: 0, display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}
                  >
                    <MoreHorizontal size={18} aria-hidden="true" />
                  </summary>
                  <div className="ui-more-menu-panel">
                    <button
                      type="button"
                      disabled={busy}
                      onClick={(e) => {
                        e.currentTarget.closest('details')?.removeAttribute('open');
                        onOpenReversal(wo);
                      }}
                    >
                      Đảo lệnh lắp ráp
                    </button>
                  </div>
                </details>
              </>
            ) : (
              <button
                type="button"
                className="ui-btn ui-btn-primary"
                disabled={busy}
                onClick={onExecutePreKit}
                style={{ flex: '1 1 auto', minWidth: 0, minHeight: 44, whiteSpace: 'nowrap' }}
              >
                Thực thi lắp ráp
              </button>
            )}
          </div>
        </article>
      );
    })}
  </div>
);
