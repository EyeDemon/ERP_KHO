import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';

export type OrderLine = {
  id: number;
  externalLineId: string;
  productId: number;
  productCode: string;
  productName: string;
  uomCode: string;
  orderedQuantity: number;
  reservedQuantity: number;
  allocatedQuantity: number;
  pickedQuantity: number;
  shippedQuantity: number;
  backorderQuantity: number;
  cancelledQuantity: number;
  openQuantity: number;
  unitPrice?: number;
  taxPercent?: number;
  lineSubTotal?: number;
  lineTaxAmount?: number;
  lineTotalAmount?: number;
};

export type SalesOrder = {
  id: number;
  orderCode: string;
  externalOrderId: string;
  customerId: number;
  customerCode: string;
  customerName: string;
  warehouseId: number;
  warehouseName: string;
  requestedShipDate?: string;
  priority: number;
  shippingMethod?: string;
  currency?: string;
  subTotalAmount?: number;
  taxAmount?: number;
  totalAmount?: number;
  status: string;
  orderedQuantity: number;
  reservedQuantity: number;
  allocatedQuantity: number;
  pickedQuantity: number;
  shippedQuantity: number;
  backorderQuantity: number;
  cancelledQuantity: number;
  openQuantity: number;
  createdAt: string;
  releasedAt?: string;
  rowVersion?: string;
  lines?: OrderLine[];
};

export type Backorder = {
  id: number;
  backorderCode: string;
  salesOrderId: number;
  orderCode: string;
  externalOrderId: string;
  salesOrderLineId: number;
  warehouseId: number;
  warehouseName: string;
  productId: number;
  productCode: string;
  productName: string;
  orderedQuantity: number;
  quantity: number;
  recoveredQuantity: number;
  cancelledQuantity: number;
  remainingQuantity: number;
  status: string;
  createdAt: string;
  updatedAt?: string;
  rowVersion: string;
};

export type Partner = { id: number; code: string; name: string; isCustomer: boolean; isActive: boolean };
export type Warehouse = { id: number; code: string; name: string; isActive: boolean };
export type Product = { id: number; code: string; name: string; isActive: boolean };
export type CreateLine = {
  externalLineId: string;
  productId: string;
  quantity: string;
  unitPrice?: string;
  taxPercent?: string;
};

export type DomSplitAllocation = {
  orderCode: string;
  productCode: string;
  warehouseName: string;
  allocatedQuantity: number;
  estimatedPackagesCount: number;
  routingStrategyLabel: string;
};

export const orderLabels: Record<string, string> = {
  Draft: 'Nháp',
  Hold: 'Tạm giữ',
  Released: 'Đã phát hành',
  PartiallyFulfilled: 'Giao một phần',
  Fulfilled: 'Hoàn tất',
  Cancelled: 'Đã hủy',
};

export const backorderLabels: Record<string, string> = {
  Open: 'Mở',
  PartiallyAllocated: 'Khôi phục một phần',
  Allocated: 'Đã khôi phục',
  Fulfilled: 'Đã giao',
  Cancelled: 'Đã hủy',
};

export const outboundSignalLabels: Record<string, string> = {
  'SO-PRIORITY-QUEUE': 'Hàng đợi ưu tiên cấp phát',
  PROMISE_DATE_VERIFIED_READONLY: 'Đã xác thực ngày giao cam kết',
  OPTIMAL_WAREHOUSE_NODE_SELECTED: 'Đã chọn điểm kho hoàn tất tối ưu',
  PROPORTIONAL_SERVICE_TIER_FAIRNESS: 'Phân bổ công bằng theo phân hạng dịch vụ',
  TIER_1_VIP_AND_STANDARD_SLA_ACTIVE: 'Đang áp dụng cam kết SLA khách hàng ưu tiên & tiêu chuẩn',
  ATP_ROUTING_FAIRNESS_READY: 'Đã hoàn tất tính toán ATP/CTP & định tuyến kho',
  ALLOCATION_WAVE_QUEUED: 'Đã xếp hàng đợt cấp phát ưu tiên',
};

export const translateOutboundSignal = (value?: string | null) =>
  value ? (outboundSignalLabels[value] ?? value) : '';

export const tone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  ['Fulfilled'].includes(status)
    ? 'success'
    : status === 'Cancelled'
      ? 'danger'
      : ['Hold', 'PartiallyFulfilled', 'PartiallyAllocated'].includes(status)
        ? 'warning'
        : 'neutral';

export const messageOf = (failure: unknown) => {
  const response = (failure as { response?: { status?: number; data?: { message?: string; code?: string } } })?.response;
  const code = response?.data?.code;
  if (code === 'SALES_ORDER_CREDIT_LIMIT_EXCEEDED') return response?.data?.message ?? 'Khách hàng vượt hạn mức tín dụng cho phép.';
  if (code === 'BACKORDER_INSUFFICIENT_AVAILABLE') return 'Chưa có tồn khả dụng để recover Backorder.';
  if (code === 'BACKORDER_EXECUTION_STARTED') return 'Picking đã bắt đầu. Không thể khôi phục Backorder tự động sau khi tác vụ lấy hàng đã thực thi.';
  if (code === 'SALES_ORDER_ALREADY_RELEASED') return 'Sales Order đã được release hoặc không còn ở trạng thái cho phép release.';
  if (code === 'SALES_ORDER_DUPLICATE') return 'External Order ID đã tồn tại.';
  if (response?.status === 403) return 'Bạn không có quyền thực hiện thao tác Demand/Backorder này.';
  if (response?.status === 409) return response.data?.message ?? 'Demand đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message ?? 'Không thể xử lý Demand/Backorder.';
};

export const emptyLine = (index: number): CreateLine => ({
  externalLineId: String(index),
  productId: '',
  quantity: '1',
  unitPrice: '1000',
  taxPercent: '0',
});

export const formatShortOrderCode = (raw?: string | null): string => {
  if (!raw) return '—';
  if (raw.length <= 14) return raw;
  return `${raw.slice(0, 11)}...${raw.slice(-3)}`;
};

export const qtyCellStyle = (value: number) => ({
  textAlign: 'right' as const,
  fontVariantNumeric: 'tabular-nums' as const,
  color: value === 0 ? '#94a3b8' : '#0f172a',
  fontWeight: value === 0 ? 400 : 700,
});

export const buildDefaultDomAllocations = (
  mode: 'MULTI_WAREHOUSE_SPLIT' | 'SINGLE_WAREHOUSE_MATCH',
  orderCode: string,
  productCode: string,
  qty: number,
): DomSplitAllocation[] => {
  const cleanOrder = orderCode.trim() || 'SO-QA-001';
  const cleanProduct = productCode.trim() || 'QA-E2E-001';
  const totalQty = Math.max(1, qty);
  const wh1Label = CANONICAL_WH01_LABEL;
  const wh2Label = formatWarehouseLabel('WH02 — Kho Chi Nhánh Hà Nội', 'WH02', 2);
  if (mode === 'SINGLE_WAREHOUSE_MATCH') {
    return [
      {
        orderCode: cleanOrder,
        productCode: cleanProduct,
        warehouseName: wh1Label,
        allocatedQuantity: totalQty,
        estimatedPackagesCount: 1,
        routingStrategyLabel: `Đủ tồn 1 kho • ${wh1Label} (${totalQty} ĐV)`,
      },
    ];
  }
  const wh1Qty = totalQty >= 2 ? Math.ceil(totalQty * 0.6) : totalQty;
  const wh2Qty = Math.max(1, totalQty - wh1Qty);
  return [
    {
      orderCode: `${cleanOrder}-SUB1`,
      productCode: cleanProduct,
      warehouseName: wh1Label,
      allocatedQuantity: wh1Qty,
      estimatedPackagesCount: 1,
      routingStrategyLabel: 'Tách đơn đa kho (Chặng 1/2) • Ưu tiên tồn khả dụng & khoảng cách',
    },
    {
      orderCode: `${cleanOrder}-SUB2`,
      productCode: cleanProduct,
      warehouseName: wh2Label,
      allocatedQuantity: wh2Qty,
      estimatedPackagesCount: 1,
      routingStrategyLabel: 'Tách đơn đa kho (Chặng 2/2) • Kho chi nhánh hỗ trợ hoàn tất đơn',
    },
  ];
};
