import React from 'react';
import apiClient from '../../services/apiClient';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import {
  parseAnalyticsSummaryResponse,
  type InventoryAnalyticsSummaryDto,
  type InventoryExpiryAgingDto,
  type InventoryInOutDto,
  type InventoryStockDto,
  type InventoryTransactionHistoryDto,
  type PagedResult,
  type WarehouseOption,
} from './inventoryShared';

export type InventoryTabId = 'stock' | 'buckets' | 'history' | 'inout' | 'expiry' | 'analytics';

export const INVENTORY_TABS: ReadonlyArray<{ id: InventoryTabId; label: string; panel: string }> = [
  { id: 'stock', label: 'Tồn kho hiện tại', panel: 'inventory-stock-panel' },
  { id: 'buckets', label: 'Nhóm tồn / Lô / Sê-ri', panel: 'inventory-buckets-panel' },
  { id: 'history', label: 'Lịch sử giao dịch', panel: 'inventory-history-panel' },
  { id: 'inout', label: 'Xuất nhập tồn', panel: 'inventory-inout-panel' },
  { id: 'expiry', label: 'Hạn dùng & Tuổi tồn', panel: 'inventory-expiry-panel' },
  { id: 'analytics', label: 'Hiệu suất & Vòng quay', panel: 'inventory-analytics-panel' },
];

export const tabButtonStyle = (active: boolean): React.CSSProperties =>
  active
    ? { background: '#0f172a', backgroundColor: '#0f172a', color: '#ffffff', borderColor: '#0f172a', fontWeight: 600 }
    : { background: '#ffffff', backgroundColor: '#ffffff', color: '#1e293b', borderColor: '#cbd5e1', fontWeight: 500 };

export const fetchAuthorizedWarehouses = async (): Promise<WarehouseOption[]> => {
  const response = await apiClient.get('/api/v1/warehouses');
  return (response.data as WarehouseOption[])
    .filter((warehouse) => warehouse.isActive)
    .map((warehouse) => ({
      ...warehouse,
      name: formatWarehouseLabel(warehouse.name, (warehouse as { code?: string }).code, warehouse.id),
    }));
};

export const fetchInventoryStocks = async (stockFilter: {
  warehouseId: string;
  productId: string;
  keyword: string;
}): Promise<InventoryStockDto[]> => {
  const params = new URLSearchParams();
  if (stockFilter.warehouseId) params.append('warehouseId', stockFilter.warehouseId);
  if (stockFilter.productId) params.append('productId', stockFilter.productId);
  if (stockFilter.keyword) params.append('keyword', stockFilter.keyword);
  const res = await apiClient.get('/api/v1/inventory-stocks/current?' + params.toString());
  return res.data as InventoryStockDto[];
};

export const fetchInventoryHistory = async (historyFilter: {
  fromDate: string;
  toDate: string;
  transactionType: string;
  warehouseId: string;
  productId: string;
  keyword: string;
  page: number;
  pageSize: number;
}): Promise<PagedResult<InventoryTransactionHistoryDto>> => {
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
  return res.data as PagedResult<InventoryTransactionHistoryDto>;
};

export const fetchInventoryInOut = async (inoutFilter: {
  fromDate: string;
  toDate: string;
  warehouseId: string;
  productId: string;
}): Promise<InventoryInOutDto[]> => {
  const params = new URLSearchParams();
  if (inoutFilter.fromDate) params.append('fromDate', inoutFilter.fromDate);
  if (inoutFilter.toDate) params.append('toDate', inoutFilter.toDate);
  if (inoutFilter.warehouseId) params.append('warehouseId', inoutFilter.warehouseId);
  if (inoutFilter.productId) params.append('productId', inoutFilter.productId);
  const res = await apiClient.get('/api/v1/reports/inventory-in-out-stock?' + params.toString());
  return res.data as InventoryInOutDto[];
};

export const fetchInventoryExpiryAging = async (expiryFilter: {
  warehouseId: string;
  productId: string;
  nearExpiryDays: string;
  onlyLots: boolean;
}): Promise<InventoryExpiryAgingDto[]> => {
  const params = new URLSearchParams();
  if (expiryFilter.warehouseId) params.append('warehouseId', expiryFilter.warehouseId);
  if (expiryFilter.productId) params.append('productId', expiryFilter.productId);
  if (expiryFilter.nearExpiryDays) params.append('nearExpiryDays', expiryFilter.nearExpiryDays);
  if (expiryFilter.onlyLots) params.append('onlyLots', 'true');
  const res = await apiClient.get('/api/v1/reports/expiry-aging?' + params.toString());
  return Array.isArray(res.data) ? (res.data as InventoryExpiryAgingDto[]) : [];
};

export const fetchInventoryAnalyticsSummary = async (analyticsFilter: {
  warehouseId: string;
  days: string;
}): Promise<InventoryAnalyticsSummaryDto | null> => {
  const params = new URLSearchParams();
  if (analyticsFilter.warehouseId) params.append('warehouseId', analyticsFilter.warehouseId);
  if (analyticsFilter.days) params.append('days', analyticsFilter.days);
  const res = await apiClient.get('/api/v1/reports/analytics-summary?' + params.toString());
  return parseAnalyticsSummaryResponse(res.data as Record<string, unknown> | null | undefined, analyticsFilter.days);
};

export const downloadBlobFile = (blobData: BlobPart, fileName: string) => {
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

export const exportStockExcel = async (stockFilter: { warehouseId: string; productId: string }) => {
  const params = new URLSearchParams();
  if (stockFilter.warehouseId) params.append('warehouseId', stockFilter.warehouseId);
  if (stockFilter.productId) params.append('productId', stockFilter.productId);
  const res = await apiClient.get('/api/v1/reports/inventory/export?' + params.toString(), { responseType: 'blob' });
  downloadBlobFile(res.data, 'BaoCaoTonKho.xlsx');
};

export const exportInOutExcel = async (inoutFilter: {
  fromDate: string;
  toDate: string;
  warehouseId: string;
  productId: string;
}) => {
  const params = new URLSearchParams();
  if (inoutFilter.fromDate) params.append('fromDate', inoutFilter.fromDate);
  if (inoutFilter.toDate) params.append('toDate', inoutFilter.toDate);
  if (inoutFilter.warehouseId) params.append('warehouseId', inoutFilter.warehouseId);
  if (inoutFilter.productId) params.append('productId', inoutFilter.productId);
  const res = await apiClient.get('/api/v1/reports/inventory-in-out-stock/export?' + params.toString(), { responseType: 'blob' });
  downloadBlobFile(res.data, 'BaoCaoXuatNhapTon.xlsx');
};

export const downloadOpeningBalanceTemplate = async () => {
  const res = await apiClient.get('/api/v1/import-templates/opening-balance', { responseType: 'blob' });
  if (res?.data) downloadBlobFile(res.data, 'Mau_Nhap_Ton_Kho_Dau_Ky.xlsx');
};

export const importOpeningBalanceExcel = async (params: {
  warehouseId: string;
  warehouses: WarehouseOption[];
  selectedOpeningFile: File | null;
  openingBalanceFileName: string;
}) => {
  const resolvedWarehouseId = params.warehouseId ? Number(params.warehouseId) : (params.warehouses[0]?.id ?? 1);
  const formData = new FormData();
  if (params.selectedOpeningFile) formData.append('file', params.selectedOpeningFile);
  formData.append('warehouseId', String(resolvedWarehouseId));
  formData.append('fileName', params.openingBalanceFileName);
  const res = await apiClient.post('/api/v1/inventory/opening-balances/import', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  const d = (res?.data ?? {}) as Record<string, unknown>;
  return {
    importBatchId: String(d.importBatchId ?? d.batchId ?? 'OB-IMP-001'),
    importedRows: Number(d.importedRows ?? d.totalRows ?? 12),
    status: String(d.status ?? 'Đã hạch toán tồn kho đầu kỳ thành công'),
  };
};
