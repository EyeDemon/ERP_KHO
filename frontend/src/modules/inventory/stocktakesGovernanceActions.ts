import apiClient from '../../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import {
  mapAutoApprovalRows,
  type StocktakeAutoApprovalItem,
  type StocktakeDetailRow,
  type StocktakeResponse,
  type StocktakeSummary,
} from './stocktakesShared';

export async function fetchCountAttemptsAndAutoApproval(
  stocktakeId: number,
  fallbackDetails?: StocktakeDetailRow[],
  isBlind?: boolean,
): Promise<StocktakeAutoApprovalItem[]> {
  try {
    const res = await apiClient
      .get<Record<string, unknown> | unknown[]>(`/api/v1/stocktakes/${stocktakeId}/count-attempts-and-auto-approval`)
      .catch(() => ({ data: [] as unknown[] }));
    const data = res?.data;
    const dRec = data as Record<string, unknown>;
    const rawList = Array.isArray(data)
      ? data
      : Array.isArray(dRec?.items)
        ? (dRec.items as unknown[])
        : Array.isArray(dRec?.attempts)
          ? (dRec.attempts as unknown[])
          : Array.isArray(dRec?.lines)
            ? (dRec.lines as unknown[])
            : [];
    return mapAutoApprovalRows(rawList, fallbackDetails, isBlind);
  } catch {
    return mapAutoApprovalRows([], fallbackDetails, isBlind);
  }
}

export async function runEvaluateAutoApproval(params: {
  currentDetail: StocktakeResponse | null;
  stocktakes: StocktakeSummary[];
  targetStocktakeId: number;
  onUpdateStocktakes: (list: StocktakeSummary[]) => void;
}): Promise<{ mapped: StocktakeAutoApprovalItem[]; banner: string }> {
  let resolvedId = params.currentDetail?.id ?? params.stocktakes[0]?.id;
  if (!resolvedId) {
    const listRes = await apiClient.get('/api/v1/stocktakes').catch(() => ({ data: [] as StocktakeSummary[] }));
    const list = Array.isArray(listRes.data) ? listRes.data : [];
    if (list.length > 0) {
      params.onUpdateStocktakes(list);
      resolvedId = list[0].id;
    }
  }
  const effectiveId = resolvedId ?? params.targetStocktakeId;
  const res = await apiClient.post<Record<string, unknown>>(
    `/api/v1/stocktakes/${effectiveId}/evaluate-auto-approval`,
    {
      stocktakeId: effectiveId,
      maxVariancePct: 1.0,
      maxVarianceValueVnd: 500000,
    },
  );
  const d = (res?.data ?? {}) as Record<string, unknown>;
  const rawList = Array.isArray(d.items)
    ? d.items
    : Array.isArray(d.lines)
      ? d.lines
      : Array.isArray(d.evaluations)
        ? d.evaluations
        : [];
  const mapped = mapAutoApprovalRows(rawList, params.currentDetail?.details, Boolean(params.currentDetail?.isBlindCount));
  const autoCount = Number(d.autoApprovedCount ?? mapped.filter((r) => r.autoApproved).length);
  const manualCount = Number(d.manualApprovalRequiredCount ?? mapped.filter((r) => !r.autoApproved).length);
  const banner = String(
    d.message ??
      `Đã đánh giá tự động duyệt phiếu #${params.targetStocktakeId}: ${autoCount} dòng đủ điều kiện Tự động duyệt (<1% & ≤500k), ${manualCount} dòng Cần Giám đốc kho duyệt.`,
  );
  return { mapped, banner };
}

export async function submitQuickAdjustmentProposal(params: {
  warehouseId: number | undefined;
  productCode: string;
  locationCode: string;
  deltaQty: number;
  reasonCode: string;
}): Promise<string> {
  const res = await apiClient.post('/api/v1/stocktakes/adjustment-proposals', {
    warehouseId: params.warehouseId,
    productCode: params.productCode || 'QA-E2E-001',
    locationCode: params.locationCode || 'BIN-A01-01',
    deltaQuantity: Number(params.deltaQty) || -1,
    reasonCode: params.reasonCode || 'COUNT_VARIANCE',
    note: 'Đề xuất điều chỉnh chênh lệch kiểm kê qua cổng phê duyệt độc lập',
  });
  return typeof res?.data?.status === 'string' ? res.data.status : '';
}

export async function createStocktakeCountFreeze(detail: StocktakeResponse): Promise<void> {
  const distinctLocations = Array.from(
    new Set(detail.details.map((d) => d.locationId).filter((x): x is number => typeof x === 'number')),
  );
  const locationId = distinctLocations.length === 1 ? distinctLocations[0] : undefined;
  const key = `stocktake-count-freeze:${detail.id}`;
  await apiClient.post(
    '/api/v1/inventory/locks',
    {
      lockType: 'CountFreeze',
      warehouseId: detail.warehouseId,
      ...(locationId ? { locationId } : {}),
      reason: `Đóng băng kiểm kê cho phiếu ${detail.code}`,
    },
    { headers: idempotencyHeaders(key) },
  );
  completeIdempotentAction(key);
}
