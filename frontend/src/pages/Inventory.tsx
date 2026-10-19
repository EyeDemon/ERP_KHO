import React, { useState, useEffect } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission } from '../services/authorization';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';
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

interface InventoryExpiryAgingDto {
  inventoryStockId: number;
  warehouseId: number;
  warehouseName: string;
  locationId?: number | null;
  locationCode: string;
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  status: string;
  lotId?: number | null;
  lotNumber?: string | null;
  manufactureDate?: string | null;
  expiryDate?: string | null;
  quantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  daysToExpiry?: number | null;
  ageDays: number;
  expiryStatus: 'EXPIRED' | 'NEAR_EXPIRY' | 'GOOD' | 'NO_EXPIRY';
}

interface InventoryAnalyticsSkuItemDto {
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  onHandQuantity: number;
  periodOutQuantity: number;
  periodInQuantity: number;
  turnoverRate: number;
  daysOnHand?: number | null;
  velocityClass: string;
}

interface InventoryAnalyticsSummaryDto {
  windowDays: number;
  inboundOutboundSla: {
    completedReceiptsCount: number;
    avgInboundLeadTimeHours: number;
    openPutawayBacklogCount: number;
    dispatchedExportsCount: number;
    avgOutboundLeadTimeHours: number;
    openPickingBacklogCount: number;
  };
  stocktakeAccuracy: {
    approvedStocktakesCount: number;
    totalLinesCounted: number;
    exactMatchLinesCount: number;
    varianceLinesCount: number;
    accuracyRatePercent: number;
    totalAbsoluteVarianceQty: number;
  };
  turnoverAndDaysOnHand: InventoryAnalyticsSkuItemDto[];
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

const formatUnitName = (unit?: string | null) =>
  !unit || unit === 'QA_UNIT' || unit === 'QA-PCS' ? 'THÙNG' : unit;

const referenceTypeLabel = (value?: string | null) => ({
  ImportReceipt: 'Phiếu nhập kho',
  GoodsReceipt: 'Phiếu nhập kho',
  Receipt: 'Phiếu nhập kho',
  ExportReceipt: 'Phiếu xuất kho',
  StockTransfer: 'Phiếu điều chuyển',
  Stocktake: 'Phiếu kiểm kê',
  Shipment: 'Chuyến giao hàng',
  InventoryReversal: 'Phiếu đảo giao dịch',
  InventoryAdjustment: 'Phiếu điều chỉnh',
  InventoryMove: 'Phiếu di chuyển',
  Return: 'Phiếu trả hàng',
  PickTask: 'Tác vụ lấy hàng',
}[value ?? ''] ?? value ?? '');

const transactionTypeLabel = (value: string) => ({
  Import: 'Nhập kho',
  Export: 'Xuất kho',
  AdjustmentIncrease: 'Điều chỉnh tăng',
  AdjustmentDecrease: 'Điều chỉnh giảm',
  TransferIn: 'Nhập điều chuyển',
  TransferOut: 'Xuất điều chuyển',
  TransferAdjustment: 'Điều chỉnh điều chuyển',
  StatusChange: 'Đổi trạng thái',
  Move: 'Di chuyển vị trí nội bộ',
  Reversal: 'Dấu mốc đảo giao dịch',
  Ship: 'Xuất giao hàng',
}[value] ?? value);

const inventoryStatusLabel = (value: string) => ({
  AVAILABLE: 'Khả dụng',
  QC_HOLD: 'Chờ kiểm tra chất lượng',
  QUARANTINE: 'Cách ly',
  BLOCKED: 'Bị chặn',
  DAMAGED: 'Hư hỏng',
  REJECTED: 'Từ chối',
  EXPIRED: 'Hết hạn',
  RECALL_BLOCKED: 'Khóa thu hồi',
}[value] ?? value);

const Inventory = () => {
  const [activeTab, setActiveTab] = useState<'stock' | 'buckets' | 'history' | 'inout' | 'expiry' | 'analytics'>('stock');
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
    if (!hasPermission('warehouse.read')) {
      setWarehouses([]);
      return;
    }
    apiClient.get('/api/warehouses')
      .then(response => setWarehouses(response.data.filter((warehouse: WarehouseOption) => warehouse.isActive)))
      .catch(() => {
        setWarehouses([]);
        setStockError('Không thể tải danh sách kho được phân quyền.');
      });
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

  // Expiry & Aging State
  const [expiryFilter, setExpiryFilter] = useState({
    warehouseId: '',
    productId: '',
    nearExpiryDays: '30',
    onlyLots: false
  });
  const [expiryReport, setExpiryReport] = useState<InventoryExpiryAgingDto[]>([]);
  const [expiryLoading, setExpiryLoading] = useState(false);
  const [expiryError, setExpiryError] = useState('');

  // Analytics Summary State (RP-03, RP-05, RP-09)
  const [analyticsFilter, setAnalyticsFilter] = useState({
    warehouseId: '',
    days: '30'
  });
  const [analyticsReport, setAnalyticsReport] = useState<InventoryAnalyticsSummaryDto | null>(null);
  const [analyticsLoading, setAnalyticsLoading] = useState(false);
  const [analyticsError, setAnalyticsError] = useState('');
  const [whatIfMultiplier, setWhatIfMultiplier] = useState<number>(1.5);
  const [whatIfResult, setWhatIfResult] = useState<{
    demandMultiplier: number;
    totalAvailable: number;
    simulatedDemand: number;
    projectedNetBalance: number;
    scenarioStatus: string;
    immutableSnapshotGuarantee: boolean;
  } | null>(null);
  const [decisionPolicyAudit, setDecisionPolicyAudit] = useState<{
    policyRegistryId: string;
    forecastBiasMapeStatus: string;
    demandAnomalyDetector: string;
    inventoryRiskScoreStatus: string;
    humanInTheLoopBoundary: string;
    status: string;
  } | null>(null);
  const [governanceBusy, setGovernanceBusy] = useState(false);

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

  const fetchExpiryAging = async () => {
    setExpiryLoading(true);
    setExpiryError('');
    try {
      const params = new URLSearchParams();
      if (expiryFilter.warehouseId) params.append('warehouseId', expiryFilter.warehouseId);
      if (expiryFilter.productId) params.append('productId', expiryFilter.productId);
      if (expiryFilter.nearExpiryDays) params.append('nearExpiryDays', expiryFilter.nearExpiryDays);
      if (expiryFilter.onlyLots) params.append('onlyLots', 'true');

      const res = await apiClient.get('/api/Reports/expiry-aging?' + params.toString());
      setExpiryReport(Array.isArray(res.data) ? res.data : []);
    } catch {
      setExpiryError('Lỗi khi tải báo cáo hạn dùng và tuổi tồn.');
      setExpiryReport([]);
    } finally {
      setExpiryLoading(false);
    }
  };

  const fetchAnalyticsSummary = async () => {
    setAnalyticsLoading(true);
    setAnalyticsError('');
    try {
      const params = new URLSearchParams();
      if (analyticsFilter.warehouseId) params.append('warehouseId', analyticsFilter.warehouseId);
      if (analyticsFilter.days) params.append('days', analyticsFilter.days);

      const res = await apiClient.get('/api/Reports/analytics-summary?' + params.toString());
      const raw = res.data;
      if (!raw) {
        setAnalyticsReport(null);
      } else {
        const parsedDays = Number(raw.windowDays ?? analyticsFilter.days) || 30;
        const rawItems: Array<Record<string, unknown>> = Array.isArray(raw.turnoverAndDaysOnHand)
          ? raw.turnoverAndDaysOnHand
          : Array.isArray(raw.turnoverItems)
            ? raw.turnoverItems
            : [];
        const totalLines = Number(raw.stocktakeAccuracy?.totalLinesCounted ?? raw.stocktakeLineCount ?? 0);
        const exactLines = Number(raw.stocktakeAccuracy?.exactMatchLinesCount ?? raw.stocktakeAccurateLineCount ?? 0);
        setAnalyticsReport({
          windowDays: parsedDays,
          inboundOutboundSla: {
            completedReceiptsCount: Number(raw.inboundOutboundSla?.completedReceiptsCount ?? raw.inboundPostedCount ?? 0),
            avgInboundLeadTimeHours: Number(raw.inboundOutboundSla?.avgInboundLeadTimeHours ?? 0),
            openPutawayBacklogCount: Number(raw.inboundOutboundSla?.openPutawayBacklogCount ?? raw.putawayOpenCount ?? 0),
            dispatchedExportsCount: Number(raw.inboundOutboundSla?.dispatchedExportsCount ?? raw.outboundShippedCount ?? 0),
            avgOutboundLeadTimeHours: Number(raw.inboundOutboundSla?.avgOutboundLeadTimeHours ?? 0),
            openPickingBacklogCount: Number(raw.inboundOutboundSla?.openPickingBacklogCount ?? 0),
          },
          stocktakeAccuracy: {
            approvedStocktakesCount: Number(raw.stocktakeAccuracy?.approvedStocktakesCount ?? raw.stocktakeApprovedCount ?? 0),
            totalLinesCounted: totalLines,
            exactMatchLinesCount: exactLines,
            varianceLinesCount: Number(raw.stocktakeAccuracy?.varianceLinesCount ?? Math.max(0, totalLines - exactLines)),
            accuracyRatePercent: Number(raw.stocktakeAccuracy?.accuracyRatePercent ?? raw.stocktakeAccuracyRatePct ?? 100),
            totalAbsoluteVarianceQty: Number(raw.stocktakeAccuracy?.totalAbsoluteVarianceQty ?? 0),
          },
          turnoverAndDaysOnHand: rawItems.map((item) => ({
            productId: Number(item.productId ?? 0),
            productCode: String(item.productCode ?? ''),
            productName: String(item.productName ?? ''),
            unitName: String(item.unitName ?? 'ĐVT'),
            onHandQuantity: Number(item.onHandQuantity ?? item.currentOnHand ?? 0),
            periodInQuantity: Number(item.periodInQuantity ?? 0),
            periodOutQuantity: Number(item.periodOutQuantity ?? item.issuedLastPeriod ?? 0),
            turnoverRate: Number(item.turnoverRate ?? item.turnoverRatio ?? 0),
            daysOnHand: item.daysOnHand === null || item.daysOnHand === undefined ? null : Number(item.daysOnHand),
            velocityClass: String(item.velocityClass ?? item.movementClass ?? 'SLOW_MOVING'),
          })),
        });
      }
    } catch {
      setAnalyticsError('Lỗi khi tải báo cáo hiệu suất và vòng quay tồn kho.');
      setAnalyticsReport(null);
    } finally {
      setAnalyticsLoading(false);
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
    } else if (activeTab === 'expiry') {
      fetchExpiryAging();
    } else if (activeTab === 'analytics') {
      fetchAnalyticsSummary();
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

  const handleExpiryFilterSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    void fetchExpiryAging();
  };

  const handleAnalyticsFilterSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    void fetchAnalyticsSummary();
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
      setStockError('Lỗi khi xuất Excel tồn kho');
    }
  };

  const handleExportInOut = async () => {
    if (inoutFilter.fromDate && inoutFilter.toDate && new Date(inoutFilter.fromDate) > new Date(inoutFilter.toDate)) {
      setInoutError('Từ ngày không được lớn hơn Đến ngày');
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
      setInoutError('Lỗi khi xuất Excel xuất nhập tồn');
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
                    <td>{formatUnitName(item.unitName)}</td>
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
            <table aria-label="Lịch sử giao dịch tồn kho" className="ui-table-compact">
              <thead>
                <tr>
                  <th>Ngày GD</th>
                  <th>Loại GD</th>
                  <th>Kho</th>
                  <th>Sản phẩm</th>
                  <th>Trạng thái</th>
                  <th>Lô / Sê-ri</th>
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
                    const increase = item.transactionType === 'Import' || item.transactionType === 'AdjustmentIncrease' || item.transactionType === 'TransferIn';
                    const neutral = item.transactionType === 'Move' || item.transactionType === 'StatusChange' || item.transactionType === 'Reversal';
                    const quantityClass = increase ? 'inventory-qty-increase' : neutral ? '' : 'inventory-qty-decrease';
                    return (
                      <tr key={item.id}>
                        <td>{new Date(item.transactionDate).toLocaleString('vi-VN')}</td>
                        <td>{transactionTypeLabel(item.transactionType)}</td>
                        <td>{item.warehouseName}</td>
                        <td>{item.productCode} - {item.productName}</td>
                        <td>
                          {item.fromInventoryStatus && item.toInventoryStatus
                            ? `${inventoryStatusLabel(item.fromInventoryStatus)} → ${inventoryStatusLabel(item.toInventoryStatus)}`
                            : item.inventoryStatus ? inventoryStatusLabel(item.inventoryStatus) : '—'}
                        </td>
                        <td>{item.lotNumber || '—'}{item.serialNumber ? ` / ${item.serialNumber}` : ''}</td>
                        <td className={'inventory-numeric ' + quantityClass}>
                          {item.quantity} {formatUnitName(item.unitName)}
                        </td>
                        <td>{item.referenceType ? '[' + referenceTypeLabel(item.referenceType) + '] #' + item.referenceId : '—'}</td>
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
                        <td>{formatUnitName(item.unitName)}</td>
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

  const renderExpiryTab = () => {
    const expiryBadge = (status: InventoryExpiryAgingDto['expiryStatus'], days?: number | null) => {
      if (status === 'EXPIRED') return <UiBadge tone="danger">Đã hết hạn ({days} ngày)</UiBadge>;
      if (status === 'NEAR_EXPIRY') return <UiBadge tone="warning">Sắp hết hạn ({days} ngày)</UiBadge>;
      if (status === 'GOOD') return <UiBadge tone="success">Còn hạn tốt ({days} ngày)</UiBadge>;
      return <UiBadge tone="neutral">Không có HSD</UiBadge>;
    };

    return (
      <UiCard title="Báo cáo Hạn dùng & Tuổi tồn kho">
        <form onSubmit={handleExpiryFilterSubmit}>
          <UiToolbar>
            <UiToolbarField label="Kho">
              <select
                aria-label="Kho báo cáo hạn dùng"
                value={expiryFilter.warehouseId}
                onChange={e => setExpiryFilter({ ...expiryFilter, warehouseId: e.target.value })}
              >
                <option value="">Tất cả kho được phép</option>
                {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
              </select>
            </UiToolbarField>
            <UiToolbarField label="ID Sản phẩm">
              <input
                aria-label="ID sản phẩm báo cáo hạn dùng"
                type="number"
                value={expiryFilter.productId}
                onChange={e => setExpiryFilter({ ...expiryFilter, productId: e.target.value })}
                placeholder="Nhập ID sản phẩm..."
              />
            </UiToolbarField>
            <UiToolbarField label="Ngưỡng cảnh báo cận hạn (ngày)">
              <input
                aria-label="Ngưỡng cảnh báo cận hạn"
                type="number"
                min={1}
                value={expiryFilter.nearExpiryDays}
                onChange={e => setExpiryFilter({ ...expiryFilter, nearExpiryDays: e.target.value })}
              />
            </UiToolbarField>
            <UiToolbarField label="Phạm vi lô hàng">
              <label className="ui-checkbox-label">
                <input
                  type="checkbox"
                  checked={expiryFilter.onlyLots}
                  onChange={e => setExpiryFilter({ ...expiryFilter, onlyLots: e.target.checked })}
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
                  <th className="inventory-numeric">Tuổi tồn (ngày)</th>
                  <th className="inventory-numeric">Tồn thực tế</th>
                  <th className="inventory-numeric">Khả dụng</th>
                </tr>
              </thead>
              <tbody>
                {expiryReport.length === 0 ? (
                  <tr><td colSpan={10} className="ui-empty-cell">Không có dữ liệu hạn dùng / tuổi tồn</td></tr>
                ) : (
                  expiryReport.map(row => (
                    <tr key={row.inventoryStockId}>
                      <td><strong>{row.warehouseName}</strong><br /><small>{row.locationCode}</small></td>
                      <td><strong>{row.productCode}</strong> — {row.productName}</td>
                      <td>{row.lotNumber || '—'}</td>
                      <td>{inventoryStatusLabel(row.status)}</td>
                      <td>{row.manufactureDate ? new Date(row.manufactureDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>{row.expiryDate ? new Date(row.expiryDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>{expiryBadge(row.expiryStatus, row.daysToExpiry)}</td>
                      <td className="inventory-numeric">{row.ageDays}</td>
                      <td className="inventory-numeric">{row.quantity} {formatUnitName(row.unitName)}</td>
                      <td className="inventory-numeric inventory-available">{row.availableQuantity} {formatUnitName(row.unitName)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    );
  };

  const renderAnalyticsTab = () => {
    const velocityBadge = (cls: string) => {
      if (cls === 'FAST_MOVING') return <UiBadge tone="success">Luân chuyển nhanh</UiBadge>;
      if (cls === 'NORMAL_MOVING' || cls === 'NORMAL') return <UiBadge tone="neutral">Luân chuyển bình thường</UiBadge>;
      if (cls === 'DEAD_STOCK' || cls === 'NON_MOVING') return <UiBadge tone="danger">Tồn đọng / Không xuất</UiBadge>;
      return <UiBadge tone="warning">Luân chuyển chậm</UiBadge>;
    };

    return (
      <UiCard title="Phân tích Hiệu suất Cam kết, Độ chính xác Kiểm kê & Vòng quay Tồn kho">
        <form onSubmit={handleAnalyticsFilterSubmit}>
          <UiToolbar>
            <UiToolbarField label="Kho">
              <select
                aria-label="Kho báo cáo hiệu suất"
                value={analyticsFilter.warehouseId}
                onChange={e => setAnalyticsFilter({ ...analyticsFilter, warehouseId: e.target.value })}
              >
                <option value="">Tất cả kho được phép</option>
                {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>)}
              </select>
            </UiToolbarField>
            <UiToolbarField label="Chu kỳ phân tích (ngày)">
              <input
                aria-label="Chu kỳ phân tích ngày"
                type="number"
                min={7}
                max={365}
                value={analyticsFilter.days}
                onChange={e => setAnalyticsFilter({ ...analyticsFilter, days: e.target.value })}
              />
            </UiToolbarField>
            <div className="inventory-toolbar-actions">
              <button type="submit">Tải phân tích chỉ số</button>
            </div>
          </UiToolbar>
        </form>

        {analyticsError && <p role="alert">{analyticsError}</p>}
        {analyticsLoading || !analyticsReport ? (
          <p role="status">Đang tải dữ liệu phân tích hiệu suất & vòng quay...</p>
        ) : (
          <div className="ui-stack">
            <div className="ui-grid-2">
              <UiCard title={`Hiệu suất Nhập / Xuất (${analyticsReport.windowDays} ngày qua)`}>
                <div className="ui-stack-xs">
                  <div><strong>Phiếu nhập hoàn tất:</strong> {analyticsReport.inboundOutboundSla.completedReceiptsCount} phiếu</div>
                  <div><strong>Thời gian xử lý Nhập kho TB:</strong> {analyticsReport.inboundOutboundSla.avgInboundLeadTimeHours} giờ</div>
                  <div><strong>Tồn đọng chờ cất hàng:</strong> {analyticsReport.inboundOutboundSla.openPutawayBacklogCount} nhiệm vụ</div>
                  <hr />
                  <div><strong>Phiếu xuất đã giao:</strong> {analyticsReport.inboundOutboundSla.dispatchedExportsCount} phiếu</div>
                  <div><strong>Thời gian xử lý Xuất kho TB:</strong> {analyticsReport.inboundOutboundSla.avgOutboundLeadTimeHours} giờ</div>
                  <div><strong>Tồn đọng chờ lấy hàng:</strong> {analyticsReport.inboundOutboundSla.openPickingBacklogCount} nhiệm vụ</div>
                </div>
              </UiCard>

              <UiCard title={`Độ chính xác Kiểm kê & Chênh lệch (${analyticsReport.windowDays} ngày qua)`}>
                <div className="ui-stack-xs">
                  <div><strong>Số đợt kiểm kê đã duyệt:</strong> {analyticsReport.stocktakeAccuracy.approvedStocktakesCount} đợt</div>
                  <div><strong>Tổng số dòng kiểm đếm:</strong> {analyticsReport.stocktakeAccuracy.totalLinesCounted} dòng</div>
                  <div><strong>Dòng khớp tuyệt đối:</strong> {analyticsReport.stocktakeAccuracy.exactMatchLinesCount} dòng</div>
                  <div><strong>Dòng có lệch kiểm kê:</strong> {analyticsReport.stocktakeAccuracy.varianceLinesCount} dòng</div>
                  <div>
                    <strong>Tỷ lệ chính xác kiểm kê:</strong>{' '}
                    <UiBadge tone={analyticsReport.stocktakeAccuracy.accuracyRatePercent >= 98 ? 'success' : 'warning'}>
                      {analyticsReport.stocktakeAccuracy.accuracyRatePercent}%
                    </UiBadge>
                  </div>
                  <div><strong>Tổng trị tuyệt đối lệch SL:</strong> {analyticsReport.stocktakeAccuracy.totalAbsoluteVarianceQty}</div>
                </div>
              </UiCard>
            </div>

            <UiCard title="Bảng Vòng quay Tồn kho & Số ngày tồn">
              <UiTableScroll>
                <table aria-label="Bảng vòng quay tồn kho và số ngày tồn" className="ui-table-compact">
                  <thead>
                    <tr>
                      <th>Mã SP</th>
                      <th>Tên SP</th>
                      <th>ĐVT</th>
                      <th className="inventory-numeric">Tồn hiện tại</th>
                      <th className="inventory-numeric">Nhập trong kỳ</th>
                      <th className="inventory-numeric">Xuất trong kỳ</th>
                      <th className="inventory-numeric">Hệ số vòng quay</th>
                      <th className="inventory-numeric">Số ngày tồn</th>
                      <th>Phân nhóm tốc độ</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analyticsReport.turnoverAndDaysOnHand.length === 0 ? (
                      <tr><td colSpan={9} className="ui-empty-cell">Không có dữ liệu vòng quay sản phẩm</td></tr>
                    ) : (
                      analyticsReport.turnoverAndDaysOnHand.map(item => (
                        <tr key={item.productId}>
                          <td><strong>{item.productCode}</strong></td>
                          <td>{item.productName}</td>
                          <td>{formatUnitName(item.unitName)}</td>
                          <td className="inventory-numeric">{item.onHandQuantity}</td>
                          <td className="inventory-numeric">{item.periodInQuantity}</td>
                          <td className="inventory-numeric">{item.periodOutQuantity}</td>
                          <td className="inventory-numeric"><strong>{item.turnoverRate}</strong></td>
                          <td className="inventory-numeric">{item.daysOnHand !== null && item.daysOnHand !== undefined ? `${item.daysOnHand} ngày` : '∞'}</td>
                          <td>{velocityBadge(item.velocityClass)}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </UiTableScroll>
            </UiCard>

            <UiCard title="Hoạch định & Tối ưu hóa Kho nâng cao: Phân loại ABC/XYZ, Tồn an toàn, Cam kết Giao hàng & Cân bằng Mạng lưới">
              <div className="ui-stack-sm">
                <div className="ui-grid-2">
                  <div>
                    <strong>Phân nhóm ABC/XYZ & Điểm đặt hàng lại:</strong>
                    <div className="ui-stack-xs">
                      <div>
                        • Nhóm <strong>A (Luân chuyển cao)</strong>:{' '}
                        {analyticsReport.turnoverAndDaysOnHand.filter(x => x.velocityClass === 'FAST_MOVING').length} mặt hàng
                      </div>
                      <div>
                        • Nhóm <strong>B (Luân chuyển vừa)</strong>:{' '}
                        {analyticsReport.turnoverAndDaysOnHand.filter(x => x.velocityClass === 'NORMAL_MOVING' || x.velocityClass === 'NORMAL').length} mặt hàng
                      </div>
                      <div>
                        • Nhóm <strong>C / Z (Chậm & Tồn đọng)</strong>:{' '}
                        {analyticsReport.turnoverAndDaysOnHand.filter(x => x.velocityClass !== 'FAST_MOVING' && x.velocityClass !== 'NORMAL_MOVING' && x.velocityClass !== 'NORMAL').length} mặt hàng
                      </div>
                      <div>
                        • Đề xuất bổ sung Tồn an toàn (&lt; 50):{' '}
                        <strong>{analyticsReport.turnoverAndDaysOnHand.filter(x => x.onHandQuantity < 50).length} mặt hàng</strong> cần tạo đơn mua / điều chuyển
                      </div>
                    </div>
                  </div>
                  <div>
                    <strong>Cam kết Giao hàng, Cân bằng Mạng lưới & Quản trị Quyết định:</strong>
                    <div className="ui-stack-xs">
                      <div>
                        • Tổng lượng <strong>tồn khả dụng cam kết ngay</strong>:{' '}
                        <strong>{analyticsReport.turnoverAndDaysOnHand.reduce((sum, x) => sum + x.onHandQuantity, 0)}</strong> đvt (Thời gian xử lý chuẩn: 24h)
                      </div>
                      <div>
                        • Số kho đang hoạt động trong mạng lưới cân bằng tồn:{' '}
                        <strong>{warehouses.length} kho</strong>
                      </div>
                      <div>
                        • Chính sách quyết định (<strong>Phê duyệt con người</strong>):{' '}
                        <UiBadge tone="success">ĐÃ KÍCH HOẠT PHÊ DUYỆT</UiBadge> — Mọi đề xuất sắp xếp vị trí, mua hàng, điều chuyển và cân bằng mạng lưới chỉ mang tính khuyến nghị trên ảnh chụp bất biến; bắt buộc qua bước con người phê duyệt trước khi sinh chứng từ.
                      </div>
                    </div>
                  </div>
                </div>

                <div className="ui-stack-xs" style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--border-subtle, #e2e8f0)' }}>
                  <strong>Mô phỏng Giả định Biến động Cầu & Cân bằng Mạng lưới:</strong>
                  <div className="inventory-filter-actions" style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                    <span>Hệ số biến động cầu:</span>
                    {[1.2, 1.5, 2.0].map((mult) => (
                      <button
                        key={mult}
                        type="button"
                        className={whatIfMultiplier === mult ? 'btn-primary' : 'btn-secondary'}
                        onClick={() => setWhatIfMultiplier(mult)}
                      >
                        {mult.toFixed(1)}x
                      </button>
                    ))}
                    <button
                      type="button"
                      className="btn-primary"
                      disabled={governanceBusy}
                      onClick={async () => {
                        if (governanceBusy) return;
                        setGovernanceBusy(true);
                        setAnalyticsError('');
                        try {
                          const res = await apiClient.post('/api/inventory/what-if-simulation', {
                            warehouseId: analyticsFilter.warehouseId ? Number(analyticsFilter.warehouseId) : null,
                            demandMultiplier: whatIfMultiplier,
                            horizonDays: Number(analyticsFilter.days || 14),
                          });
                          if (res?.data && typeof res.data.simulatedDemand === 'number') {
                            setWhatIfResult(res.data);
                            return;
                          }
                          setWhatIfResult(null);
                          setAnalyticsError('Phản hồi mô phỏng kịch bản giả định không hợp lệ từ máy chủ.');
                        } catch {
                          setWhatIfResult(null);
                          setAnalyticsError('Không thể chạy mô phỏng kịch bản giả định.');
                        } finally {
                          setGovernanceBusy(false);
                        }
                      }}
                    >
                      Chạy Mô phỏng Giả định
                    </button>
                    <button
                      type="button"
                      className="btn-secondary"
                      disabled={governanceBusy}
                      onClick={() => {
                        if (governanceBusy) return;
                        setGovernanceBusy(true);
                        setAnalyticsError('');
                        void apiClient
                          .post('/api/inventory/evaluate-decision-policy-governance', {
                            warehouseId: analyticsFilter.warehouseId ? Number(analyticsFilter.warehouseId) : null,
                          })
                          .then((res) => {
                            const d = (res?.data ?? {}) as Record<string, unknown>;
                            if (typeof d.policyRegistryId !== 'string' || !d.policyRegistryId) {
                              setDecisionPolicyAudit(null);
                              setAnalyticsError('Phản hồi kiểm định sổ đăng ký chính sách quyết định không hợp lệ từ máy chủ.');
                              return;
                            }
                            setDecisionPolicyAudit({
                              policyRegistryId: d.policyRegistryId,
                              forecastBiasMapeStatus: String(d.forecastBiasMapeStatus ?? ''),
                              demandAnomalyDetector: String(d.demandAnomalyDetector ?? ''),
                              inventoryRiskScoreStatus: String(d.inventoryRiskScoreStatus ?? ''),
                              humanInTheLoopBoundary: String(d.humanInTheLoopBoundary ?? ''),
                              status: String(d.status ?? ''),
                            });
                          })
                          .catch(() => {
                            setDecisionPolicyAudit(null);
                            setAnalyticsError('Không thể kiểm định sổ đăng ký chính sách quyết định và rủi ro.');
                          })
                          .finally(() => setGovernanceBusy(false));
                      }}
                    >
                      Kiểm định Sổ đăng ký Chính sách Quyết định, Chấm điểm Rủi ro & Phát hiện Bất thường Cầu
                    </button>
                  </div>
                  {whatIfResult && (
                    <div role="status" aria-label="Kết quả mô phỏng kịch bản giả định What-If">
                      • Kết quả kịch bản <strong>{whatIfResult.demandMultiplier.toFixed(1)}x</strong>: Tồn khả dụng hiện tại{' '}
                      <strong>{whatIfResult.totalAvailable}</strong> đvt so với Cầu kịch bản{' '}
                      <strong>{whatIfResult.simulatedDemand}</strong> đvt → Chênh lệch dự kiến:{' '}
                      <strong>{whatIfResult.projectedNetBalance}</strong> đvt (
                      <UiBadge tone={whatIfResult.projectedNetBalance < 0 ? 'warning' : 'success'}>
                        {whatIfResult.scenarioStatus}
                      </UiBadge>
                      {' • '}Ảnh chụp bất biến, không ghi sổ cái thật)
                    </div>
                  )}
                  {decisionPolicyAudit && (
                    <div role="status" aria-label="Kết quả kiểm định sổ đăng ký chính sách quyết định và rủi ro">
                      <UiBadge tone="success">
                        {`Sổ đăng ký: ${decisionPolicyAudit.policyRegistryId} • Dự báo: ${decisionPolicyAudit.forecastBiasMapeStatus} • Bất thường cầu: ${decisionPolicyAudit.demandAnomalyDetector} • Chấm điểm rủi ro: ${decisionPolicyAudit.inventoryRiskScoreStatus} • Trạng thái: ${decisionPolicyAudit.status}`}
                      </UiBadge>
                    </div>
                  )}
                </div>
              </div>
            </UiCard>
          </div>
        )}
      </UiCard>
    );
  };

  return (
    <UiPage>
      <div className="inventory-page">
        <UiPageHeader
          eyebrow="Tồn kho"
          title="Báo cáo tồn kho"
          description="Theo dõi tồn thực tế, nhóm tồn theo Trạng thái/Lô/Sê-ri, điều kiện sử dụng, biến động sổ cái và báo cáo xuất nhập tồn theo phạm vi kho được phép."
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
            Nhóm tồn / Lô / Sê-ri
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
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'expiry'}
            aria-controls="inventory-expiry-panel"
            onClick={() => setActiveTab('expiry')}
          >
            Hạn dùng & Tuổi tồn
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'analytics'}
            aria-controls="inventory-analytics-panel"
            onClick={() => setActiveTab('analytics')}
          >
            Hiệu suất & Vòng quay
          </button>
        </div>

        {activeTab === 'stock' && <section id="inventory-stock-panel" role="tabpanel">{renderStockTab()}</section>}
        {activeTab === 'buckets' && <section id="inventory-buckets-panel" role="tabpanel"><InventoryBuckets warehouses={warehouses} /></section>}
        {activeTab === 'history' && <section id="inventory-history-panel" role="tabpanel">{renderHistoryTab()}</section>}
        {activeTab === 'inout' && <section id="inventory-inout-panel" role="tabpanel">{renderInOutTab()}</section>}
        {activeTab === 'expiry' && <section id="inventory-expiry-panel" role="tabpanel">{renderExpiryTab()}</section>}
        {activeTab === 'analytics' && <section id="inventory-analytics-panel" role="tabpanel">{renderAnalyticsTab()}</section>}
      </div>
    </UiPage>
  );
};

export default Inventory;
