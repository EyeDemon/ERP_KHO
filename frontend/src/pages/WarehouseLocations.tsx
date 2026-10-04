import { useCallback, useEffect, useMemo, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import './WarehouseLocations.css';

type Warehouse = {
  id: number;
  code: string;
  name: string;
};

type WarehouseLocation = {
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

type LocationForm = {
  id?: number;
  code: string;
  name: string;
  locationType: 'Storage' | 'Damaged' | 'Rejected';
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

type LocationCapacity = {
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

type HierarchyRow = {
  key: string;
  depth: number;
  level: string;
  code: string;
  path: string;
  location?: WarehouseLocation;
};

const locationTypeLabels: Record<string, string> = {
  Storage: 'Lưu trữ',
  Damaged: 'Hư hỏng',
  Rejected: 'Hàng bị từ chối',
  Receiving: 'Nhận hàng',
  Legacy: 'Tương thích hệ thống',
};

const locationStatus = (location: WarehouseLocation) => {
  if (!location.isActive) return { label: 'Ngừng hoạt động', tone: 'neutral' as const };
  if (location.isBlocked) return { label: 'Bị khóa', tone: 'danger' as const };
  return { label: 'Đang hoạt động', tone: 'success' as const };
};

const splitStructurePath = (value?: string | null): string[] | null => {
  if (!value) return null;
  const parts = value.split('/').map((part) => part.trim()).filter(Boolean);
  return parts.length === 5 ? parts : null;
};

const normalizeSegment = (value: string) => value.trim().toUpperCase().replace(/\s+/g, '-');

const structurePathOf = (form: LocationForm) =>
  [form.zone, form.aisle, form.rack, form.level, form.bin].map(normalizeSegment).join('/');

const formFromLocation = (location: WarehouseLocation): LocationForm => {
  const parts = splitStructurePath(location.structurePath) ?? ['', '', '', '', ''];
  return {
    id: location.id,
    code: location.code,
    name: location.name,
    locationType: (['Storage', 'Damaged', 'Rejected'].includes(location.locationType) ? location.locationType : 'Storage') as LocationForm['locationType'],
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

const emptyForm = (): LocationForm => ({
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

const nullableNumber = (value: string) => value.trim() === '' ? null : Number(value);
const capacityTone = (state: string) =>
  state === 'Available' ? 'success' as const
    : state === 'NearCapacity' || state === 'ProfileIncomplete' ? 'warning' as const
      : state === 'Inactive' ? 'neutral' as const
        : 'danger' as const;
const capacityLabel: Record<string, string> = {
  Available: 'Còn sức chứa',
  NearCapacity: 'Gần đầy',
  OverCapacity: 'Vượt sức chứa',
  ProfileIncomplete: 'Thiếu profile',
  CompatibilityConflict: 'Xung đột class',
  Blocked: 'Bị khóa',
  Inactive: 'Ngừng hoạt động',
};
const capacityUsage = (used?: number | null, max?: number | null, unit = '') => {
  if (max == null) return 'Không giới hạn';
  if (used == null) return 'Thiếu profile / ' + max + (unit ? ' ' + unit : '');
  const percent = Math.round((used / max) * 100);
  return used + ' / ' + max + (unit ? ' ' + unit : '') + ' • ' + percent + '%';
};

const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  if (response.response?.status === 409) {
    return (response.response.data?.message || 'Dữ liệu đã thay đổi.') + ' Vui lòng tải lại danh sách trước khi thử lại.';
  }
  return response.response?.data?.message || fallback;
};

const WarehouseLocations = () => {
  const canManage = usePermission('location.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState<number>(0);
  const [locations, setLocations] = useState<WarehouseLocation[]>([]);
  const [capacities, setCapacities] = useState<LocationCapacity[]>([]);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [typeFilter, setTypeFilter] = useState('all');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<LocationForm>(emptyForm);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const loadWarehouses = useCallback(async () => {
    const response = await apiClient.get('/api/putaway-tasks/location-warehouses');
    const items = response.data as Warehouse[];
    setWarehouses(items);
    setWarehouseId((current) => current || items[0]?.id || 0);
    return items;
  }, []);

  const loadLocations = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) {
      setLocations([]);
      setCapacities([]);
      return;
    }
    const [locationResponse, capacityResponse] = await Promise.all([
      apiClient.get('/api/putaway-tasks/locations', { params: { warehouseId: targetWarehouseId } }),
      apiClient.get('/api/putaway-tasks/location-capacity', { params: { warehouseId: targetWarehouseId } }),
    ]);
    setLocations(locationResponse.data as WarehouseLocation[]);
    setCapacities(capacityResponse.data as LocationCapacity[]);
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const items = await loadWarehouses();
      const target = warehouseId || items[0]?.id || 0;
      await loadLocations(target);
    } catch (failure) {
      setWarehouses([]);
      setLocations([]);
      setError(messageOf(failure, 'Không thể tải cấu trúc vị trí kho.'));
    } finally {
      setLoading(false);
    }
  }, [loadLocations, loadWarehouses, warehouseId]);

  useEffect(() => {
    void load();
    // Initial load only; warehouse switching is handled separately.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!warehouseId || loading) return;
    setError('');
    void loadLocations(warehouseId).catch((failure) => {
      setLocations([]);
      setCapacities([]);
      setError(messageOf(failure, 'Không thể tải vị trí hoặc sức chứa của kho đã chọn.'));
    });
  }, [warehouseId, loadLocations, loading]);

  const mapped = useMemo(
    () => locations.filter((location) => !location.isSystemManaged && splitStructurePath(location.structurePath)),
    [locations],
  );
  const unmapped = useMemo(
    () => locations.filter((location) => !location.isSystemManaged && !splitStructurePath(location.structurePath)),
    [locations],
  );
  const systemLocations = useMemo(() => locations.filter((location) => location.isSystemManaged), [locations]);

  const hierarchyRows = useMemo(() => {
    const rows: HierarchyRow[] = [];
    const seen = new Set<string>();
    const labels = ['Khu vực', 'Dãy kệ', 'Kệ', 'Tầng'];

    [...mapped]
      .sort((a, b) => (a.structurePath ?? '').localeCompare(b.structurePath ?? '') || a.code.localeCompare(b.code))
      .forEach((location) => {
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
  }, [mapped]);

  const filtered = useMemo(() => {
    const query = search.trim().toLocaleLowerCase('vi');
    return locations.filter((location) => {
      const statusMatches =
        statusFilter === 'all'
        || (statusFilter === 'active' && location.isActive && !location.isBlocked)
        || (statusFilter === 'blocked' && location.isBlocked)
        || (statusFilter === 'inactive' && !location.isActive);
      const typeMatches = typeFilter === 'all' || location.locationType === typeFilter;
      const searchMatches = !query || [location.code, location.name, location.structurePath ?? '', location.storageClass ?? '', location.locationType]
        .join(' ')
        .toLocaleLowerCase('vi')
        .includes(query);
      return statusMatches && typeMatches && searchMatches;
    });
  }, [locations, search, statusFilter, typeFilter]);

  const zoneCount = new Set(mapped.map((location) => splitStructurePath(location.structurePath)?.[0]).filter(Boolean)).size;
  const blockedCount = locations.filter((location) => location.isBlocked).length;
  const inactiveCount = locations.filter((location) => !location.isActive).length;
  const nearCapacityCount = capacities.filter((item) => item.state === 'NearCapacity').length;
  const capacityIssueCount = capacities.filter((item) => ['OverCapacity', 'ProfileIncomplete', 'CompatibilityConflict'].includes(item.state)).length;
  const editing = form.id !== undefined;
  const editingLocation = editing ? locations.find((location) => location.id === form.id) : undefined;
  const hasLockedStructure = Boolean(editingLocation?.structurePath);

  const openCreate = () => {
    setForm(emptyForm());
    setFormError('');
    setSuccess('');
    setShowForm(true);
  };

  const openEdit = (location: WarehouseLocation) => {
    setForm(formFromLocation(location));
    setFormError('');
    setSuccess('');
    setShowForm(true);
  };

  const closeForm = () => {
    if (saving) return;
    setShowForm(false);
    setFormError('');
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving || !warehouseId) return;
    setFormError('');
    setSuccess('');

    const required = [
      ['Mã vị trí', form.code],
      ['Tên vị trí', form.name],
    ] as const;
    const missing = required.find(([, value]) => !value.trim());
    if (missing) {
      setFormError(missing[0] + ' là bắt buộc.');
      return;
    }

    const structureFields = [
      ['Zone', form.zone],
      ['Aisle', form.aisle],
      ['Rack', form.rack],
      ['Level', form.level],
      ['Bin', form.bin],
    ] as const;
    const hasAnyStructure = structureFields.some(([, value]) => value.trim());
    const structureMissing = structureFields.find(([, value]) => !value.trim());
    if ((!editing || hasAnyStructure) && structureMissing) {
      setFormError(structureMissing[0] + ' là bắt buộc khi gán cấu trúc vật lý.');
      return;
    }

    const structurePath = hasAnyStructure ? structurePathOf(form) : null;
    setSaving(true);
    try {
      if (editing && form.id !== undefined) {
        if (!form.rowVersion) {
          setFormError('Thiếu phiên bản dữ liệu. Vui lòng tải lại danh sách.');
          return;
        }
        await apiClient.put('/api/putaway-tasks/locations/' + form.id, {
          name: form.name.trim(),
          structurePath,
          updateConstraints: true,
          storageClass: form.storageClass.trim() || null,
          maxWeightKg: nullableNumber(form.maxWeightKg),
          maxVolumeM3: nullableNumber(form.maxVolumeM3),
          maxPalletEquivalent: nullableNumber(form.maxPalletEquivalent),
          isActive: form.isActive,
          isBlocked: form.isBlocked,
          isPickable: form.locationType === 'Storage',
          isReceivable: false,
          rowVersion: form.rowVersion,
        });
        setSuccess('Cập nhật vị trí thành công.');
      } else {
        await apiClient.post('/api/putaway-tasks/locations', {
          warehouseId,
          code: form.code.trim(),
          name: form.name.trim(),
          structurePath,
          storageClass: form.storageClass.trim() || null,
          maxWeightKg: nullableNumber(form.maxWeightKg),
          maxVolumeM3: nullableNumber(form.maxVolumeM3),
          maxPalletEquivalent: nullableNumber(form.maxPalletEquivalent),
          locationType: form.locationType,
          isPickable: form.locationType === 'Storage',
          isReceivable: false,
        });
        setSuccess('Tạo vị trí thành công.');
      }
      setShowForm(false);
      await loadLocations(warehouseId);
    } catch (failure) {
      setFormError(messageOf(failure, 'Không thể lưu vị trí.'));
    } finally {
      setSaving(false);
    }
  };

  const currentWarehouse = warehouses.find((warehouse) => warehouse.id === warehouseId);

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Dữ liệu nền"
        title="Cấu trúc vị trí kho"
        description="Quản lý Zone → Aisle → Rack → Level → Bin, Storage Class và sức chứa trên WarehouseLocation thật. Putaway dùng chính constraint này để chặn vị trí không tương thích hoặc vượt capacity."
        actions={canManage ? <button type="button" className="ui-primary-button" onClick={openCreate}>Thêm vị trí</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void load()}>Thử lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={locations.length} label="Tổng vị trí" />
        <UiMetric value={zoneCount} label="Khu vực đã cấu trúc" />
        <UiMetric value={mapped.length} label="Vị trí đã gắn cấu trúc" />
        <UiMetric value={unmapped.length} label="Chưa gắn cấu trúc" />
        <UiMetric value={blockedCount} label="Bị khóa" />
        <UiMetric value={inactiveCount} label="Ngừng hoạt động" />
        <UiMetric value={nearCapacityCount} label="Gần đầy" />
        <UiMetric value={capacityIssueCount} label="Capacity cần xử lý" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho vị trí" value={warehouseId} onChange={(event) => setWarehouseId(Number(event.target.value))}>
            {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm vị trí">
          <input aria-label="Tìm vị trí" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Mã, tên hoặc đường dẫn" />
        </UiToolbarField>
        <UiToolbarField label="Trạng thái">
          <select aria-label="Trạng thái vị trí" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
            <option value="all">Tất cả</option>
            <option value="active">Đang hoạt động</option>
            <option value="blocked">Bị khóa</option>
            <option value="inactive">Ngừng hoạt động</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Loại">
          <select aria-label="Loại vị trí" value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}>
            <option value="all">Tất cả</option>
            <option value="Storage">Lưu trữ</option>
            <option value="Damaged">Hư hỏng</option>
            <option value="Rejected">Hàng bị từ chối</option>
            <option value="Receiving">Nhận hàng</option>
            <option value="Legacy">Tương thích hệ thống</option>
          </select>
        </UiToolbarField>
      </UiToolbar>

      {loading ? (
        <p role="status">Đang tải cấu trúc vị trí...</p>
      ) : warehouses.length === 0 ? (
        <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
      ) : (
        <>
          <UiCard title="Sức chứa & Storage Constraints">
            <p className="ui-muted-text">Số liệu sử dụng được tính từ InventoryStock theo base UOM × profile sản phẩm. Trạng thái thiếu profile hoặc xung đột Storage Class sẽ chặn Putaway vào vị trí có constraint tương ứng.</p>
            {capacities.length === 0 ? (
              <UiEmptyState title="Chưa có vị trí vật lý để theo dõi capacity." />
            ) : (
              <UiTableScroll>
                <table aria-label="Sức chứa vị trí kho thật">
                  <thead><tr><th>Vị trí</th><th>Storage Class</th><th>Trọng lượng</th><th>Thể tích</th><th>Pallet-eq</th><th>Trạng thái</th></tr></thead>
                  <tbody>{capacities.map((item) => (
                    <tr key={item.locationId}>
                      <td><strong>{item.code}</strong><br /><small>{item.structurePath?.split('/').join(' / ') || item.name}</small></td>
                      <td>{item.storageClass || 'Không giới hạn class'}</td>
                      <td>{capacityUsage(item.usedWeightKg, item.maxWeightKg, 'kg')}</td>
                      <td>{capacityUsage(item.usedVolumeM3, item.maxVolumeM3, 'm³')}</td>
                      <td>{capacityUsage(item.usedPalletEquivalent, item.maxPalletEquivalent)}</td>
                      <td><UiBadge tone={capacityTone(item.state)}>{capacityLabel[item.state] || item.state}</UiBadge></td>
                    </tr>
                  ))}</tbody>
                </table>
              </UiTableScroll>
            )}
          </UiCard>

          <UiCard title={'Cây cấu trúc vật lý' + (currentWarehouse ? ' • ' + currentWarehouse.code : '')}>
            {hierarchyRows.length === 0 ? (
              <UiEmptyState title="Kho chưa có vị trí được gắn StructurePath." detail="Vị trí cũ vẫn được giữ nguyên ở bảng Chưa gắn cấu trúc bên dưới." />
            ) : (
              <UiTableScroll>
                <table aria-label="Cây cấu trúc vị trí thật">
                  <thead><tr><th>Cấp</th><th>Mã / Tên</th><th>Đường dẫn</th><th>Loại</th><th>Trạng thái</th><th>Khả dụng</th>{canManage && <th>Thao tác</th>}</tr></thead>
                  <tbody>
                    {hierarchyRows.map((row) => {
                      const status = row.location ? locationStatus(row.location) : null;
                      return (
                        <tr key={row.key}>
                          <td><span className={'warehouse-location-depth depth-' + row.depth}>{row.level}</span></td>
                          <td><strong>{row.code}</strong>{row.location && <><br /><small>{row.location.name}</small></>}</td>
                          <td>{row.path}</td>
                          <td>{row.location ? (locationTypeLabels[row.location.locationType] || row.location.locationType) : '—'}</td>
                          <td>{status ? <UiBadge tone={status.tone}>{status.label}</UiBadge> : '—'}</td>
                          <td>{row.location ? (row.location.isPickable ? 'Có thể pick' : row.location.isReceivable ? 'Có thể nhận' : 'Không pick') : '—'}</td>
                          {canManage && <td>{row.location && !row.location.isSystemManaged ? <button type="button" onClick={() => openEdit(row.location!)}>Sửa</button> : '—'}</td>}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </UiTableScroll>
            )}
          </UiCard>

          <UiCard title="Danh sách vị trí theo bộ lọc">
            <UiTableScroll>
              <table aria-label="Danh sách vị trí kho thật">
                <thead><tr><th>Mã</th><th>Tên</th><th>StructurePath</th><th>Loại</th><th>Trạng thái</th><th>Pick</th>{canManage && <th>Thao tác</th>}</tr></thead>
                <tbody>
                  {filtered.map((location) => {
                    const status = locationStatus(location);
                    return (
                      <tr key={location.id}>
                        <td><strong>{location.code}</strong>{location.isSystemManaged && <><br /><small>System managed</small></>}</td>
                        <td>{location.name}</td>
                        <td>{splitStructurePath(location.structurePath)?.join(' / ') || '—'}</td>
                        <td>{locationTypeLabels[location.locationType] || location.locationType}</td>
                        <td><UiBadge tone={status.tone}>{status.label}</UiBadge></td>
                        <td>{location.isPickable ? 'Có' : 'Không'}</td>
                        {canManage && <td>{!location.isSystemManaged ? <button type="button" onClick={() => openEdit(location)}>Sửa</button> : '—'}</td>}
                      </tr>
                    );
                  })}
                  {filtered.length === 0 && <tr><td className="ui-empty-cell" colSpan={canManage ? 7 : 6}>Không có vị trí phù hợp bộ lọc.</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          {unmapped.length > 0 && (
            <UiCard title="Vị trí chưa gắn cấu trúc">
              <p className="ui-muted-text">Dữ liệu cũ được giữ nguyên; hệ thống không tự suy đoán Zone/Aisle/Rack/Level/Bin từ mã vị trí.</p>
              <UiTableScroll>
                <table aria-label="Vị trí thật chưa gắn cấu trúc">
                  <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Trạng thái</th>{canManage && <th>Thao tác</th>}</tr></thead>
                  <tbody>{unmapped.map((location) => {
                    const status = locationStatus(location);
                    return <tr key={location.id}><td><strong>{location.code}</strong></td><td>{location.name}</td><td>{locationTypeLabels[location.locationType] || location.locationType}</td><td><UiBadge tone={status.tone}>{status.label}</UiBadge></td>{canManage && <td><button type="button" onClick={() => openEdit(location)}>Gắn cấu trúc / Sửa</button></td>}</tr>;
                  })}</tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          )}

          {systemLocations.length > 0 && (
            <UiCard title="Vị trí hệ thống">
              <p className="ui-muted-text">RECEIVING/LEGACY do hệ thống quản lý và không tham gia hierarchy vật lý do người dùng cấu hình.</p>
              <UiTableScroll>
                <table aria-label="Vị trí hệ thống thật">
                  <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Receivable</th><th>Pick</th></tr></thead>
                  <tbody>{systemLocations.map((location) => <tr key={location.id}><td><strong>{location.code}</strong></td><td>{location.name}</td><td>{locationTypeLabels[location.locationType] || location.locationType}</td><td>{location.isReceivable ? 'Có' : 'Không'}</td><td>{location.isPickable ? 'Có' : 'Không'}</td></tr>)}</tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          )}
        </>
      )}

      {showForm && canManage && (
        <UiCard title={editing ? 'Sửa vị trí ' + form.code : 'Thêm vị trí'}>
          <form className="warehouse-location-form" onSubmit={save}>
            {formError && <p role="alert">{formError}</p>}

            <div className="warehouse-location-form-grid">
              <label className="ui-stack"><span>Mã vị trí *</span><input aria-label="Mã vị trí" value={form.code} onChange={(event) => setForm((current) => ({ ...current, code: event.target.value }))} disabled={saving || editing} maxLength={64} /></label>
              <label className="ui-stack"><span>Tên vị trí *</span><input aria-label="Tên vị trí" value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} disabled={saving} maxLength={200} /></label>
              <label className="ui-stack"><span>Loại vị trí *</span><select aria-label="Loại vị trí form" value={form.locationType} onChange={(event) => setForm((current) => ({ ...current, locationType: event.target.value as LocationForm['locationType'] }))} disabled={saving || editing}><option value="Storage">Lưu trữ</option><option value="Damaged">Hư hỏng</option><option value="Rejected">Hàng bị từ chối</option></select></label>
            </div>

            <fieldset disabled={saving}>
              <legend>Storage Class & Capacity</legend>
              <p className="ui-muted-text">Để trống nghĩa là không áp giới hạn theo chiều đó. Nếu đã có tồn kho, backend chỉ cho lưu constraint khi tải hiện tại vẫn hợp lệ.</p>
              <div className="warehouse-location-capacity-grid">
                <label className="ui-stack"><span>Storage Class</span><input aria-label="Storage Class vị trí" value={form.storageClass} onChange={(event) => setForm((current) => ({ ...current, storageClass: event.target.value.toUpperCase() }))} maxLength={32} placeholder="AMBIENT" /></label>
                <label className="ui-stack"><span>Max kg</span><input aria-label="Giới hạn trọng lượng kg" type="number" min="0.000001" step="0.000001" value={form.maxWeightKg} onChange={(event) => setForm((current) => ({ ...current, maxWeightKg: event.target.value }))} placeholder="1500" /></label>
                <label className="ui-stack"><span>Max m³</span><input aria-label="Giới hạn thể tích m3" type="number" min="0.00000001" step="0.00000001" value={form.maxVolumeM3} onChange={(event) => setForm((current) => ({ ...current, maxVolumeM3: event.target.value }))} placeholder="10" /></label>
                <label className="ui-stack"><span>Max pallet-eq</span><input aria-label="Giới hạn pallet equivalent" type="number" min="0.00000001" step="0.00000001" value={form.maxPalletEquivalent} onChange={(event) => setForm((current) => ({ ...current, maxPalletEquivalent: event.target.value }))} placeholder="5" /></label>
              </div>
            </fieldset>

            <fieldset disabled={saving || hasLockedStructure}>
              <legend>Cấu trúc vật lý • Zone / Aisle / Rack / Level / Bin</legend>
              {!editing || hasLockedStructure ? null : <p className="ui-muted-text">Vị trí legacy có thể giữ nguyên chưa gắn cấu trúc. Nếu gán mới, cần nhập đủ cả 5 cấp.</p>}
              <div className="warehouse-location-path-grid">
                <label className="ui-stack"><span>Zone *</span><input aria-label="Zone" value={form.zone} onChange={(event) => setForm((current) => ({ ...current, zone: event.target.value }))} maxLength={32} placeholder="ZONE-A" /></label>
                <label className="ui-stack"><span>Aisle *</span><input aria-label="Aisle" value={form.aisle} onChange={(event) => setForm((current) => ({ ...current, aisle: event.target.value }))} maxLength={32} placeholder="A01" /></label>
                <label className="ui-stack"><span>Rack *</span><input aria-label="Rack" value={form.rack} onChange={(event) => setForm((current) => ({ ...current, rack: event.target.value }))} maxLength={32} placeholder="R02" /></label>
                <label className="ui-stack"><span>Level *</span><input aria-label="Level" value={form.level} onChange={(event) => setForm((current) => ({ ...current, level: event.target.value }))} maxLength={32} placeholder="L03" /></label>
                <label className="ui-stack"><span>Bin *</span><input aria-label="Bin" value={form.bin} onChange={(event) => setForm((current) => ({ ...current, bin: event.target.value }))} maxLength={32} placeholder="B04" /></label>
              </div>
              {hasLockedStructure && <p className="ui-muted-text">StructurePath đã được khóa. Muốn đổi vị trí vật lý phải tạo mã vị trí mới để không làm sai lịch sử movement.</p>}
            </fieldset>

            {editing && (
              <div className="warehouse-location-flags">
                <label className="ui-checkbox-label"><input type="checkbox" checked={form.isActive} onChange={(event) => setForm((current) => ({ ...current, isActive: event.target.checked }))} disabled={saving} />Đang hoạt động</label>
                <label className="ui-checkbox-label"><input type="checkbox" checked={form.isBlocked} onChange={(event) => setForm((current) => ({ ...current, isBlocked: event.target.checked }))} disabled={saving} />Bị khóa</label>
              </div>
            )}

            <div className="ui-inline-actions">
              <button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu vị trí'}</button>
              <button type="button" disabled={saving} onClick={closeForm}>Hủy</button>
            </div>
          </form>
        </UiCard>
      )}
    </UiPage>
  );
};

export default WarehouseLocations;
