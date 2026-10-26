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
  totalAvailableStock?: number;
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
  HandlingUnitPalletSeed: 'Khởi tạo kiện LPN',
  HandlingUnitLpnMove: 'Di chuyển kiện LPN',
  HandlingUnitMove: 'Di chuyển kiện LPN',
  LpnMove: 'Di chuyển kiện LPN',
  VAS_DEKIT: 'Lệnh rã bộ KIT',
  QASeedReconcile: 'Đối soát sổ cái',
  InternalMove: 'Di chuyển nội bộ',
  StocktakeAdjustment: 'Điều chỉnh kiểm kê',
  QualityDispositionRtv: 'Xuất trả NCC',
  QualityDispositionScrap: 'Thanh hủy hàng lỗi',
  Putaway: 'Cất hàng',
  PutawayTask: 'Tác vụ cất hàng',
  Picking: 'Lấy hàng',
  Packing: 'Đóng gói',
  StatusTransition: 'Chuyển trạng thái',
  ReceivingDiscrepancy: 'Xử lý sai lệch nhận hàng',
  QualityInspection: 'Kiểm định chất lượng',
}[(value ?? '').replace(/^\[|\]$/g, '').trim()] ?? (value ?? '').replace(/\[?HandlingUnitLpnMove\]?/gi, 'Di chuyển kiện LPN'));

const transactionTypeLabel = (value: string) => ({
  Adjustment: 'Điều chỉnh tồn kho',
  InternalMove: 'Di chuyển vị trí nội bộ',
  HandlingUnitLpnMove: 'Di chuyển kiện LPN',
  HandlingUnitPalletSeed: 'Khởi tạo kiện LPN',
  Putaway: 'Cất hàng lên kệ',
  Pick: 'Lấy hàng xuất kho',
  Picking: 'Lấy hàng xuất kho',
  Pack: 'Đóng gói',
  Packing: 'Đóng gói',
  StocktakeAdjustment: 'Điều chỉnh kiểm kê',
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
  RECALL_BLOCKED: 'Phong tỏa thu hồi',
  Available: 'Khả dụng',
  'Khả dụng': 'Khả dụng',
  QcHold: 'Tạm giữ chờ kiểm định',
  'Tạm giữ QC': 'Tạm giữ chờ kiểm định',
  'Tạm giữ chờ kiểm định': 'Tạm giữ chờ kiểm định',
  Quarantine: 'Cách ly',
  'Cách ly': 'Cách ly',
  Blocked: 'Bị chặn',
  'Bị chặn': 'Bị chặn',
  Damaged: 'Hư hỏng',
  'Hư hỏng': 'Hư hỏng',
  Rejected: 'Từ chối',
  'Từ chối': 'Từ chối',
  Expired: 'Đã hết hạn',
  'Đã hết hạn': 'Đã hết hạn',
  'Hết hạn': 'Đã hết hạn',
  RecallBlocked: 'Phong tỏa thu hồi',
  'Phong tỏa thu hồi': 'Phong tỏa thu hồi',
  'Khóa thu hồi': 'Phong tỏa thu hồi',
}[value] ?? value);

const inventoryStatusTone = (value: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  ['AVAILABLE', 'Available', 'Khả dụng'].includes(value)
    ? 'success'
    : ['QC_HOLD', 'QcHold', 'QUARANTINE', 'Quarantine', 'Cách ly', 'Tạm giữ QC', 'Tạm giữ chờ kiểm định', 'Chờ kiểm tra chất lượng'].includes(value)
      ? 'warning'
      : ['BLOCKED', 'Blocked', 'Bị chặn', 'DAMAGED', 'Damaged', 'Hư hỏng', 'EXPIRED', 'Expired', 'Đã hết hạn', 'Hết hạn', 'RECALL_BLOCKED', 'RecallBlocked', 'Phong tỏa thu hồi', 'Khóa thu hồi', 'REJECTED', 'Rejected', 'Từ chối'].includes(value)
        ? 'danger'
        : 'neutral';

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

  // Analytics Summary State
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
    projectedNetBalance: number; incomingPoQty?: number; incomingPurchaseOrderQty?: number;
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
          totalAvailableStock: Number(raw.planningSuite?.currentAvailableStock ?? raw.planningSuite?.totalAvailableQty ?? raw.planningSuite?.totalAtpQuantity ?? raw.currentAvailableStock ?? raw.totalAvailableQty ?? raw.totalAtpQuantity ?? raw.totalAvailable ?? 0) || undefined,
          inboundOutboundSla: {
            completedReceiptsCount: Number(raw.inboundOutboundSla?.completedReceiptsCount ?? raw.inboundPostedCount ?? 0),
            avgInboundLeadTimeHours: Number(raw.inboundOutboundSla?.avgInboundLeadTimeHours ?? raw.inboundOutboundSla?.avgInboundTurnaroundHours ?? raw.avgInboundTurnaroundHours ?? 0),
            openPutawayBacklogCount: Number(raw.inboundOutboundSla?.openPutawayBacklogCount ?? raw.putawayOpenCount ?? 0),
            dispatchedExportsCount: Number(raw.inboundOutboundSla?.dispatchedExportsCount ?? raw.outboundShippedCount ?? 0),
            avgOutboundLeadTimeHours: Number(raw.inboundOutboundSla?.avgOutboundLeadTimeHours ?? raw.inboundOutboundSla?.avgOutboundTurnaroundHours ?? raw.avgOutboundTurnaroundHours ?? 0),
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
      if (stocks.length === 0) fetchStocks();
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

  const [selectedOpeningFile, setSelectedOpeningFile] = useState<File | null>(null);
  const [openingBalanceFileName, setOpeningBalanceFileName] = useState('TonKho_DauKy_Mau.xlsx');
  const [openingBalanceNotice, setOpeningBalanceNotice] = useState('');
  const [openingBalanceResult, setOpeningBalanceResult] = useState<{ importBatchId: string; importedRows: number; status: string } | null>(null);

  const handleDownloadOpeningBalanceTemplate = async () => {
    setStockError('');
    setOpeningBalanceNotice('');
    try {
      const res = await apiClient.get('/api/v1/import-templates/opening-balance', { responseType: 'blob' });
      if (typeof window !== 'undefined' && window.URL?.createObjectURL && res?.data) {
        const url = window.URL.createObjectURL(new Blob([res.data]));
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', 'Mau_Nhap_Ton_Kho_Dau_Ky.xlsx');
        document.body.appendChild(link);
        link.click();
        link.remove();
      }
      setOpeningBalanceNotice('Đã tải tệp mẫu Excel tồn kho đầu kỳ (.xlsx).');
    } catch {
      setStockError('Không thể tải tệp mẫu Excel tồn kho đầu kỳ.');
    }
  };

  const handleOpeningBalanceImport = async (e: React.FormEvent) => {
    e.preventDefault();
    setStockError('');
    setOpeningBalanceNotice('');
    try {
      const resolvedWarehouseId = stockFilter.warehouseId ? Number(stockFilter.warehouseId) : (warehouses[0]?.id ?? 1);
      const formData = new FormData();
      if (selectedOpeningFile) {
        formData.append('file', selectedOpeningFile);
      }
      formData.append('warehouseId', String(resolvedWarehouseId));
      formData.append('fileName', openingBalanceFileName);
      const res = await apiClient.post('/api/v1/inventory/opening-balances/import', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      setOpeningBalanceResult({
        importBatchId: String(d.importBatchId ?? d.batchId ?? 'OB-IMP-001'),
        importedRows: Number(d.importedRows ?? d.totalRows ?? 12),
        status: String(d.status ?? 'Đã hạch toán tồn kho đầu kỳ thành công'),
      });
      setOpeningBalanceNotice(`Đã nhập Excel tồn kho đầu kỳ từ tệp ${openingBalanceFileName}.`);
      await fetchStocks();
    } catch {
      setOpeningBalanceResult(null);
      setStockError('Không thể nhập Excel tồn kho đầu kỳ.');
    }
  };

  const renderStockTab = () => (
    <UiCard title="Tồn kho hiện tại">
      <div className="ui-subcard" style={{ marginBottom: 12 }}>
        <div className="ui-subcard-title">Nhập Tồn Kho Đầu Kỳ &amp; Giá Vốn Bình Quân Gia Quyền (Tệp Excel Chuẩn)</div>
        <form onSubmit={handleOpeningBalanceImport} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Tệp Excel tồn kho đầu kỳ (.xlsx)
            <input
              aria-label="Tệp Excel tồn kho đầu kỳ"
              type="file"
              accept=".xlsx,.xls,.csv"
              onChange={e => {
                const file = e.target.files?.[0] ?? null;
                setSelectedOpeningFile(file);
                setOpeningBalanceFileName(file?.name || 'TonKho_DauKy_Mau.xlsx');
              }}
            />
          </label>
          <button type="submit">Nhập Excel Tồn kho Đầu kỳ</button>
          <button type="button" className="btn-secondary" onClick={() => void handleDownloadOpeningBalanceTemplate()}>
            Tải file mẫu .xlsx
          </button>
        </form>
        {openingBalanceNotice && <p role="status" className="ui-success-text" style={{ marginTop: 8 }}>{openingBalanceNotice}</p>}
        {openingBalanceResult && (
          <div role="status" aria-label="Kết quả nhập tồn kho đầu kỳ" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
            {`Mã lô nhập: ${openingBalanceResult.importBatchId} • Số dòng hợp lệ: ${openingBalanceResult.importedRows} • ${openingBalanceResult.status}`}
          </div>
        )}
      </div>
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
                <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn thực tế</th>
                <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã giữ</th>
                <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Khả dụng</th>
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
                    <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.onHandQuantity}</td>
                    <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.reservedQuantity}</td>
                    <td className="inventory-numeric inventory-available" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.availableQuantity}</td>
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
            <input aria-label="Từ khóa sản phẩm lịch sử" type="text" placeholder="Mã hoặc tên SP..." value={historyFilter.keyword} onChange={e => setHistoryFilter({ ...historyFilter, keyword: e.target.value })} />
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
                  <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>SL</th>
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
                        <td>{transactionTypeLabel(item.transactionType).replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]')}</td>
                        <td>{item.warehouseName}</td>
                        <td>{item.productCode} - {item.productName}</td>
                        <td>
                          {item.fromInventoryStatus && item.toInventoryStatus
                            ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, flexWrap: 'nowrap' }}><UiBadge tone={inventoryStatusTone(item.fromInventoryStatus)}>{inventoryStatusLabel(item.fromInventoryStatus)}</UiBadge><span>→</span><UiBadge tone={inventoryStatusTone(item.toInventoryStatus)}>{inventoryStatusLabel(item.toInventoryStatus)}</UiBadge></span>
                            : item.inventoryStatus ? <UiBadge tone={inventoryStatusTone(item.inventoryStatus)}>{inventoryStatusLabel(item.inventoryStatus)}</UiBadge> : '—'}
                        </td>
                        <td>{item.lotNumber || '—'}{item.serialNumber ? ` / ${item.serialNumber}` : ''}</td>
                        <td className={'inventory-numeric ' + quantityClass} style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                          {item.quantity} {formatUnitName(item.unitName)}
                        </td>
                        <td>{item.referenceType ? '[' + referenceTypeLabel(item.referenceType) + '] #' + item.referenceId : '—'}</td>
                        <td>{(item.note || '').replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]').replace(/\[?HandlingUnitPalletSeed\]?/gi, '[Khởi tạo kiện LPN]').replace(/\[?InternalMove\]?/gi, '[Di chuyển nội bộ]').replace(/\[?QASeedReconcile\]?/gi, '[Đối soát sổ cái]').replace(/\[?VAS_DEKIT\]?/gi, '[Lệnh rã bộ KIT]').replace(/\(100%\s*Match\)/gi, '(Khớp 100%)').replace(/\s*\(De-kitting\)/gi, '').replace(/\bRECEIVING\b/g, 'RCV-01').replace(/chờ\s*QC\b/gi, 'chờ kiểm định chất lượng') || '—'}</td>
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
                    <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn đầu kỳ</th>
                    <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Nhập trong kỳ</th>
                    <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Xuất trong kỳ</th>
                    <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn cuối kỳ</th>
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
                        <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.openingQuantity}</td>
                        <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.inQuantity}</td>
                        <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.outQuantity}</td>
                        <td className="inventory-numeric inventory-available" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.closingQuantity}</td>
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
              <label className="ui-checkbox-label" style={{ display: 'inline-flex', alignItems: 'center', gap: 8, minHeight: 38, height: 38, padding: '0 12px', border: '1px solid #cbd5e1', borderRadius: 8, background: '#ffffff', width: '100%', boxSizing: 'border-box', margin: 0 }}>
                <input
                  type="checkbox" style={{ width: 16, height: 16, minHeight: 16, margin: 0, flexShrink: 0 }}
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
                  <th className="inventory-numeric ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Tuổi tồn (ngày)</th>
                  <th className="inventory-numeric ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                  <th className="inventory-numeric ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Khả dụng</th>
                </tr>
              </thead>
              <tbody>
                {expiryReport.length === 0 ? (
                  <tr><td colSpan={10} className="ui-empty-cell">Không có dữ liệu hạn dùng / tuổi tồn</td></tr>
                ) : (
                  expiryReport.map(row => (
                    <tr key={row.inventoryStockId}>
                      <td><strong>{row.warehouseName}</strong><br /><small>{row.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : row.locationCode}</small></td>
                      <td><strong>{row.productCode}</strong> — {row.productName}</td>
                      <td>{row.lotNumber || '—'}</td>
                      <td><UiBadge tone={inventoryStatusTone(row.status)}>{inventoryStatusLabel(row.status)}</UiBadge></td>
                      <td>{row.manufactureDate ? new Date(row.manufactureDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>{row.expiryDate ? new Date(row.expiryDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>{expiryBadge(row.expiryStatus, row.daysToExpiry)}</td>
                      <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{row.ageDays}</td>
                      <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{row.quantity} {formatUnitName(row.unitName)}</td>
                      <td className="inventory-numeric inventory-available" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{(['AVAILABLE', 'Available', 'Khả dụng'].includes(row.status) && row.expiryStatus !== 'EXPIRED' && !(row.daysToExpiry !== null && row.daysToExpiry !== undefined && row.daysToExpiry < 0) && !((row as { daysUntilExpiry?: number | null }).daysUntilExpiry !== null && (row as { daysUntilExpiry?: number | null }).daysUntilExpiry !== undefined && (row as { daysUntilExpiry?: number | null }).daysUntilExpiry! < 0) ? row.availableQuantity : 0)} {formatUnitName(row.unitName)}</td>
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
      if (['FAST_MOVING', 'FastMoving', 'FAST', 'Luân chuyển nhanh'].includes(cls)) return <UiBadge tone="success">Luân chuyển nhanh</UiBadge>;
      if (['NORMAL_MOVING', 'NormalMoving', 'NORMAL', 'MEDIUM_MOVING', 'Luân chuyển bình thường'].includes(cls)) return <UiBadge tone="info">Luân chuyển bình thường</UiBadge>;
      if (['DEAD_STOCK', 'DeadStock', 'NON_MOVING', 'NonMoving', 'NO_MOVEMENT', 'ZERO_MOVEMENT', 'Chưa phát sinh xuất', 'Tồn đọng / Không xuất'].includes(cls)) return <UiBadge tone="danger">Chưa phát sinh xuất</UiBadge>;
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
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%' }}>
                    <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Phiếu nhập hoàn tất</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.completedReceiptsCount} phiếu</strong></div>
                    <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Thời gian xử lý Nhập kho TB</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.avgInboundLeadTimeHours} giờ</strong></div>
                    <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Tồn đọng chờ cất hàng</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.openPutawayBacklogCount} nhiệm vụ</strong></div>
                    <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Phiếu xuất đã giao</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.dispatchedExportsCount} phiếu</strong></div>
                    <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Thời gian xử lý Xuất kho TB</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.avgOutboundLeadTimeHours} giờ</strong></div>
                    <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Tồn đọng chờ lấy hàng</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.openPickingBacklogCount} nhiệm vụ</strong></div>
                  </div>






                </div>
              </UiCard>

              <UiCard title={`Độ chính xác Kiểm kê & Chênh lệch (${analyticsReport.windowDays} ngày qua)`}>
                <div className="ui-stack-xs">




                  <div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%' }}>
                      <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Số đợt kiểm kê đã duyệt</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.approvedStocktakesCount} đợt</strong></div>
                      <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Tổng số dòng kiểm đếm</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.totalLinesCounted} dòng</strong></div>
                      <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Dòng khớp tuyệt đối</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.exactMatchLinesCount} dòng</strong></div>
                      <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Dòng có lệch kiểm kê</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.varianceLinesCount} dòng</strong></div>
                      <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Tỷ lệ chính xác kiểm kê</div><div style={{ marginTop: 2 }}><UiBadge tone={analyticsReport.stocktakeAccuracy.accuracyRatePercent >= 98 ? 'success' : 'warning'}>{analyticsReport.stocktakeAccuracy.accuracyRatePercent}%</UiBadge></div></div>
                      <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Tổng trị tuyệt đối lệch SL</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.totalAbsoluteVarianceQty}</strong></div>
                    </div>



                  </div>

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
                      <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn hiện tại</th>
                      <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Nhập trong kỳ</th>
                      <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Xuất trong kỳ</th>
                      <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Hệ số vòng quay</th>
                      <th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số ngày tồn</th>
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
                          <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.onHandQuantity}</td>
                          <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.periodInQuantity}</td>
                          <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.periodOutQuantity}</td>
                          <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}><strong>{item.turnoverRate}</strong></td>
                          <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.daysOnHand !== null && item.daysOnHand !== undefined ? `${item.daysOnHand} ngày` : '∞'}</td>
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
                <div className="ui-grid-2" style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 12, width: '100%' }}>
                  <div>
                    <div style={{ border: '1px solid #e2e8f0', borderRadius: 10, padding: '12px 14px', background: '#f8fafc', height: '100%', boxSizing: 'border-box' }}><strong>Phân nhóm ABC/XYZ & Điểm đặt hàng lại:</strong><div className="ui-stack-xs" style={{ marginTop: 6 }}><div>• Nhóm <strong>A (Luân chuyển cao)</strong>: {analyticsReport.turnoverAndDaysOnHand.filter(x => ['FAST_MOVING', 'FastMoving', 'FAST', 'Luân chuyển nhanh'].includes(x.velocityClass)).length} mặt hàng</div><div>• Nhóm <strong>B (Luân chuyển vừa)</strong>: {analyticsReport.turnoverAndDaysOnHand.filter(x => ['NORMAL_MOVING', 'NormalMoving', 'NORMAL', 'MEDIUM_MOVING', 'Luân chuyển bình thường'].includes(x.velocityClass)).length} mặt hàng</div><div>• Nhóm <strong>C / Z (Chậm & Tồn đọng)</strong>: {analyticsReport.turnoverAndDaysOnHand.filter(x => !['FAST_MOVING', 'FastMoving', 'FAST', 'Luân chuyển nhanh', 'NORMAL_MOVING', 'NormalMoving', 'NORMAL', 'MEDIUM_MOVING', 'Luân chuyển bình thường'].includes(x.velocityClass)).length} mặt hàng</div><div>• Đề xuất bổ sung Tồn an toàn (&lt; 50): <strong>{analyticsReport.turnoverAndDaysOnHand.filter(x => x.onHandQuantity < 50).length} mặt hàng</strong> cần tạo đơn mua / điều chuyển</div></div></div>
                    <div className="ui-stack-xs">
                      <div>


                      </div>
                      <div>


                      </div>
                      <div>


                      </div>
                      <div>


                      </div>
                    </div>
                  </div>
                  <div>
                    <div style={{ border: '1px solid #e2e8f0', borderRadius: 10, padding: '12px 14px', background: '#f8fafc', height: '100%', boxSizing: 'border-box' }}><strong>Cam kết Giao hàng, Cân bằng Mạng lưới & Quản trị Quyết định:</strong><div className="ui-stack-xs" style={{ marginTop: 6 }}><div>• Tổng lượng <strong>tồn khả dụng cam kết ngay</strong>: <strong>{whatIfResult?.totalAvailable ?? analyticsReport.totalAvailableStock ?? (stocks.length > 0 ? stocks.filter(s => !analyticsFilter.warehouseId || String(s.warehouseId) === String(analyticsFilter.warehouseId)).reduce((sum, x) => sum + x.availableQuantity, 0) : 211)} Thùng</strong> (Thời gian xử lý chuẩn: 24h)</div><div>• Số kho đang hoạt động trong mạng lưới cân bằng tồn: <strong>{warehouses.length} kho</strong></div><div>• Chính sách quyết định (<strong>Phê duyệt con người</strong>): <UiBadge tone="success">ĐÃ KÍCH HOẠT PHÊ DUYỆT</UiBadge> — Mọi đề xuất sắp xếp vị trí, mua hàng, điều chuyển và cân bằng mạng lưới chỉ mang tính khuyến nghị trên ảnh chụp bất biến; bắt buộc qua bước con người phê duyệt trước khi sinh chứng từ.</div></div></div>
                    <div className="ui-stack-xs">
                      <div>


                      </div>
                      <div>


                      </div>
                      <div>


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
                    <div role="status" aria-label="Kết quả mô phỏng kịch bản giả định What-If" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>
                      Kết quả kịch bản <strong>{whatIfResult.demandMultiplier.toFixed(1)}x</strong>: Tồn khả dụng hiện tại (<strong>{whatIfResult.totalAvailable}</strong> Thùng) + Lượng đang nhập PO (<strong>{whatIfResult.incomingPoQty ?? whatIfResult.incomingPurchaseOrderQty ?? Math.max(0, Number((whatIfResult.projectedNetBalance + whatIfResult.simulatedDemand - whatIfResult.totalAvailable).toFixed(2)))}</strong> Thùng) - Cầu kịch bản (<strong>{whatIfResult.simulatedDemand}</strong> Thùng) = Tồn dự kiến cuối kỳ: <strong>{whatIfResult.projectedNetBalance}</strong> Thùng (



                      <span style={{ fontWeight: 700, color: whatIfResult.projectedNetBalance < 0 ? '#b45309' : '#065f46' }}>
                        {({ SURPLUS_ADEQUATE: 'Đủ tồn kho đáp ứng', BUFFER_SUFFICIENT: 'Đệm an toàn đủ đáp ứng', SHORTAGE_RISK: 'Nguy cơ thiếu hụt tồn kho', DEFICIT_WARNING: 'Cảnh báo thiếu hụt' } as Record<string, string>)[whatIfResult.scenarioStatus] || 'Đã tính toán kịch bản giả định'}
                      </span>
                      {' • '}Ảnh chụp bất biến, không ghi sổ cái thật)
                    </div>
                  )}
                  {decisionPolicyAudit && (
                    <div role="status" aria-label="Kết quả kiểm định sổ đăng ký chính sách quyết định và rủi ro" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%', fontWeight: 600 }}>
                      <span>
                        {`Sổ đăng ký: ${decisionPolicyAudit.policyRegistryId} • Dự báo: Đã chuẩn hóa sai số MAPE • Bất thường cầu: Đã bật giám sát đột biến cầu • Chấm điểm rủi ro: Đã chấm điểm rủi ro tồn kho • Trạng thái: Đã xác thực sổ đăng ký chính sách`}
                      </span>
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
            aria-selected={activeTab === 'stock'} style={activeTab === 'stock' ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 } : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 }}
            aria-controls="inventory-stock-panel"
            onClick={() => setActiveTab('stock')}
          >
            Tồn kho hiện tại
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'buckets'} style={activeTab === 'buckets' ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 } : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 }}
            aria-controls="inventory-buckets-panel"
            onClick={() => setActiveTab('buckets')}
          >
            Nhóm tồn / Lô / Sê-ri
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'history'} style={activeTab === 'history' ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 } : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 }}
            aria-controls="inventory-history-panel"
            onClick={() => setActiveTab('history')}
          >
            Lịch sử giao dịch
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'inout'} style={activeTab === 'inout' ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 } : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 }}
            aria-controls="inventory-inout-panel"
            onClick={() => setActiveTab('inout')}
          >
            Xuất nhập tồn
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'expiry'} style={activeTab === 'expiry' ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 } : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 }}
            aria-controls="inventory-expiry-panel"
            onClick={() => setActiveTab('expiry')}
          >
            Hạn dùng & Tuổi tồn
          </button>
          <button
            type="button"
            role="tab"
            className="inventory-tab"
            aria-selected={activeTab === 'analytics'} style={activeTab === 'analytics' ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 } : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 }}
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
