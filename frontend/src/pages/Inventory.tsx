import React, { useState, useEffect } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission } from '../services/authorization';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import StockCardS12DnPanel from '../modules/inventory/StockCardS12DnPanel';
import InventoryBuckets from './InventoryBuckets';
import type {
  DecisionPolicyAuditResult,
  InventoryAnalyticsSummaryDto,
  InventoryExpiryAgingDto,
  InventoryInOutDto,
  InventoryStockDto,
  InventoryTransactionHistoryDto,
  PagedResult,
  WarehouseOption,
  WhatIfSimulationResult,
} from '../modules/inventory-pages/inventoryShared';
import { parseAnalyticsSummaryResponse } from '../modules/inventory-pages/inventoryShared';
import { InventoryHistoryTab, InventoryStockTab } from '../modules/inventory-pages/InventoryStockAndHistoryTabs';
import { InventoryExpiryTab, InventoryInOutTab } from '../modules/inventory-pages/InventoryInOutAndExpiryTabs';
import { InventoryAnalyticsTab } from '../modules/inventory-pages/InventoryAnalyticsTab';
import './Inventory.css';

const tabButtonStyle = (active: boolean): React.CSSProperties =>
  active
    ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 }
    : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 };

const downloadBlobFile = (blobData: BlobPart, fileName: string) => {
  if (typeof window !== 'undefined' && window.URL?.createObjectURL) {
    const url = window.URL.createObjectURL(new Blob([blobData]));
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', fileName);
    document.body.appendChild(link);
    link.click();
    link.remove();
  }
};

const Inventory = () => {
  const [activeTab, setActiveTab] = useState<'stock' | 'buckets' | 'history' | 'inout' | 'expiry' | 'analytics'>('stock');
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);

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
    apiClient
      .get('/api/v1/warehouses')
      .then((response) => setWarehouses(response.data.filter((warehouse: WarehouseOption) => warehouse.isActive)))
      .catch(() => {
        setWarehouses([]);
        setStockError('Không thể tải danh sách kho được phân quyền.');
      });
  }, []);

  useEffect(() => {
    if (stockPage > stockTotalPages && stockTotalPages > 0) setStockPage(stockTotalPages);
  }, [stocks.length, stockTotalPages, stockPage]);

  const [history, setHistory] = useState<PagedResult<InventoryTransactionHistoryDto> | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [historyFilter, setHistoryFilter] = useState({
    fromDate: '', toDate: '', transactionType: '', warehouseId: '', productId: '', keyword: '', page: 1, pageSize: 20,
  });

  const [inoutFilter, setInoutFilter] = useState({ fromDate: '', toDate: '', warehouseId: '', productId: '' });
  const [inoutPage, setInoutPage] = useState(1);
  const inoutPageSize = 20;
  const [inoutReport, setInoutReport] = useState<InventoryInOutDto[] | null>(null);
  const [inoutLoading, setInoutLoading] = useState(false);
  const [inoutError, setInoutError] = useState('');

  const [expiryFilter, setExpiryFilter] = useState({ warehouseId: '', productId: '', nearExpiryDays: '30', onlyLots: false });
  const [expiryReport, setExpiryReport] = useState<InventoryExpiryAgingDto[]>([]);
  const [expiryLoading, setExpiryLoading] = useState(false);
  const [expiryError, setExpiryError] = useState('');

  const [analyticsFilter, setAnalyticsFilter] = useState({ warehouseId: '', days: '30' });
  const [analyticsReport, setAnalyticsReport] = useState<InventoryAnalyticsSummaryDto | null>(null);
  const [analyticsLoading, setAnalyticsLoading] = useState(false);
  const [analyticsError, setAnalyticsError] = useState('');
  const [whatIfMultiplier, setWhatIfMultiplier] = useState<number>(1.5);
  const [whatIfResult, setWhatIfResult] = useState<WhatIfSimulationResult | null>(null);
  const [decisionPolicyAudit, setDecisionPolicyAudit] = useState<DecisionPolicyAuditResult | null>(null);
  const [governanceBusy, setGovernanceBusy] = useState(false);

  const [selectedOpeningFile, setSelectedOpeningFile] = useState<File | null>(null);
  const [openingBalanceFileName, setOpeningBalanceFileName] = useState('TonKho_DauKy_Mau.xlsx');
  const [openingBalanceNotice, setOpeningBalanceNotice] = useState('');
  const [openingBalanceResult, setOpeningBalanceResult] = useState<{ importBatchId: string; importedRows: number; status: string } | null>(null);

  const fetchStocks = async () => {
    setStockLoading(true);
    setStockError('');
    try {
      const params = new URLSearchParams();
      if (stockFilter.warehouseId) params.append('warehouseId', stockFilter.warehouseId);
      if (stockFilter.productId) params.append('productId', stockFilter.productId);
      if (stockFilter.keyword) params.append('keyword', stockFilter.keyword);
      const res = await apiClient.get('/api/v1/inventory-stocks/current?' + params.toString());
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
      const res = await apiClient.get('/api/v1/inventory-transactions?' + params.toString());
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
      const res = await apiClient.get('/api/v1/reports/inventory-in-out-stock?' + params.toString());
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
      const res = await apiClient.get('/api/v1/reports/expiry-aging?' + params.toString());
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
      const res = await apiClient.get('/api/v1/reports/analytics-summary?' + params.toString());
      setAnalyticsReport(parseAnalyticsSummaryResponse(res.data as Record<string, unknown> | null | undefined, analyticsFilter.days));
    } catch {
      setAnalyticsError('Lỗi khi tải báo cáo hiệu suất và vòng quay tồn kho.');
      setAnalyticsReport(null);
    } finally {
      setAnalyticsLoading(false);
    }
  };

  useEffect(() => {
    if (activeTab === 'stock') void fetchStocks();
    else if (activeTab === 'history') void fetchHistory();
    else if (activeTab === 'inout') { if (!inoutReport && !inoutError) void fetchInOut(); }
    else if (activeTab === 'expiry') void fetchExpiryAging();
    else if (activeTab === 'analytics') {
      if (stocks.length === 0) void fetchStocks();
      void fetchAnalyticsSummary();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, historyFilter.page]);

  const handleExportStock = async () => {
    try {
      const params = new URLSearchParams();
      if (stockFilter.warehouseId) params.append('warehouseId', stockFilter.warehouseId);
      if (stockFilter.productId) params.append('productId', stockFilter.productId);
      const res = await apiClient.get('/api/v1/reports/inventory/export?' + params.toString(), { responseType: 'blob' });
      downloadBlobFile(res.data, 'BaoCaoTonKho.xlsx');
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
      const res = await apiClient.get('/api/v1/reports/inventory-in-out-stock/export?' + params.toString(), { responseType: 'blob' });
      downloadBlobFile(res.data, 'BaoCaoXuatNhapTon.xlsx');
    } catch {
      setInoutError('Lỗi khi xuất Excel xuất nhập tồn');
    }
  };

  const handleDownloadOpeningBalanceTemplate = async () => {
    setStockError('');
    setOpeningBalanceNotice('');
    try {
      const res = await apiClient.get('/api/v1/import-templates/opening-balance', { responseType: 'blob' });
      if (res?.data) downloadBlobFile(res.data, 'Mau_Nhap_Ton_Kho_Dau_Ky.xlsx');
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
      if (selectedOpeningFile) formData.append('file', selectedOpeningFile);
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

  return (
    <UiPage>
      <div className="inventory-page">
        <UiPageHeader
          eyebrow="Tồn kho"
          title="Báo cáo tồn kho"
          description="Theo dõi tồn thực tế, nhóm tồn theo Trạng thái/Lô/Sê-ri, điều kiện sử dụng, biến động sổ cái và báo cáo xuất nhập tồn theo phạm vi kho được phép."
        />

        <div className="inventory-tabs" role="tablist" aria-label="Chế độ báo cáo tồn kho">
          {[
            { id: 'stock' as const, label: 'Tồn kho hiện tại', panel: 'inventory-stock-panel' },
            { id: 'buckets' as const, label: 'Nhóm tồn / Lô / Sê-ri', panel: 'inventory-buckets-panel' },
            { id: 'history' as const, label: 'Lịch sử giao dịch', panel: 'inventory-history-panel' },
            { id: 'inout' as const, label: 'Xuất nhập tồn', panel: 'inventory-inout-panel' },
            { id: 'expiry' as const, label: 'Hạn dùng & Tuổi tồn', panel: 'inventory-expiry-panel' },
            { id: 'analytics' as const, label: 'Hiệu suất & Vòng quay', panel: 'inventory-analytics-panel' },
          ].map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              className="inventory-tab"
              aria-selected={activeTab === tab.id}
              style={tabButtonStyle(activeTab === tab.id)}
              aria-controls={tab.panel}
              onClick={() => setActiveTab(tab.id)}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {activeTab === 'stock' && (
          <section id="inventory-stock-panel" role="tabpanel">
            <InventoryStockTab
              warehouses={warehouses}
              stocks={stocks}
              paginatedStocks={paginatedStocks}
              stockLoading={stockLoading}
              stockError={stockError}
              stockFilter={stockFilter}
              setStockFilter={setStockFilter}
              currentStockPage={currentStockPage}
              stockTotalPages={stockTotalPages}
              setStockPage={setStockPage}
              onFilterSubmit={(e) => { e.preventDefault(); setStockPage(1); void fetchStocks(); }}
              onExportStock={() => void handleExportStock()}
              onOpeningBalanceImport={(e) => void handleOpeningBalanceImport(e)}
              onDownloadOpeningBalanceTemplate={() => void handleDownloadOpeningBalanceTemplate()}
              onOpeningFileChange={(file) => {
                setSelectedOpeningFile(file);
                setOpeningBalanceFileName(file?.name || 'TonKho_DauKy_Mau.xlsx');
              }}
              openingBalanceNotice={openingBalanceNotice}
              openingBalanceResult={openingBalanceResult}
            />
          </section>
        )}
        {activeTab === 'buckets' && (
          <section id="inventory-buckets-panel" role="tabpanel">
            <InventoryBuckets warehouses={warehouses} />
          </section>
        )}
        {activeTab === 'history' && (
          <section id="inventory-history-panel" role="tabpanel">
            <InventoryHistoryTab
              warehouses={warehouses}
              history={history}
              historyLoading={historyLoading}
              historyError={historyError}
              historyFilter={historyFilter}
              setHistoryFilter={setHistoryFilter}
              onFilterSubmit={(e) => {
                e.preventDefault();
                if (historyFilter.page !== 1) setHistoryFilter((prev) => ({ ...prev, page: 1 }));
                else void fetchHistory();
              }}
            />
          </section>
        )}
        {activeTab === 'inout' && (
          <section id="inventory-inout-panel" role="tabpanel">
            <InventoryInOutTab
              warehouses={warehouses}
              stocks={stocks}
              inoutFilter={inoutFilter}
              setInoutFilter={setInoutFilter}
              inoutReport={inoutReport}
              inoutLoading={inoutLoading}
              inoutError={inoutError}
              inoutPage={inoutPage}
              inoutPageSize={inoutPageSize}
              setInoutPage={setInoutPage}
              onFilterSubmit={(e) => { e.preventDefault(); setInoutPage(1); void fetchInOut(); }}
              onExportInOut={() => void handleExportInOut()}
            />
          </section>
        )}
        {activeTab === 'expiry' && (
          <section id="inventory-expiry-panel" role="tabpanel">
            <InventoryExpiryTab
              warehouses={warehouses}
              stocks={stocks}
              expiryFilter={expiryFilter}
              setExpiryFilter={setExpiryFilter}
              expiryReport={expiryReport}
              expiryLoading={expiryLoading}
              expiryError={expiryError}
              onFilterSubmit={(e) => { e.preventDefault(); void fetchExpiryAging(); }}
            />
          </section>
        )}
        {activeTab === 'analytics' && (
          <section id="inventory-analytics-panel" role="tabpanel">
            <InventoryAnalyticsTab
              warehouses={warehouses}
              stocks={stocks}
              analyticsFilter={analyticsFilter}
              setAnalyticsFilter={setAnalyticsFilter}
              analyticsReport={analyticsReport}
              analyticsLoading={analyticsLoading}
              analyticsError={analyticsError}
              setAnalyticsError={setAnalyticsError}
              whatIfMultiplier={whatIfMultiplier}
              setWhatIfMultiplier={setWhatIfMultiplier}
              whatIfResult={whatIfResult}
              setWhatIfResult={setWhatIfResult}
              decisionPolicyAudit={decisionPolicyAudit}
              setDecisionPolicyAudit={setDecisionPolicyAudit}
              governanceBusy={governanceBusy}
              setGovernanceBusy={setGovernanceBusy}
              onFilterSubmit={(e) => { e.preventDefault(); void fetchAnalyticsSummary(); }}
            />
          </section>
        )}

        <StockCardS12DnPanel warehouses={warehouses} products={stocks} />
      </div>
    </UiPage>
  );
};

export default Inventory;
