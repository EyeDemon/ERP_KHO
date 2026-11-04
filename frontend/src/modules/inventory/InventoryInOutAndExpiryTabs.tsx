import React from 'react';
import { UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  InventoryInOutDto,
  InventoryStockDto,
  WarehouseOption,
} from './inventoryShared';
import { formatUnitName } from './inventoryShared';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';

export { InventoryExpiryTab } from './InventoryExpiryTab';
export type { InventoryExpiryTabProps } from './InventoryExpiryTab';

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
                    {formatWarehouseLabel(warehouse.name, (warehouse as { code?: string }).code, warehouse.id)}
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
                      <td>{formatWarehouseLabel(item.warehouseName, undefined, item.warehouseId)}</td>
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
