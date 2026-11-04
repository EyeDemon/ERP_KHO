export type Warehouse = { id: number; code: string; name: string };

export type MapItem = {
  locationId: number;
  code: string;
  name: string;
  structurePath?: string | null;
  storageClass?: string | null;
  mapX?: number | null;
  mapY?: number | null;
  mapWidth?: number | null;
  mapHeight?: number | null;
  utilizationPercent?: number | null;
  capacityState: string;
  recentMovementCount: number;
  activePutawayCount: number;
  activityLevel: string;
  isActive: boolean;
  isBlocked: boolean;
  rowVersion?: string | null;
};

export type MapResponse = { warehouseId: number; generatedAtUtc: string; items: MapItem[] };

export type LayoutForm = {
  locationId: number;
  code: string;
  x: string;
  y: string;
  width: string;
  height: string;
  rowVersion: string;
};

export type ProductivityAndBiSnapshot = {
  completedPutawaysCount: number;
  completedPicksCount: number;
  completedPacksCount: number;
  totalMovesCount: number;
  ledgerWatermarkId: number;
  auditWatermarkId: number;
  stockSnapshotRowCount: number;
  readyForBiExport: boolean;
  generatedAtUtc?: string;
};

export const mapped = (item: MapItem) =>
  item.mapX != null && item.mapY != null && item.mapWidth != null && item.mapHeight != null;

export const capacityTone = (item: MapItem) =>
  item.capacityState === 'OverCapacity' || item.capacityState === 'CompatibilityConflict'
    ? ('danger' as const)
    : item.capacityState === 'NearCapacity' || item.capacityState === 'ProfileIncomplete'
      ? ('warning' as const)
      : item.capacityState === 'Blocked'
        ? ('danger' as const)
        : item.capacityState === 'Inactive'
          ? ('neutral' as const)
          : ('success' as const);

export const heatClass = (item: MapItem, mode: 'utilization' | 'activity') => {
  if (!item.isActive) return 'neutral';
  if (item.isBlocked) return 'danger';
  if (mode === 'activity') {
    if (item.activityLevel === 'High') return 'danger';
    if (item.activityLevel === 'Medium') return 'warning';
    return 'success';
  }
  if (item.capacityState === 'OverCapacity' || item.capacityState === 'CompatibilityConflict') return 'danger';
  if (item.capacityState === 'ProfileIncomplete') return 'warning';
  const value = item.utilizationPercent ?? 0;
  if (value >= 90) return 'danger';
  if (value >= 75) return 'warning';
  return 'success';
};

export const stateLabel: Record<string, string> = {
  Available: 'Khả dụng',
  NearCapacity: 'Gần đầy',
  OverCapacity: 'Vượt sức chứa',
  ProfileIncomplete: 'Thiếu thông số',
  CompatibilityConflict: 'Xung đột phân lớp',
  Blocked: 'Bị khóa',
  Inactive: 'Ngừng hoạt động',
};

export const activityLabel: Record<string, string> = {
  High: 'Cao',
  Medium: 'Trung bình',
  Low: 'Thấp',
};

export const errorMessage = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  if (response.response?.status === 409) return (response.response.data?.message || 'Dữ liệu đã thay đổi.') + ' Vui lòng tải lại bản đồ.';
  return response.response?.data?.message || fallback;
};
