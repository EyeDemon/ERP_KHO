import { conflictErrorMessage } from '../../services/permissionPresentation';

export interface ExportReceiptDetail {
  id: number;
  productCode: string;
  productName: string;
  unitName?: string;
  quantity: number;
  unitPrice?: number | null;
  taxPercent?: number;
  lineSubTotal?: number;
  lineTaxAmount?: number;
  lineTotalAmount?: number | null;
  note?: string;
}

export interface ExportReceipt {
  id: number;
  code: string;
  warehouseName: string;
  status: string;
  note?: string;
  createdBy: number;
  createdByName: string;
  createdAt: string;
  approvedBy?: number;
  approvedByName?: string;
  approvedAt?: string;
  dispatchedByName?: string;
  dispatchedAt?: string;
  dispatchMode?: string;
  reservationStatus?: string;
  allowPerReceiptDispatchMode?: boolean;
  allowWarehouseStaffDirectDispatch: boolean;
  writeEnabled: boolean;
  details: ExportReceiptDetail[];
  customerId?: number;
  customerCode?: string;
  customerName?: string;
  salesOrderId?: number;
  shipmentId?: number;
  subTotalAmount?: number;
  taxAmount?: number;
  totalAmount?: number | null;
  marketplaceOrderId?: string | null;
  carrierCode?: string | null;
  trackingNumber?: string | null;
  rowVersion?: string;
}

export interface Warehouse {
  id: number;
  name: string;
}

export interface Product {
  id: number;
  name: string;
  code: string;
}

export interface Partner {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

export interface ReceiptDetailForm {
  productId: number | '';
  quantity: number | '';
  unitPrice: number | '';
  note: string;
}

export interface AccountingEinvoiceData {
  outboxEventId?: string;
  exportReceiptCode?: string;
  accountingProvider?: string;
  einvoiceProvider?: string;
  invoiceTemplateCode?: string;
  invoiceSeries?: string;
  invoiceNumber?: string;
  totalCogsAmount?: number | null;
  totalRevenueAmount?: number;
  totalVatAmount?: number;
  totalReceivableAmount?: number;
  totalDebitAmount?: number | null;
  totalCreditAmount?: number | null;
  journalImbalanceDelta?: number;
  statusLabel?: string;
  circular200JournalEntries?: Array<{
    entryType: string;
    descriptionVi: string;
    debitAccount: string;
    creditAccount: string;
    debitAmount: number | null;
    creditAmount: number | null;
  }>;
}

export interface AwbLabelItem {
  awbCode: string;
  carrierName: string;
  marketplaceOrderId: string;
  pickSequenceFooter: string;
  printCountLabel: string;
  isReprint: boolean;
  watermarkText: string;
}

export type ExportSectionTab = 'ALL' | 'LIST' | 'BATCH_AWB' | 'CREATE';
export type ExportStatusQuickFilter = 'ALL' | 'Draft' | 'Approved' | 'Dispatched' | 'Cancelled' | 'OverdueHandover';

export const safeRequestError = (error: any, fallback: string): string => {
  const status = error?.response?.status;
  if (status === 401) return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  if (status === 403) return 'Bạn không còn quyền thực hiện thao tác này.';
  if (status === 404) return 'Dữ liệu không còn tồn tại hoặc bạn không có quyền truy cập.';
  if (status === 409) return conflictErrorMessage(error, 'Dữ liệu đã thay đổi. Danh sách đã được làm mới, vui lòng kiểm tra lại.');
  if (status === 400) return 'Dữ liệu phiếu xuất không hợp lệ. Vui lòng kiểm tra lại.';
  if (typeof status === 'number' && status >= 500) return 'Hệ thống đang bận. Vui lòng thử lại.';
  return fallback;
};

export const dispatchModeLabel = (mode?: string): string => ({
  RequireSeparateDispatch: 'Duyệt và giữ hàng → xác nhận xuất kho',
  DispatchOnApproval: 'Duyệt và xuất ngay (tương thích)',
}[mode || ''] || 'Không xác định');

export const reservationStatusLabel = (status?: string): string => ({
  Active: 'Đang giữ hàng',
  PartiallyConsumed: 'Đã tiêu thụ một phần',
  Consumed: 'Đã tiêu thụ',
  Released: 'Đã giải phóng',
  Cancelled: 'Đã giải phóng',
  Expired: 'Đã hết hạn',
  PendingApproval: 'Chờ duyệt',
}[status || ''] || 'Không xác định');

export const statusTone = (status: string): 'neutral' | 'info' | 'success' | 'warning' | 'danger' => {
  if (status === 'Draft') return 'neutral';
  if (status === 'Approved') return 'info';
  if (status === 'Dispatched' || status === 'Completed') return 'success';
  if (status === 'Cancelled') return 'danger';
  if (status === 'CancelPendingPutback') return 'warning';
  return 'neutral';
};

export const statusLabel = (status: string): string => ({
  Draft: 'Nháp',
  Approved: 'Đã duyệt và giữ hàng',
  Dispatched: 'Đã xuất kho',
  CancelPendingPutback: 'Chờ cất trả hàng hủy về kệ',
  Cancelled: 'Đã hủy',
  Completed: 'Hoàn tất',
}[status] || 'Không xác định');
