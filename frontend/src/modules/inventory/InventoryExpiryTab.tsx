import React from 'react';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  InventoryExpiryAgingDto,
  InventoryStockDto,
  WarehouseOption,
} from './inventoryShared';
import {
  formatUnitName,
  inventoryStatusLabel,
  inventoryStatusTone,
} from './inventoryShared';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';

const rightNumStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
};

export interface InventoryExpiryTabProps {
  warehouses: WarehouseOption[];
  stocks: InventoryStockDto[];
  expiryFilter: { warehouseId: string; productId: string; nearExpiryDays: string; onlyLots: boolean };
  setExpiryFilter: React.Dispatch<
    React.SetStateAction<{ warehouseId: string; productId: string; nearExpiryDays: string; onlyLots: boolean }>
  >;
  expiryReport: InventoryExpiryAgingDto[];
  expiryLoading: boolean;
  expiryError: string;
  onFilterSubmit: (e: React.FormEvent) => void;
}

const renderExpiryBadge = (status: InventoryExpiryAgingDto['expiryStatus'], days?: number | null) => {
  if (status === 'EXPIRED') return <UiBadge tone="danger">Đã hết hạn ({days} ngày)</UiBadge>;
  if (status === 'NEAR_EXPIRY') return <UiBadge tone="warning">Sắp hết hạn ({days} ngày)</UiBadge>;
  if (status === 'GOOD') return <UiBadge tone="success">Còn hạn tốt ({days} ngày)</UiBadge>;
  return <UiBadge tone="neutral">Không có HSD</UiBadge>;
};

export const InventoryExpiryTab: React.FC<InventoryExpiryTabProps> = ({
  warehouses,
  stocks,
  expiryFilter,
  setExpiryFilter,
  expiryReport,
  expiryLoading,
  expiryError,
  onFilterSubmit,
}) => {
  const productOptions = Array.from(
    new Map([
      ...stocks.map((s) => [s.productId, { id: s.productId, code: s.productCode, name: s.productName }] as const),
      ...(expiryReport ?? []).map((i) => [i.productId, { id: i.productId, code: i.productCode, name: i.productName }] as const),
    ]).values(),
  );

  return (
    <UiCard title="Báo cáo Hạn dùng & Tuổi tồn kho">
      <form onSubmit={onFilterSubmit}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select
              aria-label="Kho báo cáo hạn dùng"
              value={expiryFilter.warehouseId}
              onChange={(e) => setExpiryFilter({ ...expiryFilter, warehouseId: e.target.value })}
            >
              <option value="">Tất cả kho được phép</option>
              {warehouses.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>
                  {formatWarehouseLabel(warehouse.name, (warehouse as { code?: string }).code, warehouse.id)}
                </option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Sản phẩm">
            <select
              aria-label="ID sản phẩm báo cáo hạn dùng"
              value={expiryFilter.productId}
              onChange={(e) => setExpiryFilter({ ...expiryFilter, productId: e.target.value })}
            >
              <option value="">Tất cả sản phẩm</option>
              {productOptions.map((p) => (
                <option key={p.id} value={String(p.id)}>
                  {p.code} — {p.name}
                </option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Ngưỡng cảnh báo cận hạn (ngày)">
            <input
              aria-label="Ngưỡng cảnh báo cận hạn"
              type="number"
              min={1}
              value={expiryFilter.nearExpiryDays}
              onChange={(e) => setExpiryFilter({ ...expiryFilter, nearExpiryDays: e.target.value })}
            />
          </UiToolbarField>
          <UiToolbarField label="Phạm vi lô hàng">
            <label
              className="ui-checkbox-label"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 8,
                minHeight: 38,
                height: 38,
                padding: '0 12px',
                border: '1px solid #cbd5e1',
                borderRadius: 8,
                background: '#ffffff',
                width: '100%',
                boxSizing: 'border-box',
                margin: 0,
              }}
            >
              <input
                type="checkbox"
                style={{ width: 16, height: 16, minHeight: 16, margin: 0, flexShrink: 0 }}
                checked={expiryFilter.onlyLots}
                onChange={(e) => setExpiryFilter({ ...expiryFilter, onlyLots: e.target.checked })}
              />
              Chỉ hiển thị dòng có Lô
            </label>
          </UiToolbarField>
          <div className="inventory-toolbar-actions">
            <button type="submit">Lọc Hạn Dùng & Tuổi Tồn</button>
          </div>
        </UiToolbar>
      </form>

      {expiryError && <p role="alert">{expiryError}</p>}
      {expiryLoading && expiryReport.length === 0 ? (
        <p role="status">Đang tải...</p>
      ) : (
        <UiTableScroll>
          <table aria-label="Báo cáo hạn dùng và tuổi tồn" className="ui-table-compact inventory-table-wide">
            <thead>
              <tr>
                <th style={{ minWidth: 120 }}>Kho / Vị trí</th>
                <th style={{ minWidth: 150 }}>Sản phẩm</th>
                <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Mã lô</th>
                <th style={{ minWidth: 105, whiteSpace: 'nowrap' }}>Trạng thái tồn</th>
                <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Ngày SX</th>
                <th style={{ minWidth: 100, whiteSpace: 'nowrap' }}>Hạn sử dụng</th>
                <th style={{ minWidth: 110, whiteSpace: 'nowrap' }}>Tình trạng HSD</th>
                <th className="inventory-numeric ui-cell-numeric" style={{ ...rightNumStyle, minWidth: 105, whiteSpace: 'nowrap' }}>Tuổi tồn (ngày)</th>
                <th className="inventory-numeric ui-cell-numeric" style={{ ...rightNumStyle, minWidth: 96, whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                <th className="inventory-numeric ui-cell-numeric" style={{ ...rightNumStyle, minWidth: 92, whiteSpace: 'nowrap' }}>Khả dụng</th>
              </tr>
            </thead>
            <tbody>
              {expiryReport.length === 0 ? (
                <tr>
                  <td colSpan={10} className="ui-empty-cell">Không có dữ liệu hạn dùng / tuổi tồn</td>
                </tr>
              ) : (
                expiryReport.map((row) => {
                  const daysUntilExpiry = (row as { daysUntilExpiry?: number | null }).daysUntilExpiry;
                  const canCommit =
                    ['AVAILABLE', 'Available', 'Khả dụng'].includes(row.status) &&
                    row.expiryStatus !== 'EXPIRED' &&
                    !(row.daysToExpiry !== null && row.daysToExpiry !== undefined && row.daysToExpiry < 0) &&
                    !(daysUntilExpiry !== null && daysUntilExpiry !== undefined && daysUntilExpiry < 0);
                  return (
                    <tr key={row.inventoryStockId}>
                      <td>
                        <strong>{formatWarehouseLabel(row.warehouseName, undefined, row.warehouseId)}</strong>
                        <br />
                        <small className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : row.locationCode}</small>
                      </td>
                      <td>
                        <strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.productCode}</strong> — {row.productName}
                      </td>
                      <td style={{ whiteSpace: 'nowrap' }}><span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.lotNumber || '—'}</span></td>
                      <td><UiBadge tone={inventoryStatusTone(row.status)}>{inventoryStatusLabel(row.status)}</UiBadge></td>
                      <td>{row.manufactureDate ? new Date(row.manufactureDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>{row.expiryDate ? new Date(row.expiryDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>{renderExpiryBadge(row.expiryStatus, row.daysToExpiry)}</td>
                      <td className="inventory-numeric" style={rightNumStyle}>{row.ageDays}</td>
                      <td className="inventory-numeric" style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>
                        {row.quantity} {formatUnitName(row.unitName)}
                      </td>
                      <td className="inventory-numeric inventory-available" style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>
                        {canCommit ? row.availableQuantity : 0} {formatUnitName(row.unitName)}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
};
