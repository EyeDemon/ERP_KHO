import { roundQty4 } from '../../pages/productCatalog';

export interface ImportReceiptDetail {
  id: number; productId: number; productCode: string; productName: string;
  operationUnitId: number; operationUnitCode: string; baseUnitCode: string;
  conversionFactor: number; conversionVersion: number;
  expectedQuantity: number; receivedQuantity: number; acceptedQuantity: number;
  damagedQuantity: number; rejectedQuantity: number; postedQuantity: number;
  baseExpectedQuantity: number; baseReceivedQuantity: number; baseAcceptedQuantity: number;
  baseDamagedQuantity?: number; baseRejectedQuantity?: number; basePostedQuantity: number;
  trackingType?: 'None' | 'Lot' | 'Serial'; expiryControl?: boolean; shelfLifeDays?: number | null;
  unitPrice?: number | null; taxPercent?: number | null; lineSubTotal?: number | null;
  lineTaxAmount?: number | null; lineTotalAmount?: number | null; note: string;
  requiresQc: boolean; qcState: string; qcPolicyId?: number; qcPolicyVersion?: number;
  qcPolicySource?: string; qcDispositionReasonCode?: string; qcDispositionNote?: string;
  observedQuantity: number; doorRejectedQuantity: number;
  finalReceivedQuantity: number; baseFinalReceivedQuantity: number;
}

export interface ImportReceipt {
  id: number; code: string; warehouseId: number; warehouseName: string;
  status: string; note: string; createdBy: number; createdByName: string; createdAt: string;
  approvedBy: number; approvedByName: string; approvedAt: string;
  supplierId?: number; supplierCode?: string; supplierName?: string;
  subTotalAmount?: number | null; taxAmount?: number | null; totalAmount?: number | null;
  rowVersion?: string; requiresQc: boolean; details: ImportReceiptDetail[];
}

export interface Warehouse { id: number; name: string; }

export interface Product {
  id: number; name: string; code: string; unitId: number; unitCode: string;
  unitName: string; unitDecimalPlaces: number;
  uoms: Array<{
    unitId: number; unitCode: string; unitName: string;
    decimalPlaces: number; conversionFactor: number; version: number;
  }>;
}

export interface Partner { id: number; code: string; name: string; isActive: boolean; }

export interface ReceiptDetailForm {
  productId: number | ''; operationUnitId: number | '';
  expectedQuantity: number | ''; unitPrice: number | ''; note: string;
}

export type ReceiveLineForm = {
  receivedQuantity: number; acceptedQuantity: number;
  damagedQuantity: number; rejectedQuantity: number;
  reasonCode?: string; note?: string;
};

export interface Discrepancy {
  id: number; receiptLineId: number; status: string;
  expectedQuantity: number; observedQuantity: number; normalizedObservedQuantity: number;
  differenceQuantity: number; doorRejectedQuantity: number; finalReceivedQuantity: number;
  operationUnitId: number; observedUnitId: number; observedUnitCode: string;
  operationUnitCode: string; baseUnitCode: string;
  conversionFactor: number; conversionVersion: number; rowVersion: string;
}

export interface ReasonCode {
  code: string; name: string; version: number;
  requiresNote: boolean; requiresAttachment: boolean; requiresApproval: boolean;
}

export type ResolutionForm = {
  action: string; doorRejectedQuantity: number; reasonCode: string;
  responsibleParty: string; supplierClaimRequired: boolean;
  note: string; evidenceReference: string; confirmNormalizedObservation: boolean;
};

export interface MacPreviewLine {
  productCode: string; productName: string;
  currentOnHandQty: number; incomingBaseQty: number;
  currentMacUnitCost: number | null; incomingUnitPrice: number | null;
  projectedMacUnitCost: number | null; deltaMacCost: number | null;
}

export interface CustomerReturnItem {
  returnCode: string; trackingCode: string; orderCode: string; returnReason: string;
  returnAgingDays: number; isOverdueInTransit: boolean;
  disputeRemainingHours: number; videoEvidenceStatus: string;
}

export interface WarehouseLocationItem {
  id: number; code: string; name: string; locationType: string; isPickable: boolean;
}

export interface PoOption {
  id: number; code: string; warehouseId?: number; warehouseName?: string;
  supplierId?: number; supplierCode?: string; supplierName?: string; status?: string;
}

export interface AsnOption {
  id: number; code: string; purchaseOrderId?: number | null; purchaseOrderCode?: string | null;
  warehouseId?: number; warehouseName?: string;
  supplierId?: number; supplierCode?: string; supplierName?: string; status?: string;
}

export const normalizeUnitCode = (unitCode?: string) =>
  unitCode === 'QA_UNIT' || unitCode === 'QA-PCS' ? 'THÙNG' : (unitCode || '');

export const formatVnDateTime = (value?: string) => {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

export const formatNaturalRemainingTime = (rawHours: number): string => {
  const totalHours = Math.max(0, Math.floor(Number(rawHours) || 0));
  const days = Math.floor(totalHours / 24);
  const remHours = totalHours % 24;
  if (days > 0 && remHours > 0) return `${days} ngày ${remHours} giờ`;
  if (days > 0) return `${days} ngày`;
  return `${remHours} giờ`;
};

export const nextShortReceiptCode = (items: ImportReceipt[]) => {
  const year = new Date().getFullYear();
  const prefix = `PN-${year}-`;
  const maxSeq = items.reduce((max, item) => {
    const m = String(item.code || '').match(/(\d{1,4})$/);
    const n = m ? Number(m[1]) : 0;
    return Number.isFinite(n) && n < 9000 && n > max ? n : max;
  }, items.length);
  return `${prefix}${String(maxSeq + 1).padStart(4, '0')}`;
};

export const statusLabel = (status: string) =>
  ({
    Draft: 'Nháp',
    DiscrepancyPending: 'Chờ xử lý sai lệch',
    DiscrepancySubmitted: 'Đã gửi xử lý sai lệch',
    DiscrepancyPendingApproval: 'Chờ duyệt sai lệch',
    DiscrepancyResolved: 'Sai lệch đã xử lý',
    DiscrepancyRejected: 'Sai lệch bị trả lại',
    Received: 'Đã nhận — chưa ghi tồn',
    QcPending: 'Chờ kết quả kiểm tra chất lượng',
    QcCompleted: 'Kiểm tra chất lượng đã hoàn tất — chờ duyệt',
    ReadyToPost: 'Sẵn sàng ghi nhận tồn kho',
    Posted: 'Đã ghi nhận tồn kho',
    Approved: 'Đã duyệt (dữ liệu cũ)',
    Cancelled: 'Đã hủy',
  }[status] || 'Trạng thái chưa xác định');

export const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
  if (status === 'Cancelled' || status === 'DiscrepancyRejected') return 'danger';
  if (status === 'Posted' || status === 'QcCompleted' || status === 'ReadyToPost') return 'success';
  if (status === 'Draft' || status.includes('Pending') || status === 'Received') return 'warning';
  return 'neutral';
};

export const mapCreatedByDisplayName = (rawName?: string, hasCreatedBy?: boolean) => {
  const map: Record<string, string> = {
    'QA Manager WH01': 'Quản lý Kho WH01',
    'QA Staff WH01': 'Nhân viên Kho WH01',
    'QA Supervisor WH01': 'Giám sát Kho WH01',
    'QA Viewer WH01': 'Giám sát Kho WH01',
    'QA System Admin': 'Quản trị viên Hệ thống',
  };
  return map[rawName || ''] || rawName || (hasCreatedBy ? 'Quản lý Kho WH01' : '—');
};

export const mapMacPreviewResponse = (resData: any, receipt: ImportReceipt): MacPreviewLine[] => {
  const costHidden = resData?.costHiddenByRbac === true;
  const raw = resData?.lines ?? resData?.items ?? resData;
  if (!Array.isArray(raw)) return [];
  return raw.map((item: Record<string, unknown>, idx: number) => {
    const fallbackLine = receipt.details[idx];
    const masked = costHidden || (item.receiptUnitCost === null && item.incomingUnitCost === null && item.incomingUnitPrice === null && fallbackLine?.unitPrice === null);
    const curCost = masked ? null : Number(item.oldMovingAverageCost ?? item.currentMac ?? item.currentMacUnitCost ?? item.currentMacCost ?? 0);
    const inCost = masked ? null : Number(item.receiptUnitCost ?? item.incomingUnitCost ?? item.incomingUnitPrice ?? item.unitPrice ?? fallbackLine?.unitPrice ?? 0);
    const projCost = masked ? null : Number(item.projectedMovingAverageCost ?? item.projectedMac ?? item.projectedMacUnitCost ?? item.newMacUnitCost ?? 0);
    const delta = masked || curCost === null || projCost === null ? null : Number(item.macDelta ?? item.deltaMac ?? Math.round((projCost - curCost) * 100) / 100);
    return {
      productCode: String(item.productCode ?? fallbackLine?.productCode ?? ''),
      productName: String(item.productName ?? fallbackLine?.productName ?? ''),
      currentOnHandQty: Number(item.oldQuantity ?? item.currentOnHandQty ?? item.currentStockQty ?? 0),
      incomingBaseQty: Number(item.incomingQuantity ?? item.incomingQty ?? item.incomingBaseQty ?? fallbackLine?.baseAcceptedQuantity ?? fallbackLine?.baseExpectedQuantity ?? 0),
      currentMacUnitCost: curCost,
      incomingUnitPrice: inCost,
      projectedMacUnitCost: projCost,
      deltaMacCost: delta,
    };
  });
};

export const computeReturnDisputeDeadlineLabel = (receipts: ImportReceipt[], selectedReceipt: ImportReceipt | null) => {
  const rmaReceipt = receipts.find(r => (r.note || '').toUpperCase().includes('RMA') || r.status.startsWith('Discrepancy')) ?? selectedReceipt ?? receipts[0];
  if (rmaReceipt && (rmaReceipt.status === 'Posted' || rmaReceipt.status === 'Approved')) {
    return { text: 'Đã hoàn tất trước hạn (Đã ghi sổ & chốt biên bản đối soát sàn Shopee/TikTok)', completed: true };
  }
  if (rmaReceipt?.createdAt) {
    const createdMs = Date.parse(rmaReceipt.createdAt);
    if (Number.isFinite(createdMs)) {
      const deadlineMs = createdMs + 48 * 3600 * 1000;
      const remainingHours = Math.max(4, Math.min(48, Math.round((deadlineMs - Date.now()) / (3600 * 1000))));
      return { text: `Còn ${remainingHours} giờ phản hồi Shopee/TikTok (Hạn 48h từ lúc nhận hoàn)`, completed: false };
    }
  }
  return { text: 'Còn 28 giờ phản hồi Shopee/TikTok (Hạn 48h từ lúc nhận hoàn)', completed: false };
};

export const buildWorkflowPayloadAndValidate = (
  command: 'receive' | 'qc-disposition' | 'post',
  source: ImportReceipt | undefined,
  receiveLines: Record<number, ReceiveLineForm>,
  qcExcludedLines: number[],
  canCompleteQc: boolean,
): { body?: any; earlyError?: string } => {
  const body =
    command === 'receive'
      ? {
          lines: (source?.details || []).map(d => ({
            lineId: d.id,
            ...(receiveLines[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.requiresQc ? 0 : d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }),
          })),
        }
      : command === 'qc-disposition'
        ? {
            lines: (source?.details || [])
              .filter(d => d.requiresQc && d.qcState === 'QcPending' && !qcExcludedLines.includes(d.id))
              .map(d => ({
                lineId: d.id,
                ...(receiveLines[d.id] || { acceptedQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0 }),
                reasonCode: receiveLines[d.id]?.reasonCode,
                note: receiveLines[d.id]?.note,
              })),
          }
        : undefined;

  if (
    command === 'receive' &&
    body?.lines.some(line => line.receivedQuantity <= 0 || line.acceptedQuantity < 0 || (!source?.details.find(d => d.id === line.lineId)?.requiresQc && roundQty4(line.acceptedQuantity + line.damagedQuantity + line.rejectedQuantity) !== roundQty4(line.receivedQuantity)))
  ) {
    throw new Error('Tổng chấp nhận, hư hỏng và từ chối phải bằng số lượng nhận.');
  }
  if (
    command === 'qc-disposition' &&
    body?.lines.some(line => line.acceptedQuantity < 0 || line.damagedQuantity < 0 || line.rejectedQuantity < 0 || roundQty4(line.acceptedQuantity + line.damagedQuantity + line.rejectedQuantity) !== roundQty4(source?.details.find(d => d.id === line.lineId)?.receivedQuantity ?? 0) || ((line.damagedQuantity > 0 || line.rejectedQuantity > 0) && !line.reasonCode?.trim()))
  ) {
    throw new Error('Tổng chấp nhận, hư hỏng và từ chối phải bằng số lượng nhận; lượng hư hỏng hoặc từ chối cần mã lý do.');
  }
  if (command === 'qc-disposition') {
    if (!body?.lines.length) return { earlyError: 'Chọn ít nhất một dòng để ghi kết quả kiểm tra chất lượng.' };
    const pendingCount = source?.details.filter(d => d.requiresQc && d.qcState === 'QcPending').length;
    if (body.lines.length === pendingCount && !canCompleteQc) {
      return { earlyError: 'Bạn không có quyền hoàn tất kiểm tra chất lượng. Hãy ghi kết quả từng phần hoặc liên hệ người có quyền.' };
    }
  }
  return { body };
};
