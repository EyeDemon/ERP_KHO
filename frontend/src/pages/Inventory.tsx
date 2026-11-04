import React, { useState, useEffect } from 'react';
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
} from '../modules/inventory/inventoryShared';
import { InventoryHistoryTab, InventoryStockTab } from '../modules/inventory/InventoryStockAndHistoryTabs';
import { InventoryExpiryTab, InventoryInOutTab } from '../modules/inventory/InventoryInOutAndExpiryTabs';
import { InventoryAnalyticsTab } from '../modules/inventory/InventoryAnalyticsTab';
import {
  downloadOpeningBalanceTemplate,
  exportInOutExcel,
  exportStockExcel,
  fetchAuthorizedWarehouses,
  fetchInventoryAnalyticsSummary,
  fetchInventoryExpiryAging,
  fetchInventoryHistory,
  fetchInventoryInOut,
  fetchInventoryStocks,
  importOpeningBalanceExcel,
  INVENTORY_TABS,
  tabButtonStyle,
  type InventoryTabId,
} from '../modules/inventory/inventoryPageActions';
import './Inventory.css';

const Inventory = () => {
  const [activeTab, setActiveTab] = useState<InventoryTabId>('stock');
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
    fetchAuthorizedWarehouses()
      .then(setWarehouses)
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
      setStocks(await fetchInventoryStocks(stockFilter));
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
      setHistory(await fetchInventoryHistory(historyFilter));
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
      setInoutReport(await fetchInventoryInOut(inoutFilter));
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
      setExpiryReport(await fetchInventoryExpiryAging(expiryFilter));
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
      setAnalyticsReport(await fetchInventoryAnalyticsSummary(analyticsFilter));
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
      await exportStockExcel(stockFilter);
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
      await exportInOutExcel(inoutFilter);
    } catch {
      setInoutError('Lỗi khi xuất Excel xuất nhập tồn');
    }
  };

  const handleDownloadOpeningBalanceTemplate = async () => {
    setStockError('');
    setOpeningBalanceNotice('');
    try {
      await downloadOpeningBalanceTemplate();
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
      const result = await importOpeningBalanceExcel({
        warehouseId: stockFilter.warehouseId,
        warehouses,
        selectedOpeningFile,
        openingBalanceFileName,
      });
      setOpeningBalanceResult(result);
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
          {INVENTORY_TABS.map((tab) => (
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
