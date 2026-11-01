export type ShipmentHu = {
  id: number;
  handlingUnitId: number;
  huCode: string;
  barcode: string;
  sscc?: string;
  type: string;
  status: string;
  sequence: number;
  contentQuantity: number;
  assignedAt: string;
  stagedAt?: string;
  loadedAt?: string;
};

export type ShipmentTrackingEvent = {
  id: number;
  eventType: string;
  fromStatus: string;
  toStatus: string;
  occurredAt: string;
  recordedAt: string;
  source: string;
  sourceEventId?: string;
  reasonCode?: string;
  note?: string;
};

export type ShipmentProofOfDelivery = {
  deliveredAt: string;
  receiverName: string;
  evidenceReference?: string;
  latitude?: number;
  longitude?: number;
  carrierReference?: string;
  deliveryNote?: string;
  createdAt: string;
};

export type Shipment = {
  id: number;
  shipmentCode: string;
  packingSessionId: number;
  packingSessionCode: string;
  warehouseId: number;
  warehouseName: string;
  sourceType: string;
  sourceId?: number;
  sourceCode?: string;
  exportReceiptId?: number;
  exportReceiptCode?: string;
  status: string;
  stagingLocationId?: number;
  stagingLocationCode?: string;
  handlingUnitCount: number;
  loadedHandlingUnitCount: number;
  dockAppointmentId?: number;
  dockAppointmentCode?: string;
  dockId?: number;
  dockCode?: string;
  carrierName?: string;
  vehiclePlate?: string;
  trailerPlate?: string;
  sealNumber?: string;
  createdAt: string;
  stagedAt?: string;
  loadingStartedAt?: string;
  loadedAt?: string;
  dispatchedAt?: string;
  dispatchedBy?: number;
  dispatchedByName?: string;
  inTransitAt?: string;
  deliveryFailedAt?: string;
  returnInitiatedAt?: string;
  deliveredAt?: string;
  completedAt?: string;
  rowVersion?: string;
  proofOfDelivery?: ShipmentProofOfDelivery;
  trackingEvents?: ShipmentTrackingEvent[];
  handlingUnits?: ShipmentHu[];
};

export type DockAppointment = {
  id: number;
  code: string;
  warehouseId: number;
  direction: number | string;
  status: number | string;
  dockId?: number;
  dockCode?: string;
  vehiclePlate?: string;
  trailerPlate?: string;
  carrierName?: string;
};

export type TmsManifestResult = {
  manifestId: string;
  carrierCode: string;
  syncedShipmentCount: number;
  totalHandlingUnitsCount: number;
  totalGrossWeightKg: number;
  totalVolumeM3: number;
  ediStatus: string;
  statusLabel?: string;
};

export const statusLabels: Record<string, string> = {
  Draft: 'Nháp',
  Ready: 'Sẵn sàng',
  Staging: 'Đang tập kết ra cửa xuất',
  Loading: 'Đang xếp hàng lên xe',
  Loaded: 'Đã xếp lên xe',
  Dispatched: 'Đã dispatch',
  InTransit: 'Đang vận chuyển',
  Delivered: 'Đã giao',
  DeliveryFailed: 'Giao thất bại',
  ReturnToWarehouse: 'Đang trả về kho',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
};

export const huLabels: Record<string, string> = {
  InUse: 'Đang đóng gói',
  Closed: 'Đã đóng',
  Staged: 'Đã tập kết ra cửa xuất',
  Loaded: 'Đã xếp lên xe',
  Shipped: 'Đã xuất giao',
};

export const huTypeLabels: Record<string, string> = {
  Carton: 'Thùng carton',
  Pallet: 'Pallet',
  Tote: 'Khay nhựa (Tote)',
  Box: 'Hộp',
};

export const trackingEventLabels: Record<string, string> = {
  ShipmentCreated: 'Khởi tạo chuyến giao hàng',
  Staged: 'Đã đưa vào vị trí tập kết',
  ShipmentStaged: 'Đã đưa vào vị trí tập kết',
  LoadingStarted: 'Bắt đầu xếp hàng lên xe',
  HuLoaded: 'Đã xếp hàng hoàn tất',
  Loaded: 'Đã xếp hàng hoàn tất',
  LoadingCompleted: 'Đã xếp hàng hoàn tất',
  Dispatched: 'Đã xuất bến giao hàng',
  ShipmentDispatched: 'Đã xuất bến giao hàng',
  InTransit: 'Đang trên đường vận chuyển',
  Delivered: 'Đã giao hàng thành công',
  DeliveryFailed: 'Giao hàng không thành công',
  ReturnInitiated: 'Khởi tạo hoàn trả về kho',
  ReturnToWarehouse: 'Khởi tạo hoàn trả về kho',
};

export const trackingSourceLabels: Record<string, string> = {
  Internal: 'Hệ thống nội bộ',
  CarrierWebhook: 'Hãng vận chuyển',
  Manual: 'Cập nhật thủ công',
};

export const tone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  ['Loaded', 'Dispatched', 'Delivered', 'Completed'].includes(status)
    ? 'success'
    : ['DeliveryFailed', 'Cancelled'].includes(status)
      ? 'danger'
      : ['Staging', 'Loading', 'InTransit', 'ReturnToWarehouse'].includes(status)
        ? 'warning'
        : 'neutral';

export const messageFor = (e: unknown) => {
  const response = (e as { response?: { status?: number; data?: { message?: string; code?: string } } })?.response;
  const code = response?.data?.code;
  if (code === 'SHIPMENT_DOCK_APPOINTMENT_INVALID') return response?.data?.message ?? 'Outbound appointment chưa sẵn sàng để loading.';
  if (code === 'SHIPMENT_HU_NOT_LOADED') return 'Chưa load đủ Handling Unit của Shipment.';
  if (code === 'SHIPMENT_HU_NOT_STAGED') return 'Handling Unit chưa ở staging.';
  if (code === 'SHIPMENT_STAGING_LOCATION_INVALID') return response?.data?.message ?? 'Staging location không hợp lệ.';
  if (code === 'SHIPMENT_HU_MISMATCH') return 'Handling Unit không thuộc Shipment này hoặc bạn đang quét HU con.';
  if (code === 'SHIPMENT_ALLOCATION_MISMATCH') return response?.data?.message ?? 'Allocation/Reservation không còn khớp Shipment.';
  if (code === 'SHIPMENT_INVENTORY_MISMATCH') return response?.data?.message ?? 'Inventory bucket không còn khớp Allocation.';
  if (code === 'SHIPMENT_PICKING_NOT_READY') return response?.data?.message ?? 'Picking chưa đủ điều kiện dispatch.';
  if (code === 'SHIPMENT_SOURCE_STATE_INVALID') return response?.data?.message ?? 'Chứng từ nguồn không còn ở trạng thái cho phép dispatch.';
  if (code === 'HU_NOT_FOUND') return 'Không tìm thấy Handling Unit theo mã đã quét.';
  if (code === 'HU_ALREADY_LOADED') return 'Handling Unit này đã được load.';
  if (code === 'SHIPMENT_TRACKING_TIME_INVALID') return response?.data?.message ?? 'Thời điểm tracking không hợp lệ.';
  if (code === 'SHIPMENT_POD_EXISTS') return 'Shipment đã có Proof of Delivery.';
  if (code === 'SHIPMENT_POD_REQUIRED') return 'Cần Proof of Delivery trước khi hoàn tất Shipment.';
  if (code === 'SHIPMENT_NOT_DISPATCHED') return 'Shipment chưa qua inventory dispatch boundary.';
  if (code === 'SHIPMENT_STATE_INVALID') return response?.data?.message ?? 'Trạng thái Shipment không cho phép thao tác này.';
  if (code === 'SHIPMENT_VERSION_CONFLICT' || response?.status === 409) return response?.data?.message ?? 'Shipment đã thay đổi. Vui lòng tải lại.';
  if (response?.status === 403) return 'Bạn không có quyền thực hiện thao tác Shipment này.';
  if (response?.status === 404) return 'Không tìm thấy Shipment trong phạm vi kho của bạn.';
  return response?.data?.message ?? 'Không thể xử lý Shipment.';
};
