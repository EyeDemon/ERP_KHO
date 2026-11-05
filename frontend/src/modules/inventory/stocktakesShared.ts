import React from 'react';
import { roundQty4 } from '../../pages/productCatalog';

export const inactiveTabPanelStyle: React.CSSProperties = {
  display: 'none',
};

export interface StocktakeSummary {
  id: number;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: number | string;
  note?: string;
  createdBy: number;
  createdByName: string;
  approvedBy?: number;
  approvedByName?: string;
  createdAt: string;
  approvedAt?: string;
  detailCount: number;
  isBlindCount?: boolean;
}

export interface StocktakeDetailRow {
  id: number;
  stocktakeId: number;
  productId: number;
  locationId?: number;
  locationCode?: string;
  locationName?: string;
  productCode: string;
  productName: string;
  unitName: string;
  systemQuantity: number;
  actualQuantity?: number;
  differenceQuantity: number;
  note?: string;
}

export interface StocktakeResponse extends StocktakeSummary {
  details: StocktakeDetailRow[];
}

export interface StocktakeAutoApprovalItem {
  id: number;
  productCode: string;
  productName: string;
  isBlindCount: boolean;
  systemQuantity: number;
  attempt1Quantity: number | null;
  attempt2Quantity: number | null;
  variancePct: number;
  varianceValueVnd: number;
  autoApproved: boolean;
}

export interface Warehouse {
  id: number;
  name: string;
  isActive: boolean;
}

export interface LocationOption {
  id: number;
  code: string;
  name: string;
}

export const PAGE_SIZE = 10;

export const normalizeStatusCode = (status: number | string | undefined | null): number => {
  if (typeof status === 'number' && Number.isFinite(status)) return status;
  const raw = String(status ?? '').trim();
  if (/^[0-7]$/.test(raw)) return Number(raw);
  const upper = raw.toUpperCase().replace(/[\s_-]+/g, '');
  switch (upper) {
    case 'DRAFT':
      return 0;
    case 'APPROVED':
      return 1;
    case 'CANCELLED':
    case 'CANCELED':
      return 2;
    case 'INPROGRESS':
    case 'COUNTING':
      return 3;
    case 'COUNTCOMPLETED':
    case 'COUNTED':
      return 4;
    case 'PENDINGAPPROVAL':
    case 'AWAITINGAPPROVAL':
      return 5;
    case 'COMPLETED':
    case 'ADJUSTED':
    case 'POSTED':
      return 6;
    case 'REJECTED':
      return 7;
    default:
      return 0;
  }
};

export const getStatusText = (status: number | string): string => {
  switch (normalizeStatusCode(status)) {
    case 0:
      return 'Nháp';
    case 1:
      return 'Đã duyệt';
    case 2:
      return 'Đã hủy';
    case 3:
      return 'Đang kiểm đếm';
    case 4:
      return 'Hoàn tất đếm';
    case 5:
      return 'Chờ phê duyệt';
    case 6:
      return 'Hoàn tất điều chỉnh';
    case 7:
      return 'Từ chối';
    default:
      return 'Nháp';
  }
};

export const getStatusBadgeStyle = (status: number | string): React.CSSProperties => {
  const code = normalizeStatusCode(status);
  if (code === 1 || code === 4 || code === 6) {
    return { background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0' };
  }
  if (code === 2 || code === 7) {
    return { background: '#fef2f2', color: '#991b1b', border: '1px solid #fecaca' };
  }
  return { background: '#fffbeb', color: '#92400e', border: '1px solid #fde68a' };
};

export const statusTone = (status: number | string): 'neutral' | 'success' | 'warning' | 'danger' => {
  const code = normalizeStatusCode(status);
  if (code === 0 || code === 3 || code === 5) return 'warning';
  if (code === 1 || code === 4 || code === 6) return 'success';
  if (code === 2 || code === 7) return 'danger';
  return 'neutral';
};

export const mapAutoApprovalRows = (
  rawItems: unknown[],
  fallbackDetails?: StocktakeDetailRow[],
  isBlind?: boolean,
): StocktakeAutoApprovalItem[] => {
  const source = Array.isArray(rawItems) && rawItems.length > 0 ? rawItems : (fallbackDetails ?? []);
  return source.map((raw: unknown, idx: number) => {
    const item = (raw ?? {}) as Record<string, unknown>;
    const systemQty = Number(item.systemQuantity ?? item.systemQty ?? item.expectedQuantity ?? 100);
    const historyArr = Array.isArray(item.attemptHistory) ? (item.attemptHistory as Record<string, unknown>[]) : [];
    const historyAttempt1 = historyArr.length > 0 && historyArr[0]?.countedQty !== undefined ? Number(historyArr[0].countedQty) : null;
    const historyAttempt2 =
      historyArr.length > 1 && historyArr[historyArr.length - 1]?.countedQty !== undefined
        ? Number(historyArr[historyArr.length - 1].countedQty)
        : null;
    const attempt1 =
      item.attempt1Quantity !== undefined && item.attempt1Quantity !== null
        ? Number(item.attempt1Quantity)
        : item.firstCountQty !== undefined && item.firstCountQty !== null
          ? Number(item.firstCountQty)
          : item.countAttempt1 !== undefined && item.countAttempt1 !== null
            ? Number(item.countAttempt1)
            : historyAttempt1 !== null
              ? historyAttempt1
              : item.actualQuantity !== undefined && item.actualQuantity !== null
                ? Number(item.actualQuantity)
                : item.countedQty !== undefined && item.countedQty !== null
                  ? Number(item.countedQty)
                  : systemQty;
    const attempt2 =
      item.attempt2Quantity !== undefined && item.attempt2Quantity !== null
        ? Number(item.attempt2Quantity)
        : item.secondCountQty !== undefined && item.secondCountQty !== null
          ? Number(item.secondCountQty)
          : item.recountQty !== undefined && item.recountQty !== null
            ? Number(item.recountQty)
            : item.countAttempt2 !== undefined && item.countAttempt2 !== null
              ? Number(item.countAttempt2)
              : historyAttempt2 !== null
                ? historyAttempt2
                : item.countedQty !== undefined && item.countedQty !== null
                  ? Number(item.countedQty)
                  : attempt1;
    const effectiveCount = attempt2 ?? attempt1 ?? systemQty;
    const diffQty = roundQty4(effectiveCount - systemQty);
    const computedPct = systemQty > 0 ? Math.round((Math.abs(diffQty) / systemQty) * 10000) / 100 : 0;
    const variancePct = Number(item.variancePct ?? item.variancePercent ?? item.diffPercent ?? computedPct);
    const unitCost = Number(item.unitCost ?? item.movingAverageUnitCost ?? 120000);
    const varianceValueVnd = Number(
      item.varianceValueVnd ?? item.varianceAmount ?? item.diffValueVnd ?? Math.abs(Math.round(diffQty * unitCost)),
    );
    const autoApproved =
      typeof item.autoApproved === 'boolean'
        ? item.autoApproved
        : typeof item.isAutoApproved === 'boolean'
          ? item.isAutoApproved
          : typeof item.autoApproveEligible === 'boolean'
            ? item.autoApproveEligible
            : Math.abs(variancePct) < 1 && Math.abs(varianceValueVnd) <= 500000;

    return {
      id: Number(item.id ?? item.detailId ?? idx + 1),
      productCode: String(item.productCode ?? item.sku ?? `SKU-${idx + 1}`),
      productName: String(item.productName ?? item.name ?? 'Sản phẩm kiểm kê'),
      isBlindCount: Boolean(item.isBlindCount ?? isBlind ?? false),
      systemQuantity: systemQty,
      attempt1Quantity: attempt1,
      attempt2Quantity: attempt2,
      variancePct,
      varianceValueVnd,
      autoApproved,
    };
  });
};
