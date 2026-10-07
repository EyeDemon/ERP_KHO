import React, { useState, useEffect } from 'react';
import apiClient from '../services/apiClient';
import { UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';
import InventoryBuckets from './InventoryBuckets';
import './Inventory.css';

interface InventoryStockDto {
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  warehouseId: number;
  warehouseName: string;
  quantity: number;
  onHandQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  lastUpdated: string;
}

interface InventoryTransactionHistoryDto {
  id: number;
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  warehouseId: number;
  warehouseName: string;
  transactionType: string;
  inventoryStatus?: string;
  fromInventoryStatus?: string | null;
  toInventoryStatus?: string | null;
  lotId?: number | null;
  lotNumber?: string | null;
  expiryDate?: string | null;
  serialId?: number | null;
  serialNumber?: string | null;
  quantity: number;
  referenceId: number | null;
  referenceType: string | null;
  transactionDate: string;
  createdBy: number;
  createdByName: string;
  note: string | null;
}

interface InventoryInOutDto {
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  warehouseId: number;
  warehouseName: string;
  openingQuantity: number;
  inQuantity: number;
  outQuantity: number;
  closingQuantity: number;
}

interface PagedResult<T> {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
}

interface WarehouseOption {
  id: number;
  name: string;
  isActive: boolean;
}

const Inventory = () => {
  const [activeTab, setActiveTab] = useState<'stock' | 'buckets' | 'history' | 'inout'>('stock');
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);

  // Stock State
  const [stocks, setStocks] = useState<InventoryStockDto[]>([]);
  const [stockLoading, setStockLoading] = useState(false);
  const [stockError, setStockError] = useState('');
  const [stockFilter, setStockFilter] = useState({ warehouseId: '', productId: '', keyword: '' });
  const [stockPage, setStockPage] = useState(1);
  const stockPageSize = 20;

  const stockTotalPages = Math.ceil(stocks.length / stockPageSize) || 1;
  const currentStockPage = Math.min(stockPage, stockTotalPages);
  const paginatedStocks = stocks.slice((currentStockPage - 1) * stockPageSize, currentStockPage * stockPageSize);

  useEffect(() => {
    apiClient.get('/api/warehouses')
      .then(response => setWarehouses(response.data.filter((warehouse: WarehouseOption) => warehouse.isActive)))
      .catch(() => setWarehouses([]));
  }, []);

  useEffect(() => {
    if (stockPage > stockTotalPages && stockTotalPages > 0) {
      setStockPage(stockTotalPages);
    }
  }, [stocks.length, stockTotalPages, stockPage]);

  // History State
  const [history, setHistory] = useState<PagedResult<InventoryTransactionHistoryDto> | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [historyFilter, setHistoryFilter] = useState({
    fromDate: '',
    toDate: '',
    transactionType: '',
    warehouseId: '',
    productId: '',
    keyword: '',
    page: 1,
    pageSize: 20
  });

  // InOut State
  const [inoutFilter, setInoutFilter] = useState({
    fromDate: '',
    toDate: '',
    warehouseId: '',
    productId: ''
  });
  const [inoutPage, setInoutPage] = useState(1);
  const inoutPageSize = 20;

  const [inoutReport, setInoutReport] = useState<InventoryInOutDto[] | null>(null);
  const [inoutLoading, setInoutLoading] = useState(false);
  const [inoutError, setInoutError] = useState('');

  const fetchStocks = async () => {
    setStockLoading(true);
    setStockError('');
    try {
      const params = new URLSearchParams();
      if (stockFilter.warehouseId) params.append('warehouseId', stockFilter.warehouseId);
      if (stockFilter.productId) params.append('productId', stockFilter.productId);
      if (stockFilter.keyword) params.append('keyword', stockFilter.keyword);

      const res = await apiClient.get('/api/InventoryStocks/current?' + params.toString());
      setStocks(res.data);
    } catch {
      setStockError('Lỗi khi tải dữ liệu tồn kho.');
    } finally {
      setStockLoading(false);
    }
  };

  const fetchHistory = async () => {
    setHistoryLoading(true);
    setHistoryError('');
    try {
      const params = new URLSearchParams();
      if (historyFilter.fromDate) params.append('fromDate', historyFilter.fromDate);
      if (historyFilter.toDate) params.append('toDate', historyFilter.toDate);
      if (historyFilter.transactionType) params.append('transactionType', historyFilter.transactionType);
      if (historyFilter.warehouseId) params.append('warehouseId', historyFilter.warehouseId);
      if (historyFilter.productId) params.append('productId', historyFilter.productId);
      if (historyFilter.keyword) params.append('keyword', historyFilter.keyword);
      params.append('page', historyFilter.page.toString());
      params.append('pageSize', historyFilter.pageSize.toString());

      const res = await apiClient.get('/api/InventoryTransactions?' + params.toString());
      setHistory(res.data);
    } catch {
      setHistoryError('Lỗi khi tải lịch sử giao dịch.');
    } finally {
      setHistoryLoading(false);
    }
  };

  const fetchInOut = async () => {
    if (inoutFilter.fromDate && inoutFilter.toDate && new Date(inoutFilter.fromDate) > new Date(inoutFilter.toDate)) {
      setInoutError('Từ ngày không được lớn hơn Đến ngày');
      setInoutReport(null);
      return;
    }

    setInoutLoading(true);
    setInoutError('');
    try {
      const params = new URLSearchParams();
      if (inoutFilter.fromDate) params.append('fromDate', inoutFilter.fromDate);
      if (inoutFilter.toDate) params.append('toDate', inoutFilter.toDate);
      if (inoutFilter.warehouseId) params.append('warehouseId', inoutFilter.warehouseId);
      if (inoutFilter.productId) params.append('productId', inoutFilter.productId);

      const res = await apiClient.get('/api/Reports/inventory-in-out-stock?' + params.toString());
      setInoutReport(res.data);
    } catch {
      setInoutError('Lỗi khi tải báo cáo xuất nhập tồn.');
      setInoutReport(null);
    } finally {
      setInoutLoading(false);
    }
  };

  useEffect(() => {
    if (activeTab === 'stock') {
      fetchStocks();
    } else if (activeTab === 'history') {
      fetchHistory();
    } else if (activeTab === 'inout') {
      if (!inoutReport && !inoutError) {
        fetchInOut();
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, historyFilter.page]); 

  const handleStockFilterSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setStockPage(1);
    fetchStocks();
  };

  const handleHistoryFilterSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (historyFilter.page !== 1) {
      setHistoryFilter(prev => ({ ...prev, page: 1 }));
    } else {
      fetchHistory();
    }
  };

  const handleInOutFilterSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setInoutPage(1);
    fetchInOut();
  };

  const handleExportStock = async () => {
    try {
      const params = new URLSearchParams();
      if (stockFilter.warehouseId) params.append('warehouseId', stockFilter.warehouseId);
      if (stockFilter.productId) params.append('productId', stockFilter.productId);
      
      const res = await apiClient.get('/api/Reports/inventory/export?' + params.toString(), { responseType: 'blob' });
      const url = window.URL.createObjectURL(new Blob([res.data]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `BaoCaoTonKho.xlsx`);
      document.body.appendChild(link);
      link.click();
      link.remove();
    } catch {
      alert('Lỗi khi xuất Excel tồn kho');
    }
  };

  const handleExportInOut = async () => {
    if (inoutFilter.fromDate && inoutFilter.toDate && new Date(inoutFilter.fromDate) > new Date(inoutFilter.toDate)) {
      alert('Từ ngày không được lớn hơn Đến ngày');
      return;
    }
    try {
      const params = new URLSearchParams();
      if (inoutFilter.fromDate) params.append('fromDate', inoutFilter.fromDate);
      if (inoutFilter.toDate) params.append('toDate', inoutFilter.toDate);
      if (inoutFilter.warehouseId) params.append('warehouseId', inoutFilter.warehouseId);
      if (inoutFilter.productId) params.append('productId', inoutFilter.productId);
      
      const res = await apiClient.get('/api/Reports/inventory-in-out-stock/export?' + params.toString(), { responseType: 'blob' });
      const url = window.URL.createObjectURL(new Blob([res.data]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `BaoCaoXuatNhapTon.xlsx`);
      document.body.appendChild(link);
      link.click();
      link.remove();
    } catch {
      alert('Lỗi khi xuất Excel xuất nhập tồn');
    }
  };

  const renderStockTab = () => (
    <UiCard title="Tồn kho hiện tại">
      <form onSubmit={handleStockFilterSubmit}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select aria-label="Kho tồn hiện tại" value={stockFilter.warehouseId} onChange={e => setStockFilter({ ...stockFilter, warehouseId: e.target.value })}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
            </select>
          </UiToolbarField>

          <UiToolbarField label="ID Sản phẩm">
            <input
              aria-label="ID sản phẩm tồn hiện tại"
              type="number"
              value={stockFilter.productId}
              onChange={e => setStockFilter({ ...stockFilter, productId: e.target.value })}
              placeholder="Nhập ID sản phẩm..."
            />
          </UiToolbarField>

          <UiToolbarField label="Từ khóa SP">
            <input
              aria-label="Từ khóa sản phẩm tồn hiện tại"
              type="text"
              value={stockFilter.keyword}
              onChange={e => setStockFilter({ ...stockFilter, keyword: e.target.value })}
              placeholder="Mã hoặc tên SP..."
            />
          </UiToolbarField>

          <div className="inventory-toolbar-actions">
            <button type="submit">Lọc Tồn Kho</button>
            <button type="button" onClick={handleExportStock}>Xuất Excel Tồn Kho</button>
          </div>
        </UiToolbar>
      </form>

      {stockError && <p role="alert">{stockError}</p>}
      {stockLoading ? (
        <p role="status">Đang tải...</p>
      ) : (
        <UiTableScroll>
          <table aria-label="Tồn kho hiện tại" className="inventory-table-wide">
            <thead>
              <tr>
                <th>Mã SP</th>
                <th>Tên SP</th>
                <th>ĐVT</th>
                <th>Kho</th>
                <th className="inventory-numeric">Tồn thực tế</th>
                <th className="inventory-numeric">Đã giữ</th>
                <th className="inventory-numeric">Khả dụng</th>
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
                    <td>{item.unitName}</td>
                    <td>{item.warehouseName}</td>
                    <td className="inventory-numeric">{item.onHandQuantity}</td>
                    <td className="inventory-numeric">{item.reservedQuantity}</td>
                    <td className="inventory-numeric inventory-available">{item.availableQuantity}</td>
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
          <button type="button" disabled={currentStockPage <= 1} onClick={() => setStockPage(prev => Math.max(prev - 1, 1))}>Trang trước</button>
          <span>Trang {currentStockPage} / {stockTotalPages} (Tổng: {stocks.length})</span>
          <button type="button" disabled={currentStockPage >= stockTotalPages} onClick={() => setStockPage(prev => Math.min(prev + 1, stockTotalPages))}>Trang sau</button>
        </div>
      )}
    </UiCard>
  );

  const renderHistoryTab = () => (
    <UiCard title="Lịch sử giao dịch">
      <form onSubmit={handleHistoryFilterSubmit}>
        <UiToolbar>
          <UiToolbarField label="Từ ngày">
            <input aria-label="Lịch sử từ ngày" type="date" value={historyFilter.fromDate} onChange={e => setHistoryFilter({ ...historyFilter, fromDate: e.target.value })} />
          </UiToolbarField>

          <UiToolbarField label="Đến ngày">
            <input aria-label="Lịch sử đến ngày" type="date" value={historyFilter.toDate} onChange={e => setHistoryFilter({ ...historyFilter, toDate: e.target.value })} />
          </UiToolbarField>

          <UiToolbarField label="Loại GD">
            <select aria-label="Loại giao dịch" value={historyFilter.transactionType} onChange={e => setHistoryFilter({ ...historyFilter, transactionType: e.target.value })}>
              <option value="">-- Tất cả --</option>
              <option value="Import">Nhập kho (Import)</option>
              <option value="Export">Xuất kho (Export)</option>
              <option value="AdjustmentIncrease">Điều chỉnh Tăng</option>
              <option value="AdjustmentDecrease">Điều chỉnh Giảm</option>
            </select>
          </UiToolbarField>

          <UiToolbarField label="Kho">
            <select aria-label="Kho lịch sử giao dịch" value={historyFilter.warehouseId} onChange={e => setHistoryFilter({ ...historyFilter, warehouseId: e.target.value })}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
            </select>
          </UiToolbarField>

          <UiToolbarField label="Từ khóa SP">
            <input aria-label="Từ khóa sản phẩm lịch sử" type="text" value={historyFilter.keyword} onChange={e => setHistoryFilter({ ...historyFilter, keyword: e.target.value })} />
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
            <table aria-label="Lịch sử giao dịch tồn kho">
              <thead>
                <tr>
                  <th>Ngày GD</th>
                  <th>Loại GD</th>
                  <th>Kho</th>
                  <th>Sản phẩm</th>
                  <th>Status</th>
                  <th>Lot / Serial</th>
                  <th className="inventory-numeric">SL</th>
                  <th>Tham chiếu</th>
                  <th>Ghi chú</th>
                </tr>
              </thead>
              <tbody>
                {(!history || history.items.length === 0) ? (
                  <tr><td colSpan={9} className="ui-empty-cell">Không có lịch sử giao dịch</td></tr>
                ) : (
                  history.items.map(item => {
                    const increase = item.transactionType === 'Import' || item.transactionType === 'AdjustmentIncrease';
                    return (
                      <tr key={item.id}>
                        <td>{new Date(item.transactionDate).toLocaleString('vi-VN')}</td>
                        <td>{item.transactionType}</td>
                        <td>{item.warehouseName}</td>
                        <td>{item.productCode} - {item.productName}</td>
                        <td>
                          {item.fromInventoryStatus && item.toInventoryStatus
                            ? `${item.fromInventoryStatus} → ${item.toInventoryStatus}`
                            : item.inventoryStatus || '—'}
                        </td>
                        <td>{item.lotNumber || '—'}{item.serialNumber ? ` / ${item.serialNumber}` : ''}</td>
                        <td className={'inventory-numeric ' + (increase ? 'inventory-qty-increase' : 'inventory-qty-decrease')}>
                          {item.quantity} {item.unitName}
                        </td>
                        <td>{item.referenceType ? '[' + item.referenceType + '] #' + item.referenceId : ''}</td>
                        <td>{item.note}</td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </UiTableScroll>

          {history && history.totalPages > 1 && (
            <div className="ui-pagination" aria-label="Phân trang lịch sử tồn kho">
              <button type="button" disabled={history.pageIndex <= 1} onClick={() => setHistoryFilter(prev => ({ ...prev, page: prev.page - 1 }))}>Trang trước</button>
              <span>Trang {history.pageIndex} / {history.totalPages} (Tổng: {history.totalRecords})</span>
              <button type="button" disabled={history.pageIndex >= history.totalPages} onClick={() => setHistoryFilter(prev => ({ ...prev, page: prev.page + 1 }))}>Trang sau</button>
            </div>
          )}
        </>
      )}
    </UiCard>
  );

  const renderInOutTab = () => {
    const inoutTotalPages = inoutReport ? Math.ceil(inoutReport.length / inoutPageSize) : 0;
    const safeInoutPage = inoutTotalPages > 0 ? Math.min(inoutPage, inoutTotalPages) : 1;
    const inoutCurrentPageData = inoutReport ? inoutReport.slice((safeInoutPage - 1) * inoutPageSize, safeInoutPage * inoutPageSize) : [];

    return (
      <UiCard title="Báo cáo Xuất Nhập Tồn">
        <div className="inventory-report-card">
          <form onSubmit={handleInOutFilterSubmit}>
            <UiToolbar>
              <UiToolbarField label="Từ ngày">
                <input aria-label="Báo cáo từ ngày" type="date" value={inoutFilter.fromDate} onChange={e => setInoutFilter({ ...inoutFilter, fromDate: e.target.value })} />
              </UiToolbarField>

              <UiToolbarField label="Đến ngày">
                <input aria-label="Báo cáo đến ngày" type="date" value={inoutFilter.toDate} onChange={e => setInoutFilter({ ...inoutFilter, toDate: e.target.value })} />
              </UiToolbarField>

              <UiToolbarField label="Kho">
                <select aria-label="Kho báo cáo xuất nhập tồn" value={inoutFilter.warehouseId} onChange={e => setInoutFilter({ ...inoutFilter, warehouseId: e.target.value })}>
                  <option value="">Tất cả kho được phép</option>
                  {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
                </select>
              </UiToolbarField>

              <UiToolbarField label="ID Sản phẩm">
                <input aria-label="ID sản phẩm báo cáo xuất nhập tồn" type="number" value={inoutFilter.productId} onChange={e => setInoutFilter({ ...inoutFilter, productId: e.target.value })} placeholder="Nhập ID sản phẩm..." />
              </UiToolbarField>

              <div className="inventory-toolbar-actions">
                <button type="submit">Lọc Báo Cáo</button>
                <button type="button" onClick={handleExportInOut}>Xuất Excel</button>
              </div>
            </UiToolbar>
          </form>

          {inoutError && <p role="alert">{inoutError}</p>}
          {inoutLoading ? (
            <p role="status">Đang tải...</p>
          ) : (
            <UiTableScroll>
              <table aria-label="Báo cáo xuất nhập tồn">
                <thead>
                  <tr>
                    <th>Mã SP</th>
                    <th>Tên SP</th>
                    <th>ĐVT</th>
                    <th>Kho</th>
                    <th className="inventory-numeric">Tồn đầu kỳ</th>
                    <th className="inventory-numeric">Nhập trong kỳ</th>
                    <th className="inventory-numeric">Xuất trong kỳ</th>
                    <th className="inventory-numeric">Tồn cuối kỳ</th>
                  </tr>
                </thead>
                <tbody>
                  {(!inoutReport || inoutReport.length === 0) ? (
                    <tr><td colSpan={8} className="ui-empty-cell">Không có dữ liệu báo cáo</td></tr>
                  ) : (
                    inoutCurrentPageData.map((item, index) => (
                      <tr key={item.productId + '-' + item.warehouseId + '-' + index}>
                        <td><strong>{item.productCode}</strong></td>
                        <td>{item.productName}</td>
                        <td>{item.unitName}</td>
                        <td>{item.warehouseName}</td>
                        <td className="inventory-numeric">{item.openingQuantity}</td>
                        <td className="inventory-numeric">{item.inQuantity}</td>
                        <td className="inventory-numeric">{item.outQuantity}</td>
                        <td className="inventory-numeric inventory-available">{item.closingQuantity}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </UiTableScroll>
          )}

          {inoutReport && inoutTotalPages > 1 && (
            <div className="ui-pagination" aria-label="Phân trang báo cáo xuất nhập tồn">
              <button type="button" disabled={safeInoutPage <= 1} onClick={() => setInoutPage(prev => Math.max(prev - 1, 1))}>Trang trước</button>
              <span>Trang {safeInoutPage} / {inoutTotalPages} (Tổng: {inoutReport.length})</span>
              <button type="button" disabled={safeInoutPage >= inoutTotalPages} onClick={() => setInoutPage(prev => Math.min(prev + 1, inoutTotalPages))}>Trang sau</button>
            </div>
          )}
        </div>
      </UiCard>
    );
  };

  return (
    <UiPage>
      <div className="inventory-page">
        <UiPageHeader
          eyebrow="Inventory"
          title="Báo Cáo Tồn Kho"
          description="Theo dõi tồn thực tế, bucket theo Status/Lot/Serial, eligibility, ledger movement và báo cáo xuất nhập tồn theo phạm vi kho được phép."
        />

        <div className="inventory-tabs" role="tablist" aria-label="Chế độ báo cáo tồn kho">
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'stock'}
            aria-controls="inventory-stock-panel"
            onClick={() => setActiveTab('stock')}
          >
            Tồn kho hiện tại
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'buckets'}
            aria-controls="inventory-buckets-panel"
            onClick={() => setActiveTab('buckets')}
          >
            Bucket / Lot / Serial
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'history'}
            aria-controls="inventory-history-panel"
            onClick={() => setActiveTab('history')}
          >
            Lịch sử giao dịch
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'inout'}
            aria-controls="inventory-inout-panel"
            onClick={() => setActiveTab('inout')}
          >
            Xuất nhập tồn
          </button>
        </div>

        {activeTab === 'stock' && <section id="inventory-stock-panel" role="tabpanel">{renderStockTab()}</section>}
        {activeTab === 'buckets' && <section id="inventory-buckets-panel" role="tabpanel"><InventoryBuckets warehouses={warehouses} /></section>}
        {activeTab === 'history' && <section id="inventory-history-panel" role="tabpanel">{renderHistoryTab()}</section>}
        {activeTab === 'inout' && <section id="inventory-inout-panel" role="tabpanel">{renderInOutTab()}</section>}
      </div>
    </UiPage>
  );
};

export default Inventory;
