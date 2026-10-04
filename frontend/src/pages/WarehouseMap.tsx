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
import './WarehouseMap.css';

type Warehouse = { id: number; code: string; name: string };
type MapItem = {
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
type MapResponse = { warehouseId: number; generatedAtUtc: string; items: MapItem[] };
type LayoutForm = { locationId: number; code: string; x: string; y: string; width: string; height: string; rowVersion: string };

const mapped = (item: MapItem) =>
  item.mapX != null && item.mapY != null && item.mapWidth != null && item.mapHeight != null;

const capacityTone = (item: MapItem) =>
  item.capacityState === 'OverCapacity' || item.capacityState === 'CompatibilityConflict' ? 'danger' as const
    : item.capacityState === 'NearCapacity' || item.capacityState === 'ProfileIncomplete' ? 'warning' as const
      : item.capacityState === 'Blocked' ? 'danger' as const
        : item.capacityState === 'Inactive' ? 'neutral' as const
          : 'success' as const;

const heatClass = (item: MapItem, mode: 'utilization' | 'activity') => {
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

const stateLabel: Record<string, string> = {
  Available: 'Khả dụng',
  NearCapacity: 'Gần đầy',
  OverCapacity: 'Vượt sức chứa',
  ProfileIncomplete: 'Thiếu profile',
  CompatibilityConflict: 'Xung đột class',
  Blocked: 'Bị khóa',
  Inactive: 'Ngừng hoạt động',
};

const errorMessage = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  if (response.response?.status === 409) return (response.response.data?.message || 'Dữ liệu đã thay đổi.') + ' Vui lòng tải lại bản đồ.';
  return response.response?.data?.message || fallback;
};

const WarehouseMap = () => {
  const canManage = usePermission('location.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState(0);
  const [snapshot, setSnapshot] = useState<MapResponse | null>(null);
  const [mode, setMode] = useState<'utilization' | 'activity'>('utilization');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [layout, setLayout] = useState<LayoutForm | null>(null);
  const [saving, setSaving] = useState(false);

  const loadWarehouses = useCallback(async () => {
    const response = await apiClient.get('/api/putaway-tasks/location-warehouses');
    const rows = response.data as Warehouse[];
    setWarehouses(rows);
    setWarehouseId(current => current || rows[0]?.id || 0);
    return rows;
  }, []);

  const loadMap = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) {
      setSnapshot(null);
      return;
    }
    const response = await apiClient.get('/api/putaway-tasks/location-map', { params: { warehouseId: targetWarehouseId } });
    setSnapshot(response.data as MapResponse);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    void loadWarehouses()
      .catch(failure => {
        if (!cancelled) setError(errorMessage(failure, 'Không thể tải danh sách kho.'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [loadWarehouses]);

  useEffect(() => {
    if (!warehouseId || loading) return;
    setError('');
    void loadMap(warehouseId).catch(failure => setError(errorMessage(failure, 'Không thể tải bản đồ kho.')));
  }, [warehouseId, loadMap, loading]);

  const items = snapshot?.items ?? [];
  const mappedItems = useMemo(() => items.filter(mapped), [items]);
  const unmappedItems = useMemo(() => items.filter(item => !mapped(item)), [items]);
  const filteredMapped = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('vi');
    return mappedItems.filter(item => !q || [item.code, item.name, item.structurePath ?? '', item.storageClass ?? ''].join(' ').toLocaleLowerCase('vi').includes(q));
  }, [mappedItems, search]);

  const criticalCount = items.filter(item => heatClass(item, mode) === 'danger').length;
  const busyCount = items.filter(item => item.activityLevel === 'High' || item.activityLevel === 'Medium').length;
  const avgUtilization = (() => {
    const values = items.map(item => item.utilizationPercent).filter((value): value is number => value != null);
    return values.length ? Math.round(values.reduce((sum, value) => sum + value, 0) / values.length) : 0;
  })();

  const currentWarehouse = warehouses.find(item => item.id === warehouseId);
  const generatedAt = snapshot?.generatedAtUtc ? new Date(snapshot.generatedAtUtc) : null;
  const ageMinutes = generatedAt ? Math.max(0, Math.floor((Date.now() - generatedAt.getTime()) / 60000)) : null;

  const openLayout = (item: MapItem) => {
    if (!canManage || !item.rowVersion) return;
    setLayout({
      locationId: item.locationId,
      code: item.code,
      x: item.mapX?.toString() ?? '',
      y: item.mapY?.toString() ?? '',
      width: item.mapWidth?.toString() ?? '',
      height: item.mapHeight?.toString() ?? '',
      rowVersion: item.rowVersion,
    });
    setSuccess('');
    setError('');
  };

  const saveLayout = async (clear = false) => {
    if (!layout || saving) return;
    setSaving(true);
    setError('');
    setSuccess('');
    try {
      if (!clear && [layout.x, layout.y, layout.width, layout.height].some(value => !value.trim())) {
        setError('Cần nhập đủ X, Y, Width và Height.');
        return;
      }
      const payload = clear
        ? { mapX: null, mapY: null, mapWidth: null, mapHeight: null, rowVersion: layout.rowVersion }
        : {
            mapX: Number(layout.x),
            mapY: Number(layout.y),
            mapWidth: Number(layout.width),
            mapHeight: Number(layout.height),
            rowVersion: layout.rowVersion,
          };
      if (!clear && [payload.mapX, payload.mapY, payload.mapWidth, payload.mapHeight].some(value => Number.isNaN(value))) {
        setError('X, Y, Width và Height phải là số hợp lệ.');
        return;
      }
      await apiClient.put('/api/putaway-tasks/locations/' + layout.locationId + '/layout', payload);
      setLayout(null);
      setSuccess(clear ? 'Đã xóa bố trí bản đồ.' : 'Đã cập nhật bố trí bản đồ.');
      await loadMap(warehouseId);
    } catch (failure) {
      setError(errorMessage(failure, 'Không thể cập nhật bố trí bản đồ.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kho & Vị trí"
        title="Bản đồ kho & Heatmap"
        description="Bản đồ vật lý dựa trên layout được cấu hình thật. Heatmap sử dụng capacity, movement 60 phút gần nhất và active putaway; location chưa có tọa độ không được tự suy đoán."
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error}</p>}

      <UiMetricGrid>
        <UiMetric value={mappedItems.length} label="Đã bố trí" />
        <UiMetric value={unmappedItems.length} label="Chưa bố trí" />
        <UiMetric value={avgUtilization + '%'} label="Utilization trung bình" />
        <UiMetric value={busyCount} label="Khu vực có hoạt động" />
        <UiMetric value={criticalCount} label="Điểm cần chú ý" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho bản đồ" value={warehouseId} onChange={event => setWarehouseId(Number(event.target.value))}>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Heatmap">
          <select aria-label="Chế độ heatmap" value={mode} onChange={event => setMode(event.target.value as 'utilization' | 'activity')}>
            <option value="utilization">Utilization / Capacity</option>
            <option value="activity">Operational Activity</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm vị trí">
          <input aria-label="Tìm vị trí trên bản đồ" value={search} onChange={event => setSearch(event.target.value)} placeholder="Mã, tên, path, class" />
        </UiToolbarField>
        <button type="button" onClick={() => void loadMap(warehouseId)} disabled={!warehouseId || loading}>Làm mới</button>
      </UiToolbar>

      <p className={ageMinutes != null && ageMinutes > 5 ? 'warehouse-map-freshness stale' : 'warehouse-map-freshness'}>
        Snapshot: {generatedAt ? generatedAt.toLocaleString('vi-VN') : '—'} • Tuổi dữ liệu: {ageMinutes == null ? '—' : ageMinutes + ' phút'}
        {ageMinutes != null && ageMinutes > 5 ? ' • Dữ liệu cũ, cần refresh trước khi điều phối.' : ''}
      </p>

      {loading ? (
        <p role="status">Đang tải bản đồ kho...</p>
      ) : warehouses.length === 0 ? (
        <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
      ) : (
        <>
          <UiCard title={'Warehouse Map' + (currentWarehouse ? ' • ' + currentWarehouse.code : '')}>
            {filteredMapped.length === 0 ? (
              <UiEmptyState title="Chưa có location phù hợp đã được bố trí trên bản đồ." detail="Location không có tọa độ thật sẽ nằm trong danh sách Chưa bố trí." />
            ) : (
              <div className="warehouse-map-canvas" aria-label="Bản đồ kho thật">
                <div className="warehouse-map-grid" aria-hidden="true" />
                {filteredMapped.map(item => (
                  <button
                    key={item.locationId}
                    type="button"
                    className={'warehouse-map-cell ' + heatClass(item, mode)}
                    style={{
                      left: item.mapX + '%',
                      top: item.mapY + '%',
                      width: item.mapWidth + '%',
                      height: item.mapHeight + '%',
                    }}
                    onClick={() => openLayout(item)}
                    disabled={!canManage}
                    aria-label={'Vị trí ' + item.code}
                    title={item.structurePath || item.name}
                  >
                    <strong>{item.code}</strong>
                    <span>{mode === 'utilization' ? (item.utilizationPercent != null ? item.utilizationPercent + '%' : 'N/A') : item.activityLevel}</span>
                    <small>{item.recentMovementCount} mvmt • {item.activePutawayCount} task</small>
                  </button>
                ))}
              </div>
            )}
            <div className="warehouse-map-legend" aria-label="Chú giải heatmap">
              <span><i className="success" /> Bình thường</span>
              <span><i className="warning" /> Cần chú ý</span>
              <span><i className="danger" /> Rủi ro / cao</span>
              <span><i className="neutral" /> Ngừng hoạt động</span>
            </div>
          </UiCard>

          <UiCard title="Operational Heatmap Detail">
            <UiTableScroll>
              <table aria-label="Chi tiết heatmap kho thật">
                <thead><tr><th>Vị trí</th><th>Storage Class</th><th>Utilization</th><th>Capacity</th><th>Movement 60m</th><th>Active putaway</th><th>Activity</th>{canManage && <th>Layout</th>}</tr></thead>
                <tbody>{items.map(item => (
                  <tr key={item.locationId}>
                    <td><strong>{item.code}</strong><br /><small>{item.structurePath?.split('/').join(' / ') || item.name}</small></td>
                    <td>{item.storageClass || '—'}</td>
                    <td>{item.utilizationPercent != null ? item.utilizationPercent + '%' : 'N/A'}</td>
                    <td><UiBadge tone={capacityTone(item)}>{stateLabel[item.capacityState] || item.capacityState}</UiBadge></td>
                    <td>{item.recentMovementCount}</td>
                    <td>{item.activePutawayCount}</td>
                    <td>{item.activityLevel}</td>
                    {canManage && <td><button type="button" onClick={() => openLayout(item)} disabled={!item.rowVersion}>{mapped(item) ? 'Sửa bố trí' : 'Bố trí'}</button></td>}
                  </tr>
                ))}</tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          {unmappedItems.length > 0 && (
            <UiCard title="Location chưa bố trí">
              <p className="ui-muted-text">Hệ thống không tự suy đoán tọa độ từ StructurePath. Manager phải bố trí bằng tọa độ phần trăm.</p>
              <UiTableScroll>
                <table aria-label="Location chưa bố trí bản đồ">
                  <thead><tr><th>Mã</th><th>Tên</th><th>StructurePath</th><th>Storage Class</th>{canManage && <th>Thao tác</th>}</tr></thead>
                  <tbody>{unmappedItems.map(item => (
                    <tr key={item.locationId}>
                      <td><strong>{item.code}</strong></td><td>{item.name}</td><td>{item.structurePath?.split('/').join(' / ') || '—'}</td><td>{item.storageClass || '—'}</td>
                      {canManage && <td><button type="button" onClick={() => openLayout(item)} disabled={!item.rowVersion}>Bố trí</button></td>}
                    </tr>
                  ))}</tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          )}

          {layout && canManage && (
            <UiCard title={'Bố trí bản đồ • ' + layout.code}>
              <p className="ui-muted-text">Tọa độ dùng phần trăm canvas 0–100. X + Width và Y + Height không được vượt 100.</p>
              <div className="warehouse-map-layout-form">
                <label className="ui-stack"><span>X (%)</span><input aria-label="Map X" type="number" min="0" max="100" step="0.01" value={layout.x} onChange={event => setLayout(current => current && ({ ...current, x: event.target.value }))} /></label>
                <label className="ui-stack"><span>Y (%)</span><input aria-label="Map Y" type="number" min="0" max="100" step="0.01" value={layout.y} onChange={event => setLayout(current => current && ({ ...current, y: event.target.value }))} /></label>
                <label className="ui-stack"><span>Width (%)</span><input aria-label="Map Width" type="number" min="0.01" max="100" step="0.01" value={layout.width} onChange={event => setLayout(current => current && ({ ...current, width: event.target.value }))} /></label>
                <label className="ui-stack"><span>Height (%)</span><input aria-label="Map Height" type="number" min="0.01" max="100" step="0.01" value={layout.height} onChange={event => setLayout(current => current && ({ ...current, height: event.target.value }))} /></label>
              </div>
              <div className="ui-inline-actions">
                <button type="button" onClick={() => void saveLayout(false)} disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu bố trí'}</button>
                <button type="button" onClick={() => setLayout(null)} disabled={saving}>Hủy</button>
                {mapped(items.find(item => item.locationId === layout.locationId) ?? {} as MapItem) && <button type="button" onClick={() => void saveLayout(true)} disabled={saving}>Xóa bố trí</button>}
              </div>
            </UiCard>
          )}
        </>
      )}
    </UiPage>
  );
};

export default WarehouseMap;
