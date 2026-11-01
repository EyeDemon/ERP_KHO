import React from 'react';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  InventoryStockDto,
  InventoryTransactionHistoryDto,
  PagedResult,
  WarehouseOption,
} from './inventoryShared';
import {
  formatUnitName,
  inventoryStatusLabel,
  inventoryStatusTone,
  referenceTypeLabel,
  transactionTypeLabel,
} from './inventoryShared';

interface InventoryStockTabProps {
  warehouses: WarehouseOption[];
  stocks: InventoryStockDto[];
  paginatedStocks: InventoryStockDto[];
  stockLoading: boolean;
  stockError: string;
  stockFilter: { warehouseId: string; productId: string; keyword: string };
  setStockFilter: React.Dispatch<React.SetStateAction<{ warehouseId: string; productId: string; keyword: string }>>;
  currentStockPage: number;
  stockTotalPages: number;
  setStockPage: React.Dispatch<React.SetStateAction<number>>;
  onFilterSubmit: (e: React.FormEvent) => void;
  onExportStock: () => void;
  onOpeningBalanceImport: (e: React.FormEvent) => void;
  onDownloadOpeningBalanceTemplate: () => void;
  onOpeningFileChange: (file: File | null) => void;
  openingBalanceNotice: string;
  openingBalanceResult: { importBatchId: string; importedRows: number; status: string } | null;
}

const rightNumStyle: React.CSSProperties = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' };

export const InventoryStockTab: React.FC<InventoryStockTabProps> = ({
  warehouses, stocks, paginatedStocks, stockLoading, stockError, stockFilter, setStockFilter,
  currentStockPage, stockTotalPages, setStockPage, onFilterSubmit, onExportStock,
  onOpeningBalanceImport, onDownloadOpeningBalanceTemplate, onOpeningFileChange,
  openingBalanceNotice, openingBalanceResult,
}) => (
  <UiCard title="Tồn kho hiện tại">
    <div className="ui-subcard" style={{ marginBottom: 12 }}>
      <div className="ui-subcard-title">Nhập Tồn Kho Đầu Kỳ &amp; Giá Vốn Bình Quân Gia Quyền (Tệp Excel Chuẩn)</div>
      <form onSubmit={onOpeningBalanceImport} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          Tệp Excel tồn kho đầu kỳ (.xlsx)
          <input
            aria-label="Tệp Excel tồn kho đầu kỳ"
            type="file"
            accept=".xlsx,.xls,.csv"
            onChange={(e) => onOpeningFileChange(e.target.files?.[0] ?? null)}
          />
        </label>
        <button type="submit">Nhập Excel Tồn kho Đầu kỳ</button>
        <button type="button" className="btn-secondary" onClick={onDownloadOpeningBalanceTemplate}>
          Tải file mẫu .xlsx
        </button>
      </form>
      {openingBalanceNotice && <p role="status" className="ui-success-text" style={{ marginTop: 8 }}>{openingBalanceNotice}</p>}
      {openingBalanceResult && (
        <div
          role="status"
          aria-label="Kết quả nhập tồn kho đầu kỳ"
          style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
        >
          {`Mã lô nhập: ${openingBalanceResult.importBatchId} • Số dòng hợp lệ: ${openingBalanceResult.importedRows} • ${openingBalanceResult.status}`}
        </div>
      )}
    </div>
    <form onSubmit={onFilterSubmit}>
      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho tồn hiện tại" value={stockFilter.warehouseId} onChange={(e) => setStockFilter({ ...stockFilter, warehouseId: e.target.value })}>
            <option value="">Tất cả kho được phép</option>
            {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Sản phẩm">
          <select aria-label="ID sản phẩm tồn hiện tại" value={stockFilter.productId} onChange={(e) => setStockFilter({ ...stockFilter, productId: e.target.value })}>
            <option value="">Tất cả sản phẩm</option>
            {Array.from(new Map(stocks.map((s) => [s.productId, { id: s.productId, code: s.productCode, name: s.productName }] as const)).values()).map((p) => (
              <option key={p.id} value={String(p.id)}>{p.code} — {p.name}</option>
            ))}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Từ khóa SP">
          <input
            aria-label="Từ khóa sản phẩm tồn hiện tại"
            type="text"
            value={stockFilter.keyword}
            onChange={(e) => setStockFilter({ ...stockFilter, keyword: e.target.value })}
            placeholder="Mã hoặc tên SP..."
          />
        </UiToolbarField>
        <div className="inventory-toolbar-actions">
          <button type="submit">Lọc Tồn Kho</button>
          <button type="button" onClick={onExportStock}>Xuất Excel Tồn Kho</button>
        </div>
      </UiToolbar>
    </form>

    {stockError && <p role="alert">{stockError}</p>}
    {stockLoading && stocks.length === 0 ? (
      <p role="status">Đang tải...</p>
    ) : (
      <UiTableScroll>
        <table aria-label="Tồn kho hiện tại" className="ui-table-compact inventory-table-wide">
          <thead>
            <tr>
              <th>Mã SP</th>
              <th>Tên SP</th>
              <th>ĐVT</th>
              <th>Kho</th>
              <th className="inventory-numeric" style={rightNumStyle}>Tồn thực tế</th>
              <th className="inventory-numeric" style={rightNumStyle}>Đã giữ</th>
              <th className="inventory-numeric" style={rightNumStyle}>Khả dụng</th>
              <th>Cập nhật lần cuối</th>
            </tr>
          </thead>
          <tbody>
            {paginatedStocks.length === 0 ? (
              <tr><td colSpan={8} className="ui-empty-cell">Không có dữ liệu tồn kho</td></tr>
            ) : (
              paginatedStocks.map((item, index) => (
                <tr key={item.productId + '-' + item.warehouseId + '-' + index}>
                  <td><strong>{item.productCode}</strong></td>
                  <td>{item.productName}</td>
                  <td>{formatUnitName(item.unitName)}</td>
                  <td>{item.warehouseName}</td>
                  <td className="inventory-numeric" style={rightNumStyle}>{item.onHandQuantity}</td>
                  <td className="inventory-numeric" style={rightNumStyle}>{item.reservedQuantity}</td>
                  <td className="inventory-numeric inventory-available" style={rightNumStyle}>{item.availableQuantity}</td>
                  <td>{new Date(item.lastUpdated).toLocaleString('vi-VN')}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </UiTableScroll>
    )}

    {stocks.length > 0 && stockTotalPages > 1 && (
      <div className="ui-pagination" aria-label="Phân trang tồn kho">
        <button type="button" disabled={currentStockPage <= 1} onClick={() => setStockPage((prev) => Math.max(prev - 1, 1))}>Trang trước</button>
        <span>Trang {currentStockPage} / {stockTotalPages} (Tổng: {stocks.length})</span>
        <button type="button" disabled={currentStockPage >= stockTotalPages} onClick={() => setStockPage((prev) => Math.min(prev + 1, stockTotalPages))}>Trang sau</button>
      </div>
    )}
  </UiCard>
);

type HistoryFilterState = {
  fromDate: string; toDate: string; transactionType: string; warehouseId: string;
  productId: string; keyword: string; page: number; pageSize: number;
};

interface InventoryHistoryTabProps {
  warehouses: WarehouseOption[];
  history: PagedResult<InventoryTransactionHistoryDto> | null;
  historyLoading: boolean;
  historyError: string;
  historyFilter: HistoryFilterState;
  setHistoryFilter: React.Dispatch<React.SetStateAction<HistoryFilterState>>;
  onFilterSubmit: (e: React.FormEvent) => void;
}

const normalizeHistoryNote = (note?: string | null) =>
  (note || '')
    .replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]')
    .replace(/\[?HandlingUnitPalletSeed\]?/gi, '[Khởi tạo kiện LPN]')
    .replace(/\[?InternalMove\]?/gi, '[Di chuyển nội bộ]')
    .replace(/\[?QASeedReconcile\]?/gi, '[Đối soát sổ cái]')
    .replace(/\[?VAS_DEKIT\]?/gi, '[Lệnh rã bộ KIT]')
    .replace(/\(100%\s*Match\)/gi, '(Khớp 100%)')
    .replace(/\s*\(De-kitting\)/gi, '')
    .replace(/\bRECEIVING\b/g, 'RCV-01')
    .replace(/chờ\s*QC\b/gi, 'chờ kiểm định chất lượng') || '—';

export const InventoryHistoryTab: React.FC<InventoryHistoryTabProps> = ({
  warehouses, history, historyLoading, historyError, historyFilter, setHistoryFilter, onFilterSubmit,
}) => (
  <UiCard title="Lịch sử giao dịch">
    <form onSubmit={onFilterSubmit}>
      <UiToolbar>
        <UiToolbarField label="Từ ngày">
          <input aria-label="Lịch sử từ ngày" type="date" value={historyFilter.fromDate} onChange={(e) => setHistoryFilter({ ...historyFilter, fromDate: e.target.value })} />
        </UiToolbarField>
        <UiToolbarField label="Đến ngày">
          <input aria-label="Lịch sử đến ngày" type="date" value={historyFilter.toDate} onChange={(e) => setHistoryFilter({ ...historyFilter, toDate: e.target.value })} />
        </UiToolbarField>
        <UiToolbarField label="Loại GD">
          <select aria-label="Loại giao dịch" value={historyFilter.transactionType} onChange={(e) => setHistoryFilter({ ...historyFilter, transactionType: e.target.value })}>
            <option value="">-- Tất cả --</option>
            <option value="Import">Nhập kho</option>
            <option value="Export">Xuất kho</option>
            <option value="AdjustmentIncrease">Điều chỉnh Tăng</option>
            <option value="AdjustmentDecrease">Điều chỉnh Giảm</option>
            <option value="StatusChange">Đổi trạng thái</option>
            <option value="Move">Di chuyển vị trí nội bộ</option>
            <option value="Reversal">Dấu mốc đảo giao dịch</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Kho">
          <select aria-label="Kho lịch sử giao dịch" value={historyFilter.warehouseId} onChange={(e) => setHistoryFilter({ ...historyFilter, warehouseId: e.target.value })}>
            <option value="">Tất cả kho được phép</option>
            {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Từ khóa SP">
          <input aria-label="Từ khóa sản phẩm lịch sử" type="text" placeholder="Mã hoặc tên SP..." value={historyFilter.keyword} onChange={(e) => setHistoryFilter({ ...historyFilter, keyword: e.target.value })} />
        </UiToolbarField>
        <div className="inventory-toolbar-actions">
          <button type="submit">Lọc Lịch Sử</button>
        </div>
      </UiToolbar>
    </form>

    {historyError && <p role="alert">{historyError}</p>}
    {historyLoading && !history ? (
      <p role="status">Đang tải...</p>
    ) : (
      <>
        <UiTableScroll>
          <table aria-label="Lịch sử giao dịch tồn kho" className="ui-table-compact">
            <thead>
              <tr>
                <th>Ngày GD</th>
                <th>Loại GD</th>
                <th>Kho</th>
                <th>Sản phẩm</th>
                <th>Trạng thái</th>
                <th>Lô / Sê-ri</th>
                <th className="inventory-numeric" style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>SL</th>
                <th>Tham chiếu</th>
                <th>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {!history || history.items.length === 0 ? (
                <tr><td colSpan={9} className="ui-empty-cell">Không có lịch sử giao dịch</td></tr>
              ) : (
                history.items.map((item) => {
                  const increase = item.transactionType === 'Import' || item.transactionType === 'AdjustmentIncrease' || item.transactionType === 'TransferIn';
                  const neutral = item.transactionType === 'Move' || item.transactionType === 'StatusChange' || item.transactionType === 'Reversal';
                  const quantityClass = increase ? 'inventory-qty-increase' : neutral ? '' : 'inventory-qty-decrease';
                  return (
                    <tr key={item.id}>
                      <td>{new Date(item.transactionDate).toLocaleString('vi-VN')}</td>
                      <td>{transactionTypeLabel(item.transactionType).replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]')}</td>
                      <td>{item.warehouseName}</td>
                      <td>{item.productCode} - {item.productName}</td>
                      <td>
                        {item.fromInventoryStatus && item.toInventoryStatus ? (
                          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, flexWrap: 'nowrap' }}>
                            <UiBadge tone={inventoryStatusTone(item.fromInventoryStatus)}>{inventoryStatusLabel(item.fromInventoryStatus)}</UiBadge>
                            <span>→</span>
                            <UiBadge tone={inventoryStatusTone(item.toInventoryStatus)}>{inventoryStatusLabel(item.toInventoryStatus)}</UiBadge>
                          </span>
                        ) : item.inventoryStatus ? (
                          <UiBadge tone={inventoryStatusTone(item.inventoryStatus)}>{inventoryStatusLabel(item.inventoryStatus)}</UiBadge>
                        ) : '—'}
                      </td>
                      <td>{item.lotNumber || '—'}{item.serialNumber ? ` / ${item.serialNumber}` : ''}</td>
                      <td className={'inventory-numeric ' + quantityClass} style={{ ...rightNumStyle, whiteSpace: 'nowrap' }}>
                        {item.quantity} {formatUnitName(item.unitName)}
                      </td>
                      <td>{item.referenceType ? '[' + referenceTypeLabel(item.referenceType) + '] #' + item.referenceId : '—'}</td>
                      <td>{normalizeHistoryNote(item.note)}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </UiTableScroll>

        {history && history.totalPages > 1 && (
          <div className="ui-pagination" aria-label="Phân trang lịch sử tồn kho">
            <button type="button" disabled={history.pageIndex <= 1} onClick={() => setHistoryFilter((prev) => ({ ...prev, page: prev.page - 1 }))}>Trang trước</button>
            <span>Trang {history.pageIndex} / {history.totalPages} (Tổng: {history.totalRecords})</span>
            <button type="button" disabled={history.pageIndex >= history.totalPages} onClick={() => setHistoryFilter((prev) => ({ ...prev, page: prev.page + 1 }))}>Trang sau</button>
          </div>
        )}
      </>
    )}
  </UiCard>
);
