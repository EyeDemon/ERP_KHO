export type Warehouse = {
  id: number;
  code: string;
  name: string;
};

export type WarehouseLocation = {
  id: number;
  warehouseId: number;
  code: string;
  name: string;
  structurePath?: string | null;
  storageClass?: string | null;
  maxWeightKg?: number | null;
  maxVolumeM3?: number | null;
  maxPalletEquivalent?: number | null;
  locationType: string;
  isActive: boolean;
  isBlocked: boolean;
  isPickable: boolean;
  isReceivable: boolean;
  isSystemManaged: boolean;
  rowVersion?: string | null;
};

export type LocationForm = {
  id?: number;
  code: string;
  name: string;
  locationType: 'Storage' | 'Damaged' | 'Rejected' | 'Staging';
  zone: string;
  aisle: string;
  rack: string;
  level: string;
  bin: string;
  storageClass: string;
  maxWeightKg: string;
  maxVolumeM3: string;
  maxPalletEquivalent: string;
  isActive: boolean;
  isBlocked: boolean;
  rowVersion?: string | null;
};

export type LocationCapacity = {
  locationId: number;
  code: string;
  name: string;
  structurePath?: string | null;
  storageClass?: string | null;
  maxWeightKg?: number | null;
  usedWeightKg?: number | null;
  maxVolumeM3?: number | null;
  usedVolumeM3?: number | null;
  maxPalletEquivalent?: number | null;
  usedPalletEquivalent?: number | null;
  profileIncomplete: boolean;
  compatibilityConflict: boolean;
  isActive: boolean;
  isBlocked: boolean;
  state: string;
};

export type HierarchyRow = {
  key: string;
  depth: number;
  level: string;
  code: string;
  path: string;
  location?: WarehouseLocation;
};

export type LocationViewTab = 'hierarchy' | 'capacity' | 'system';

export const locationTypeLabels: Record<string, string> = {
  Storage: 'Lưu trữ',
  Damaged: 'Hư hỏng',
  Rejected: 'Hàng bị từ chối',
  Staging: 'Khu tập kết xuất hàng',
  Receiving: 'Nhận hàng',
  Legacy: 'Tương thích hệ thống',
};

export const locationStatus = (location: WarehouseLocation) => {
  if (!location.isActive) return { label: 'Ngừng hoạt động', tone: 'neutral' as const };
  if (location.isBlocked) return { label: 'Bị khóa', tone: 'danger' as const };
  return { label: 'Đang hoạt động', tone: 'success' as const };
};

export const splitStructurePath = (value?: string | null): string[] | null => {
  if (!value) return null;
  const parts = value.split('/').map(part => part.trim()).filter(Boolean);
  return parts.length === 5 ? parts : null;
};

export const normalizeSegment = (value: string) => value.trim().toUpperCase().replace(/\s+/g, '-');

export const structurePathOf = (form: LocationForm) =>
  [form.zone, form.aisle, form.rack, form.level, form.bin].map(normalizeSegment).join('/');

export const formFromLocation = (location: WarehouseLocation): LocationForm => {
  const parts = splitStructurePath(location.structurePath) ?? ['', '', '', '', ''];
  return {
    id: location.id,
    code: location.code,
    name: location.name,
    locationType: (['Storage', 'Damaged', 'Rejected', 'Staging'].includes(location.locationType)
      ? location.locationType
      : 'Storage') as LocationForm['locationType'],
    zone: parts[0],
    aisle: parts[1],
    rack: parts[2],
    level: parts[3],
    bin: parts[4],
    storageClass: location.storageClass || '',
    maxWeightKg: location.maxWeightKg?.toString() || '',
    maxVolumeM3: location.maxVolumeM3?.toString() || '',
    maxPalletEquivalent: location.maxPalletEquivalent?.toString() || '',
    isActive: location.isActive,
    isBlocked: location.isBlocked,
    rowVersion: location.rowVersion,
  };
};

export const emptyForm = (): LocationForm => ({
  code: '',
  name: '',
  locationType: 'Storage',
  zone: '',
  aisle: '',
  rack: '',
  level: '',
  bin: '',
  storageClass: '',
  maxWeightKg: '',
  maxVolumeM3: '',
  maxPalletEquivalent: '',
  isActive: true,
  isBlocked: false,
});

export const nullableNumber = (value: string) => (value.trim() === '' ? null : Number(value));

export const capacityTone = (state: string) =>
  state === 'Available'
    ? ('success' as const)
    : state === 'NearCapacity' || state === 'ProfileIncomplete'
      ? ('warning' as const)
      : state === 'Inactive'
        ? ('neutral' as const)
        : ('danger' as const);

export const capacityLabel: Record<string, string> = {
  Available: 'Còn sức chứa',
  NearCapacity: 'Gần đầy',
  OverCapacity: 'Vượt sức chứa',
  ProfileIncomplete: 'Thiếu thông số',
  CompatibilityConflict: 'Xung đột phân lớp',
  Blocked: 'Bị khóa',
  Inactive: 'Ngừng hoạt động',
};

export const capacityUsage = (used?: number | null, max?: number | null, unit = '') => {
  if (max == null || !Number.isFinite(max) || max <= 0) return 'Không giới hạn';
  if (used == null || !Number.isFinite(used)) return 'Thiếu thông số / ' + max + (unit ? ' ' + unit : '');
  const percent = Math.round((used / max) * 100);
  return used + ' / ' + max + (unit ? ' ' + unit : '') + ' • ' + percent + '%';
};

export const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  if (response.response?.status === 409) {
    return (response.response.data?.message || 'Dữ liệu đã thay đổi.') + ' Vui lòng tải lại danh sách trước khi thử lại.';
  }
  return response.response?.data?.message || fallback;
};

export const buildHierarchyRows = (mapped: WarehouseLocation[]): HierarchyRow[] => {
  const rows: HierarchyRow[] = [];
  const seen = new Set<string>();
  const labels = ['Khu vực', 'Dãy kệ', 'Kệ', 'Tầng'];

  [...mapped]
    .sort((a, b) => (a.structurePath ?? '').localeCompare(b.structurePath ?? '') || a.code.localeCompare(b.code))
    .forEach(location => {
      const parts = splitStructurePath(location.structurePath);
      if (!parts) return;
      for (let depth = 0; depth < 4; depth += 1) {
        const prefix = parts.slice(0, depth + 1).join('/');
        if (seen.has(prefix)) continue;
        seen.add(prefix);
        rows.push({
          key: 'node-' + prefix,
          depth,
          level: labels[depth],
          code: parts[depth],
          path: parts.slice(0, depth + 1).join(' / '),
        });
      }
      rows.push({
        key: 'location-' + location.id,
        depth: 4,
        level: 'Ô / Vị trí',
        code: location.code,
        path: parts.join(' / '),
        location,
      });
    });
  return rows;
};
