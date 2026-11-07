import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useInRouterContext } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import {
  UiCard,
  UiEmptyState,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import {
  activityLabel,
  errorMessage,
  heatClass,
  mapped,
  type LayoutForm,
  type MapItem,
  type MapResponse,
  type ProductivityAndBiSnapshot,
  type Warehouse,
} from '../modules/inventory/warehouseMapShared';
import {
  WarehouseMapBottomSections,
  WarehouseMapTopCards,
} from '../modules/inventory/WarehouseMapSections';
import './WarehouseMap.css';

const WarehouseMap = () => {
  const inRouter = useInRouterContext();
  const canManage = usePermission('location.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState(0);
  const [snapshot, setSnapshot] = useState<MapResponse | null>(null);
  const [biSnapshot, setBiSnapshot] = useState<ProductivityAndBiSnapshot | null>(null);
  const [mode, setMode] = useState<'utilization' | 'activity'>('utilization');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [layout, setLayout] = useState<LayoutForm | null>(null);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  const [biSyncBusy, setBiSyncBusy] = useState(false);
  const [biSyncResult, setBiSyncResult] = useState<{ snapshotId: string; status: string } | null>(null);

  const syncBiSnapshot = async () => {
    if (biSyncBusy) return;
    setBiSyncBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/v1/reports/sync-bi-snapshot', { warehouseId });
      if (res?.data?.snapshotId && res?.data?.status) {
        setBiSyncResult({
          snapshotId: String(res.data.snapshotId),
          status: String(res.data.status),
        });
      } else {
        setBiSyncResult(null);
        setError('Phản hồi đồng bộ ảnh chụp bản đồ nhiệt BI không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setBiSyncResult(null);
      setError(errorMessage(failure, 'Không thể đồng bộ ảnh chụp bản đồ nhiệt và chuẩn dữ liệu BI.'));
    } finally {
      setBiSyncBusy(false);
    }
  };

  const loadWarehouses = useCallback(async () => {
    const response = await apiClient.get('/api/v1/putaway-tasks/location-warehouses');
    const rows = response.data as Warehouse[];
    setWarehouses(rows);
    setWarehouseId((current) => current || rows[0]?.id || 0);
    return rows;
  }, []);

  const loadMap = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) {
      setSnapshot(null);
      return;
    }
    let biLoadError = '';
    const [response, biRes] = await Promise.all([
      apiClient.get('/api/v1/putaway-tasks/location-map', { params: { warehouseId: targetWarehouseId } }),
      apiClient.get<ProductivityAndBiSnapshot>('/api/v1/reports/productivity-and-bi-snapshot', { params: { warehouseId: targetWarehouseId } }).catch((err: any) => {
        if (err?.response?.status && err.response.status >= 400) {
          biLoadError = errorMessage(err, 'Không thể tải ảnh chụp năng suất BI.');
        }
        return null;
      }),
    ]);
    setSnapshot(response.data as MapResponse);
    if (biRes?.data && typeof biRes.data.completedPutawaysCount === 'number') {
      setBiSnapshot(biRes.data);
    }
    if (biLoadError) setError(biLoadError);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    void loadWarehouses()
      .catch((failure) => {
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
    void loadMap(warehouseId).catch((failure) => setError(errorMessage(failure, 'Không thể tải bản đồ kho.')));
  }, [warehouseId, loadMap, loading]);

  const items = snapshot?.items ?? [];
  const mappedItems = useMemo(() => items.filter(mapped), [items]);
  const unmappedItems = useMemo(() => items.filter((item) => !mapped(item)), [items]);
  const filteredMapped = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('vi');
    return mappedItems.filter((item) => !q || [item.code, item.name, item.structurePath ?? '', item.storageClass ?? ''].join(' ').toLocaleLowerCase('vi').includes(q));
  }, [mappedItems, search]);

  const criticalCount = items.filter((item) => heatClass(item, mode) === 'danger').length;
  const busyCount = items.filter((item) => item.activityLevel === 'High' || item.activityLevel === 'Medium').length;
  const avgUtilization = (() => {
    const values = items.map((item) => item.utilizationPercent).filter((value): value is number => value != null);
    return values.length ? Math.round(values.reduce((sum, value) => sum + value, 0) / values.length) : 0;
  })();

  const currentWarehouse = warehouses.find((item) => item.id === warehouseId);
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
    if (!layout || saving || savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setError('');
    setSuccess('');
    try {
      if (!clear && [layout.x, layout.y, layout.width, layout.height].some((value) => !value.trim())) {
        setError('Cần nhập đủ X, Y, Rộng và Cao.');
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
      if (!clear && [payload.mapX, payload.mapY, payload.mapWidth, payload.mapHeight].some((value) => Number.isNaN(value))) {
        setError('X, Y, Rộng và Cao phải là số hợp lệ.');
        return;
      }
      await apiClient.put('/api/v1/putaway-tasks/locations/' + layout.locationId + '/layout', payload);
      setLayout(null);
      setSuccess(clear ? 'Đã xóa bố trí bản đồ.' : 'Đã cập nhật bố trí bản đồ.');
      await loadMap(warehouseId);
    } catch (failure) {
      setError(errorMessage(failure, 'Không thể cập nhật bố trí bản đồ.'));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const hasOverlap = filteredMapped.some((a, i) =>
    filteredMapped.slice(0, i).some((b) => Math.abs((a.mapX ?? 0) - (b.mapX ?? 0)) < 22 && Math.abs((a.mapY ?? 0) - (b.mapY ?? 0)) < 26),
  );

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kho & Vị trí"
        title="Bản đồ Kho & Bản đồ Nhiệt"
        description="Bản đồ vật lý dựa trên bố cục không gian được cấu hình thật. Bản đồ nhiệt sử dụng sức chứa, dịch chuyển 60 phút gần nhất và tác vụ cất hàng đang mở; vị trí chưa có tọa độ không được tự suy đoán."
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error}</p>}

      <UiMetricGrid>
        <UiMetric value={mappedItems.length} label="Đã bố trí" />
        <UiMetric value={unmappedItems.length} label="Chưa bố trí" />
        <UiMetric value={avgUtilization + '%'} label="Mức sử dụng trung bình" />
        <UiMetric value={busyCount} label="Khu vực có hoạt động" />
        <UiMetric value={criticalCount} label="Điểm cần chú ý" />
      </UiMetricGrid>

      <WarehouseMapTopCards
        inRouter={inRouter}
        items={items}
        mappedCount={mappedItems.length}
        avgUtilization={avgUtilization}
        biSnapshot={biSnapshot}
        loading={loading}
        saving={saving}
        biSyncBusy={biSyncBusy}
        biSyncResult={biSyncResult}
        onSyncBiSnapshot={() => void syncBiSnapshot()}
      />

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho bản đồ" value={warehouseId} onChange={(event) => setWarehouseId(Number(event.target.value))}>
            {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Chế độ nhiệt">
          <select aria-label="Chế độ heatmap" value={mode} onChange={(event) => setMode(event.target.value as 'utilization' | 'activity')}>
            <option value="utilization">Mức sử dụng / Sức chứa</option>
            <option value="activity">Cường độ vận hành</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm vị trí">
          <input aria-label="Tìm vị trí trên bản đồ" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Mã hoặc tên vị trí..." />
        </UiToolbarField>
        <button type="button" onClick={() => void loadMap(warehouseId)} disabled={!warehouseId || loading}>Làm mới</button>
      </UiToolbar>

      <p className={ageMinutes != null && ageMinutes > 5 ? 'warehouse-map-freshness stale' : 'warehouse-map-freshness'}>
        Ảnh chụp dữ liệu: {generatedAt ? generatedAt.toLocaleString('vi-VN') : '—'} • Tuổi dữ liệu: {ageMinutes == null ? '—' : ageMinutes + ' phút'}
        {ageMinutes != null && ageMinutes > 5 ? ' • Bản chụp đã quá 5 phút, cần làm mới trước khi điều phối.' : ''}
      </p>

      {loading ? (
        <p role="status">Đang tải bản đồ kho...</p>
      ) : warehouses.length === 0 ? (
        <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
      ) : (
        <>
          <UiCard title={'Bản đồ Kho' + (currentWarehouse ? ' • ' + currentWarehouse.code : '')}>
            {filteredMapped.length === 0 ? (
              <UiEmptyState title="Chưa có vị trí phù hợp đã được bố trí trên bản đồ." detail="Vị trí không có tọa độ thật sẽ nằm trong danh sách Chưa bố trí." />
            ) : (
              <div className="warehouse-map-canvas" aria-label="Bản đồ kho thật">
                <div className="warehouse-map-grid" aria-hidden="true" />
                {filteredMapped.map((item, idx) => (
                  <button
                    key={item.locationId}
                    type="button"
                    className={'warehouse-map-cell ' + heatClass(item, mode)}
                    style={{
                      left: (hasOverlap ? 3 + (idx % 3) * 32 : item.mapX) + '%',
                      top: (hasOverlap ? 4 + Math.floor(idx / 3) * 31 : item.mapY) + '%',
                      width: (hasOverlap ? 29 : item.mapWidth) + '%',
                      height: (hasOverlap ? 27 : item.mapHeight) + '%',
                    }}
                    onClick={() => openLayout(item)}
                    disabled={!canManage}
                    aria-label={'Vị trí ' + item.code}
                    title={item.structurePath || item.name}
                  >
                    <strong>{item.code}</strong>
                    <span>{mode === 'utilization' ? (item.utilizationPercent != null ? item.utilizationPercent + '%' : 'Chưa có') : (activityLabel[item.activityLevel] || item.activityLevel)}</span>
                    <small>{item.recentMovementCount} dịch chuyển • {item.activePutawayCount} tác vụ</small>
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

          <WarehouseMapBottomSections
            items={items}
            unmappedItems={unmappedItems}
            canManage={canManage}
            layout={layout}
            setLayout={setLayout}
            saving={saving}
            onOpenLayout={openLayout}
            onSaveLayout={(clear) => void saveLayout(clear)}
          />
        </>
      )}
    </UiPage>
  );
};

export default WarehouseMap;
