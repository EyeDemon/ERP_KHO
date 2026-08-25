import React, { useState, useEffect } from 'react';
import apiClient from '../services/apiClient';

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
  const [activeTab, setActiveTab] = useState<'stock' | 'history' | 'inout'>('stock');
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
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <form onSubmit={handleStockFilterSubmit} style={{ display: 'flex', gap: '10px', marginBottom: '20px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div>
            <label style={{ display: 'block' }}>ID Kho</label>
            <select value={stockFilter.warehouseId} onChange={e => setStockFilter({...stockFilter, warehouseId: e.target.value})}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
            </select>
          </div>
          <div>
            <label style={{ display: 'block' }}>ID Sản phẩm</label>
            <input type="number" value={stockFilter.productId} onChange={e => setStockFilter({...stockFilter, productId: e.target.value})} placeholder="Nhập ID sản phẩm..." />
          </div>
          <div>
            <label style={{ display: 'block' }}>Từ khóa SP</label>
            <input type="text" value={stockFilter.keyword} onChange={e => setStockFilter({...stockFilter, keyword: e.target.value})} placeholder="Mã hoặc tên SP..." />
          </div>
          <button type="submit" style={{ padding: '5px 10px', cursor: 'pointer' }}>Lọc Tồn Kho</button>
        </form>
        <button onClick={handleExportStock} style={{ padding: '5px 15px', backgroundColor: '#27ae60', color: 'white', border: 'none', cursor: 'pointer', borderRadius: '4px' }}>
          Xuất Excel Tồn Kho
        </button>
      </div>

      {stockError && <div style={{ color: 'red', marginBottom: '10px' }}>{stockError}</div>}
      {stockLoading ? <div>Đang tải...</div> : (
        <div style={{ overflowX: 'auto', maxWidth: '100%' }}><table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '900px' }}>
          <thead>
            <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã SP</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên SP</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>ĐVT</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Kho</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tồn thực tế</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Đã giữ</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Khả dụng</th>
              <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Cập nhật lần cuối</th>
            </tr>
          </thead>
          <tbody>
            {paginatedStocks.length === 0 ? (
              <tr><td colSpan={8} style={{ textAlign: 'center', padding: '10px' }}>Không có dữ liệu tồn kho</td></tr>
            ) : (
              paginatedStocks.map((item, index) => (
                <tr key={`${item.productId}-${item.warehouseId}-${index}`}>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{item.productCode}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{item.productName}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{item.unitName}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{item.warehouseName}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{item.onHandQuantity}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{item.reservedQuantity}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', fontWeight: 700 }}>{item.availableQuantity}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{new Date(item.lastUpdated).toLocaleString()}</td>
                </tr>
              ))
            )}
          </tbody>
        </table></div>
      )}
      {stocks.length > 0 && stockTotalPages > 1 && (
        <div style={{ marginTop: '15px', display: 'flex', gap: '10px', alignItems: 'center' }}>
          <button 
            disabled={currentStockPage <= 1} 
            onClick={() => setStockPage(prev => Math.max(prev - 1, 1))}
            style={{ cursor: currentStockPage <= 1 ? 'not-allowed' : 'pointer' }}>
            Trang trước
          </button>
          <span>Trang {currentStockPage} / {stockTotalPages} (Tổng: {stocks.length})</span>
          <button 
            disabled={currentStockPage >= stockTotalPages} 
            onClick={() => setStockPage(prev => Math.min(prev + 1, stockTotalPages))}
            style={{ cursor: currentStockPage >= stockTotalPages ? 'not-allowed' : 'pointer' }}>
            Trang sau
          </button>
        </div>
      )}
    </div>
  );

  const renderHistoryTab = () => (
    <div>
      <form onSubmit={handleHistoryFilterSubmit} style={{ display: 'flex', gap: '10px', marginBottom: '20px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <div>
          <label style={{ display: 'block' }}>Từ ngày</label>
          <input type="date" value={historyFilter.fromDate} onChange={e => setHistoryFilter({...historyFilter, fromDate: e.target.value})} />
        </div>
        <div>
          <label style={{ display: 'block' }}>Đến ngày</label>
          <input type="date" value={historyFilter.toDate} onChange={e => setHistoryFilter({...historyFilter, toDate: e.target.value})} />
        </div>
        <div>
          <label style={{ display: 'block' }}>Loại GD</label>
          <select value={historyFilter.transactionType} onChange={e => setHistoryFilter({...historyFilter, transactionType: e.target.value})}>
            <option value="">-- Tất cả --</option>
            <option value="Import">Nhập kho (Import)</option>
            <option value="Export">Xuất kho (Export)</option>
            <option value="AdjustmentIncrease">Điều chỉnh Tăng</option>
            <option value="AdjustmentDecrease">Điều chỉnh Giảm</option>
          </select>
        </div>
        <div>
          <label style={{ display: 'block' }}>Kho (ID)</label>
          <select value={historyFilter.warehouseId} onChange={e => setHistoryFilter({...historyFilter, warehouseId: e.target.value})}>
            <option value="">Tất cả kho được phép</option>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
          </select>
        </div>
        <div>
          <label style={{ display: 'block' }}>Từ khóa SP</label>
          <input style={{ width: '120px' }} type="text" value={historyFilter.keyword} onChange={e => setHistoryFilter({...historyFilter, keyword: e.target.value})} />
        </div>
        <button type="submit" style={{ padding: '5px 10px', cursor: 'pointer' }}>Lọc Lịch Sử</button>
      </form>

      {historyError && <div style={{ color: 'red', marginBottom: '10px' }}>{historyError}</div>}
      {historyLoading && !history ? <div>Đang tải...</div> : (
        <>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '14px' }}>
            <thead>
              <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Ngày GD</th>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Loại GD</th>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Kho</th>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Sản phẩm</th>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>SL</th>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Tham chiếu</th>
                <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {(!history || history.items.length === 0) ? (
                <tr><td colSpan={7} style={{ textAlign: 'center', padding: '10px' }}>Không có lịch sử giao dịch</td></tr>
              ) : (
                history.items.map(item => (
                  <tr key={item.id}>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{new Date(item.transactionDate).toLocaleString()}</td>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.transactionType}</td>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.warehouseName}</td>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.productCode} - {item.productName}</td>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7', fontWeight: 'bold', color: (item.transactionType === 'Import' || item.transactionType === 'AdjustmentIncrease') ? 'green' : 'red' }}>
                      {item.quantity} {item.unitName}
                    </td>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.referenceType ? `[${item.referenceType}] #${item.referenceId}` : ''}</td>
                    <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.note}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
          {history && history.totalPages > 1 && (
            <div style={{ marginTop: '15px', display: 'flex', gap: '10px', alignItems: 'center' }}>
              <button 
                disabled={history.pageIndex <= 1} 
                onClick={() => setHistoryFilter(prev => ({ ...prev, page: prev.page - 1 }))}
                style={{ cursor: 'pointer' }}>
                Trang trước
              </button>
              <span>Trang {history.pageIndex} / {history.totalPages} (Tổng: {history.totalRecords})</span>
              <button 
                disabled={history.pageIndex >= history.totalPages} 
                onClick={() => setHistoryFilter(prev => ({ ...prev, page: prev.page + 1 }))}
                style={{ cursor: 'pointer' }}>
                Trang sau
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );

  const renderInOutTab = () => {
    const inoutTotalPages = inoutReport ? Math.ceil(inoutReport.length / inoutPageSize) : 0;
    const safeInoutPage = inoutTotalPages > 0 ? Math.min(inoutPage, inoutTotalPages) : 1;
    const inoutCurrentPageData = inoutReport ? inoutReport.slice((safeInoutPage - 1) * inoutPageSize, safeInoutPage * inoutPageSize) : [];
    
    return (
      <div>
        <div style={{ marginBottom: '20px', padding: '15px', border: '1px solid #ccc', borderRadius: '5px' }}>
          <h3>Báo cáo Xuất Nhập Tồn</h3>
          <form onSubmit={handleInOutFilterSubmit} style={{ display: 'flex', gap: '10px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
            <div>
              <label style={{ display: 'block' }}>Từ ngày</label>
              <input type="date" value={inoutFilter.fromDate} onChange={e => setInoutFilter({...inoutFilter, fromDate: e.target.value})} />
            </div>
            <div>
              <label style={{ display: 'block' }}>Đến ngày</label>
              <input type="date" value={inoutFilter.toDate} onChange={e => setInoutFilter({...inoutFilter, toDate: e.target.value})} />
            </div>
            <div>
              <label style={{ display: 'block' }}>ID Kho</label>
              <select value={inoutFilter.warehouseId} onChange={e => setInoutFilter({...inoutFilter, warehouseId: e.target.value})}>
                <option value="">Tất cả kho được phép</option>
                {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
              </select>
            </div>
            <div>
              <label style={{ display: 'block' }}>ID Sản phẩm</label>
              <input type="number" value={inoutFilter.productId} onChange={e => setInoutFilter({...inoutFilter, productId: e.target.value})} placeholder="Nhập ID sản phẩm..." />
            </div>
            <button type="submit" style={{ padding: '5px 15px', backgroundColor: '#3498db', color: 'white', border: 'none', cursor: 'pointer', borderRadius: '4px' }}>
              Lọc Báo Cáo
            </button>
            <button type="button" onClick={handleExportInOut} style={{ padding: '5px 15px', backgroundColor: '#27ae60', color: 'white', border: 'none', cursor: 'pointer', borderRadius: '4px' }}>
              Xuất Excel
            </button>
          </form>
        </div>

        {inoutError && <div style={{ color: 'red', marginBottom: '10px' }}>{inoutError}</div>}
        {inoutLoading ? <div>Đang tải...</div> : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '14px' }}>
              <thead>
                <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Mã SP</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Tên SP</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>ĐVT</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Kho</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Tồn đầu kỳ</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Nhập trong kỳ</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Xuất trong kỳ</th>
                  <th style={{ padding: '8px', border: '1px solid #bdc3c7' }}>Tồn cuối kỳ</th>
                </tr>
              </thead>
              <tbody>
                {(!inoutReport || inoutReport.length === 0) ? (
                  <tr><td colSpan={8} style={{ textAlign: 'center', padding: '10px' }}>Không có dữ liệu báo cáo</td></tr>
                ) : (
                  inoutCurrentPageData.map((item, index) => (
                    <tr key={`${item.productId}-${item.warehouseId}-${index}`}>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.productCode}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.productName}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.unitName}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.warehouseName}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.openingQuantity}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.inQuantity}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.outQuantity}</td>
                      <td style={{ padding: '8px', border: '1px solid #bdc3c7' }}>{item.closingQuantity}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
            {inoutReport && inoutTotalPages > 1 && (
              <div style={{ marginTop: '15px', display: 'flex', gap: '10px', alignItems: 'center' }}>
                <button 
                  disabled={safeInoutPage <= 1} 
                  onClick={() => setInoutPage(prev => Math.max(prev - 1, 1))}
                  style={{ cursor: safeInoutPage <= 1 ? 'not-allowed' : 'pointer' }}>
                  Trang trước
                </button>
                <span>Trang {safeInoutPage} / {inoutTotalPages} (Tổng: {inoutReport.length})</span>
                <button 
                  disabled={safeInoutPage >= inoutTotalPages} 
                  onClick={() => setInoutPage(prev => Math.min(prev + 1, inoutTotalPages))}
                  style={{ cursor: safeInoutPage >= inoutTotalPages ? 'not-allowed' : 'pointer' }}>
                  Trang sau
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    );
  };

  return (
    <div style={{ padding: '20px' }}>
      <h2>Báo Cáo Tồn Kho</h2>
      <div style={{ display: 'flex', gap: '10px', marginBottom: '20px', borderBottom: '2px solid #ccc', paddingBottom: '10px' }}>
        <button 
          style={{ padding: '10px 20px', cursor: 'pointer', fontWeight: activeTab === 'stock' ? 'bold' : 'normal', backgroundColor: activeTab === 'stock' ? '#3498db' : '#ecf0f1', color: activeTab === 'stock' ? 'white' : 'black', border: 'none', borderRadius: '4px' }}
          onClick={() => setActiveTab('stock')}
        >
          Tồn kho hiện tại
        </button>
        <button 
          style={{ padding: '10px 20px', cursor: 'pointer', fontWeight: activeTab === 'history' ? 'bold' : 'normal', backgroundColor: activeTab === 'history' ? '#3498db' : '#ecf0f1', color: activeTab === 'history' ? 'white' : 'black', border: 'none', borderRadius: '4px' }}
          onClick={() => setActiveTab('history')}
        >
          Lịch sử giao dịch
        </button>
        <button 
          style={{ padding: '10px 20px', cursor: 'pointer', fontWeight: activeTab === 'inout' ? 'bold' : 'normal', backgroundColor: activeTab === 'inout' ? '#3498db' : '#ecf0f1', color: activeTab === 'inout' ? 'white' : 'black', border: 'none', borderRadius: '4px' }}
          onClick={() => setActiveTab('inout')}
        >
          Xuất nhập tồn
        </button>
      </div>

      {activeTab === 'stock' && renderStockTab()}
      {activeTab === 'history' && renderHistoryTab()}
      {activeTab === 'inout' && renderInOutTab()}
    </div>
  );
};

export default Inventory;
