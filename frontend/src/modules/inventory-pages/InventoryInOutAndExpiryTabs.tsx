import React from 'react';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  InventoryExpiryAgingDto,
  InventoryInOutDto,
  InventoryStockDto,
  WarehouseOption,
} from './inventoryShared';
import {
  formatUnitName,
  inventoryStatusLabel,
  inventoryStatusTone,
} from './inventoryShared';

const rightNumStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
};

interface InventoryInOutTabProps {
  warehouses: WarehouseOption[];
  stocks: InventoryStockDto[];
  inoutFilter: { fromDate: string; toDate: string; warehouseId: string; productId: string };
  setInoutFilter: React.Dispatch<React.SetStateAction<{ fromDate: string; toDate: string; warehouseId: string; productId: string }>>;
  inoutReport: InventoryInOutDto[] | null;
  inoutLoading: boolean;
  inoutError: string;
  inoutPage: number;
  inoutPageSize: number;
  setInoutPage: React.Dispatch<React.SetStateAction<number>>;
  onFilterSubmit: (e: React.FormEvent) => void;
  onExportInOut: () => void;
}

export const InventoryInOutTab: React.FC<InventoryInOutTabProps> = ({
  warehouses,
  stocks,
  inoutFilter,
  setInoutFilter,
  inoutReport,
  inoutLoading,
  inoutError,
  inoutPage,
  inoutPageSize,
  setInoutPage,
  onFilterSubmit,
  onExportInOut,
}) => {
  const inoutTotalPages = inoutReport ? Math.ceil(inoutReport.length / inoutPageSize) : 0;
  const safeInoutPage = inoutTotalPages > 0 ? Math.min(inoutPage, inoutTotalPages) : 1;
  const inoutCurrentPageData = inoutReport ? inoutReport.slice((safeInoutPage - 1) * inoutPageSize, safeInoutPage * inoutPageSize) : [];
  const productOptions = Array.from(
    new Map([
      ...stocks.map((s) => [s.productId, { id: s.productId, code: s.productCode, name: s.productName }] as const),
      ...(inoutReport ?? []).map((i) => [i.productId, { id: i.productId, code: i.productCode, name: i.productName }] as const),
    ]).values(),
  );

  return (
    <UiCard title="Báo cáo Xuất Nhập Tồn">
      <div className="inventory-report-card">
        <form onSubmit={onFilterSubmit}>
          <UiToolbar>
            <UiToolbarField label="Từ ngày">
              <input
                aria-label="Báo cáo từ ngày"
                type="date"
                value={inoutFilter.fromDate}
                onChange={(e) => setInoutFilter({ ...inoutFilter, fromDate: e.target.value })}
              />
            </UiToolbarField>

            <UiToolbarField label="Đến ngày">
              <input
                aria-label="Báo cáo đến ngày"
                type="date"
                value={inoutFilter.toDate}
                onChange={(e) => setInoutFilter({ ...inoutFilter, toDate: e.target.value })}
              />
            </UiToolbarField>

            <UiToolbarField label="Kho">
              <select
                aria-label="Kho báo cáo xuất nhập tồn"
                value={inoutFilter.warehouseId}
                onChange={(e) => setInoutFilter({ ...inoutFilter, warehouseId: e.target.value })}
              >
                <option value="">Tất cả kho được phép</option>
                {warehouses.map((warehouse) => (
                  <option key={warehouse.id} value={warehouse.id}>
                    {warehouse.name}
                  </option>
                ))}
              </select>
            </UiToolbarField>

            <UiToolbarField label="Sản phẩm">
              <select
                aria-label="ID sản phẩm báo cáo xuất nhập tồn"
                value={inoutFilter.productId}
                onChange={(e) => setInoutFilter({ ...inoutFilter, productId: e.target.value })}
              >
                <option value="">Tất cả sản phẩm</option>
                {productOptions.map((p) => (
                  <option key={p.id} value={String(p.id)}>
                    {p.code} — {p.name}
                  </option>
                ))}
              </select>
            </UiToolbarField>

            <div className="inventory-toolbar-actions">
              <button type="submit">Lọc Báo Cáo</button>
              <button type="button" onClick={onExportInOut}>
                Xuất Excel
              </button>
            </div>
          </UiToolbar>
        </form>

        {inoutError && <p role="alert">{inoutError}</p>}
        {inoutLoading && !inoutReport ? (
          <p role="status">Đang tải...</p>
        ) : (
          <UiTableScroll>
            <table aria-label="Báo cáo xuất nhập tồn" className="ui-table-compact">
              <thead>
                <tr>
                  <th>Mã SP</th>
                  <th>Tên SP</th>
                  <th>ĐVT</th>
                  <th>Kho</th>
                  <th className="inventory-numeric" style={rightNumStyle}>Tồn đầu kỳ</th>
                  <th className="inventory-numeric" style={rightNumStyle}>Nhập trong kỳ</th>
                  <th className="inventory-numeric" style={rightNumStyle}>Xuất trong kỳ</th>
                  <th className="inventory-numeric" style={rightNumStyle}>Tồn cuối kỳ</th>
                </tr>
              </thead>
              <tbody>
                {!inoutReport || inoutReport.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="ui-empty-cell">Không có dữ liệu báo cáo</td>
                  </tr>
                ) : (
                  inoutCurrentPageData.map((item, index) => (
                    <tr key={item.productId + '-' + item.warehouseId + '-' + index}>
                      <td><strong>{item.productCode}</strong></td>
                      <td>{item.productName}</td>
                      <td>{formatUnitName(item.unitName)}</td>
                      <td>{item.warehouseName}</td>
                      <td className="inventory-numeric" style={rightNumStyle}>{item.openingQuantity}</td>
                      <td className="inventory-numeric" style={rightNumStyle}>{item.inQuantity}</td>
                      <td className="inventory-numeric" style={rightNumStyle}>{item.outQuantity}</td>
                      <td className="inventory-numeric inventory-available" style={rightNumStyle}>{item.closingQuantity}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
        )}

        {inoutReport && inoutTotalPages > 1 && (
          <div className="ui-pagination" aria-label="Phân trang báo cáo xuất nhập tồn">
            <button type="button" disabled={safeInoutPage <= 1} onClick={() => setInoutPage((prev) => Math.max(prev - 1, 1))}>
              Trang trước
            </button>
            <span>Trang {safeInoutPage} / {inoutTotalPages} (Tổng: {inoutReport.length})</span>
            <button type="button" disabled={safeInoutPage >= inoutTotalPages} onClick={() => setInoutPage((prev) => Math.min(prev + 1, inoutTotalPages))}>
              Trang sau
            </button>
          </div>
        )}
      </div>
    </UiCard>
  );
};

interface InventoryExpiryTabProps {
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
                  {warehouse.name}
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
                <th>Kho / Vị trí</th>
                <th>Sản phẩm</th>
                <th>Mã lô</th>
                <th>Trạng thái tồn</th>
                <th>Ngày SX</th>
                <th>Hạn sử dụng</th>
                <th>Tình trạng HSD</th>
                <th className="inventory-numeric ui-cell-numeric" style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>Tuổi tồn (ngày)</th>
                <th className="inventory-numeric ui-cell-numeric" style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                <th className="inventory-numeric ui-cell-numeric" style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>Khả dụng</th>
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
                        <strong>{row.warehouseName}</strong>
                        <br />
                        <small>{row.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : row.locationCode}</small>
                      </td>
                      <td>
                        <strong>{row.productCode}</strong> — {row.productName}
                      </td>
                      <td>{row.lotNumber || '—'}</td>
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
