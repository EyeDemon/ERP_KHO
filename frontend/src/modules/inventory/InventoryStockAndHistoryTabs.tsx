import React, { useRef, useState } from 'react';
import { UiBadge, UiCard, UiErrorBanner, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
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
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { usePlatformMobileViewport } from '../platform/usePlatformMobileViewport';

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
}) => {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [selectedFileName, setSelectedFileName] = useState<string>('');
  const isMobile640 = usePlatformMobileViewport(640);

  return (
    <UiCard title="Tồn kho hiện tại">
      <div className="ui-subcard inventory-opening-subcard" style={{ marginBottom: 10 }}>
        <div className="ui-subcard-title inventory-opening-subcard-title">
          Nhập Tồn Kho Đầu Kỳ &amp; Giá Vốn Bình Quân Gia Quyền (Tệp Excel Chuẩn)
        </div>
        <form onSubmit={onOpeningBalanceImport} className="inventory-opening-form">
          <input
            ref={fileInputRef}
            id="inventory-opening-excel-input"
            aria-label="Tệp Excel tồn kho đầu kỳ"
            type="file"
            accept=".xlsx,.xls,.csv"
            className="sr-only"
            onChange={(e) => {
              const picked = e.target.files?.[0] ?? null;
              setSelectedFileName(picked ? picked.name : '');
              onOpeningFileChange(picked);
            }}
          />
          <div className="inventory-file-picker-row">
            <button
              type="button"
              className="btn-secondary inventory-file-pick-btn"
              data-testid="btn-pick-opening-excel"
              style={{ minHeight: 44 }}
              onClick={() => fileInputRef.current?.click()}
            >
              Chọn tệp Excel (.xlsx)
            </button>
            <span
              id="opening-excel-file-status"
              data-testid="opening-excel-filename"
              className="inventory-file-status-chip"
            >
              {selectedFileName || 'Chưa chọn tệp nào'}
            </span>
            <button
              type="button"
              className="btn-secondary inventory-file-clear-btn"
              data-testid="btn-clear-opening-excel"
              disabled={!selectedFileName}
              title={!selectedFileName ? 'Chưa chọn tệp Excel tồn đầu kỳ nào để xóa' : undefined}
              aria-describedby={!selectedFileName ? 'opening-excel-file-status' : undefined}
              style={{ minHeight: 44 }}
              onClick={() => {
                if (fileInputRef.current) fileInputRef.current.value = '';
                setSelectedFileName('');
                onOpeningFileChange(null);
              }}
            >
              Xóa
            </button>
          </div>
          <div className="inventory-opening-actions-row">
            <button type="submit" style={{ minHeight: 44 }}>Nhập Excel Tồn kho Đầu kỳ</button>
            <button type="button" className="btn-secondary" style={{ minHeight: 44 }} onClick={onDownloadOpeningBalanceTemplate}>
              Tải file mẫu .xlsx
            </button>
          </div>
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
              <option value="">Tất cả kho</option>
              {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{formatWarehouseLabel(warehouse.name, (warehouse as { code?: string }).code, warehouse.id)}</option>)}
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
            <button type="submit" style={{ minHeight: 44 }}>Lọc Tồn Kho</button>
            <button type="button" style={{ minHeight: 44 }} onClick={onExportStock}>Xuất Excel Tồn Kho</button>
          </div>
        </UiToolbar>
      </form>

      {stockError && <UiErrorBanner message={stockError} />}
      {stockLoading && stocks.length === 0 ? (
        <p role="status">Đang tải...</p>
      ) : isMobile640 ? (
        <div className="ui-mobile-card-list" data-testid="inventory-stock-mobile-cards" style={{ display: 'grid', gap: 8 }}>
          {paginatedStocks.length === 0 ? (
            <p className="ui-empty-cell">Không có dữ liệu tồn kho</p>
          ) : (
            paginatedStocks.map((item, index) => (
              <article
                key={item.productId + '-' + item.warehouseId + '-' + index}
                className="ui-mobile-card"
                data-testid={`inventory-stock-mobile-card-${item.productId}-${item.warehouseId}`}
                style={{ padding: '8px 10px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#ffffff', display: 'grid', gap: 4 }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                  <strong className="ui-code" style={{ fontSize: 13, color: '#0f172a' }}>{item.productCode}</strong>
                  <span style={{ fontSize: 11.5, color: '#475569' }}>{formatWarehouseLabel(item.warehouseName, undefined, item.warehouseId)}</span>
                </div>
                <div style={{ fontSize: 12.5, fontWeight: 600, color: '#1e293b', overflowWrap: 'anywhere' }}>{item.productName}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 6, fontSize: 11.5, fontVariantNumeric: 'tabular-nums', paddingTop: 4, borderTop: '1px solid #e2e8f0' }}>
                  <div><span style={{ color: '#64748b' }}>Thực tế:</span> <strong>{item.onHandQuantity}</strong> {formatUnitName(item.unitName)}</div>
                  <div><span style={{ color: '#64748b' }}>Đã giữ:</span> <strong>{item.reservedQuantity}</strong></div>
                  <div style={{ textAlign: 'right' }}><span style={{ color: '#64748b' }}>Khả dụng:</span> <strong style={{ color: '#0f172a' }}>{item.availableQuantity}</strong></div>
                </div>
              </article>
            ))
          )}
        </div>
      ) : (
        <UiTableScroll>
          <table aria-label="Tồn kho hiện tại" className="ui-table-compact inventory-table-wide inventory-current-stock-table">
            <thead>
              <tr>
                <th className="inv-col-code">Mã SP</th>
                <th className="inv-col-name">Tên SP</th>
                <th className="inv-col-unit">Đơn vị</th>
                <th className="inv-col-wh">Kho</th>
                <th className="inventory-numeric inv-col-onhand" style={rightNumStyle}>Tồn thực tế</th>
                <th className="inventory-numeric inv-col-reserved" style={rightNumStyle}>Đã giữ</th>
                <th className="inventory-numeric inv-col-avail" style={rightNumStyle}>Khả dụng</th>
                <th className="inv-col-updated">Cập nhật lần cuối</th>
              </tr>
            </thead>
            <tbody>
              {paginatedStocks.length === 0 ? (
                <tr><td colSpan={8} className="ui-empty-cell">Không có dữ liệu tồn kho</td></tr>
              ) : (
                paginatedStocks.map((item, index) => (
                  <tr key={item.productId + '-' + item.warehouseId + '-' + index}>
                    <td><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.productCode}</strong></td>
                    <td>{item.productName}</td>
                    <td>{formatUnitName(item.unitName)}</td>
                    <td>{formatWarehouseLabel(item.warehouseName, undefined, item.warehouseId)}</td>
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
};

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
            {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{formatWarehouseLabel(warehouse.name, (warehouse as { code?: string }).code, warehouse.id)}</option>)}
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
                <th style={{ minWidth: 115, whiteSpace: 'nowrap' }}>Ngày GD</th>
                <th style={{ minWidth: 115 }}>Loại GD</th>
                <th style={{ minWidth: 110 }}>Kho</th>
                <th style={{ minWidth: 150 }}>Sản phẩm</th>
                <th style={{ minWidth: 105 }}>Trạng thái</th>
                <th style={{ minWidth: 105 }}>Lô / Sê-ri</th>
                <th className="inventory-numeric" style={{ ...rightNumStyle, minWidth: 92, whiteSpace: 'nowrap' }}>Số lượng</th>
                <th style={{ minWidth: 115 }}>Tham chiếu</th>
                <th style={{ minWidth: 135 }}>Ghi chú</th>
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
                      <td style={{ whiteSpace: 'nowrap' }}>{new Date(item.transactionDate).toLocaleString('vi-VN')}</td>
                      <td>{transactionTypeLabel(item.transactionType).replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]')}</td>
                      <td>{formatWarehouseLabel(item.warehouseName, undefined, item.warehouseId)}</td>
                      <td><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.productCode}</strong> - {item.productName}</td>
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
