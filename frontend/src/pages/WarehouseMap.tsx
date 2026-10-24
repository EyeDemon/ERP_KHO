import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
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
type ProductivityAndBiSnapshot = {
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
  ProfileIncomplete: 'Thiếu thông số',
  CompatibilityConflict: 'Xung đột phân lớp',
  Blocked: 'Bị khóa',
  Inactive: 'Ngừng hoạt động',
};

const activityLabel: Record<string, string> = {
  High: 'Cao',
  Medium: 'Trung bình',
  Low: 'Thấp',
};

const errorMessage = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  if (response.response?.status === 409) return (response.response.data?.message || 'Dữ liệu đã thay đổi.') + ' Vui lòng tải lại bản đồ.';
  return response.response?.data?.message || fallback;
};

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
      const res = await apiClient.post('/api/Reports/sync-bi-snapshot', { warehouseId });
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
    let biLoadError = '';
    const [response, biRes] = await Promise.all([
      apiClient.get('/api/putaway-tasks/location-map', { params: { warehouseId: targetWarehouseId } }),
      apiClient.get<ProductivityAndBiSnapshot>('/api/Reports/productivity-and-bi-snapshot', { params: { warehouseId: targetWarehouseId } }).catch((err: any) => {
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
    if (!layout || saving || savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setError('');
    setSuccess('');
    try {
      if (!clear && [layout.x, layout.y, layout.width, layout.height].some(value => !value.trim())) {
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
      if (!clear && [payload.mapX, payload.mapY, payload.mapWidth, payload.mapHeight].some(value => Number.isNaN(value))) {
        setError('X, Y, Rộng và Cao phải là số hợp lệ.');
        return;
      }
      await apiClient.put('/api/putaway-tasks/locations/' + layout.locationId + '/layout', payload);
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

      <div
        data-testid="warehouse-topology-tabs"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          marginBottom: 12,
          padding: 6,
          borderRadius: 10,
          background: '#f1f5f9',
          border: '1px solid #cbd5e1',
        }}
      >
        {[
          { to: '/warehouses', label: 'Tab 1: Danh mục Kho hàng (/warehouses)', testId: 'tab-warehouses', active: false },
          { to: '/warehouse-locations', label: 'Tab 2: Cấu trúc & Mã vạch Vị trí Kệ (/warehouse-locations)', testId: 'tab-warehouse-locations', active: false },
          { to: '/warehouse-map', label: 'Tab 3: Bản đồ Nhiệt Kho 2D (/warehouse-map)', testId: 'tab-warehouse-map', active: true },
        ].map((tab) => {
          const style: React.CSSProperties = {
            padding: '8px 14px',
            borderRadius: 8,
            fontWeight: 700,
            fontSize: 13,
            textDecoration: 'none',
            background: tab.active ? '#0f172a' : '#ffffff',
            color: tab.active ? '#ffffff' : '#334155',
            border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
          };
          return inRouter ? (
            <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
              {tab.label}
            </Link>
          ) : (
            <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
              {tab.label}
            </a>
          );
        })}
      </div>
      <UiCard title="Tổng quan Mật độ Sơ đồ Không gian Kho">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, maxWidth: '100%' }}>
          <div>
            <strong>Độ phủ tọa độ sơ đồ không gian</strong><br />
            {items.length > 0 ? Math.round((mappedItems.length / items.length) * 100) : 0}% ({mappedItems.length}/{items.length} vị trí)
          </div>
          <div>
            <strong>Mật độ lấp đầy & Ô lưu trữ tải cao (≥75%)</strong><br />
            TB {avgUtilization}% • {items.filter(item => (item.utilizationPercent ?? 0) >= 75).length} vị trí tải cao
          </div>
          <div>
            <strong>Cường độ vận hành & Cất hàng chờ xử lý</strong><br />
            {items.reduce((sum, item) => sum + (item.recentMovementCount || 0), 0)} dịch chuyển 60p • {items.reduce((sum, item) => sum + (item.activePutawayCount || 0), 0)} tác vụ cất hàng
          </div>
        </div>
      </UiCard>

      <UiCard title="Năng suất Tác vụ Kho, Bản đồ Nhiệt & Điểm chuẩn Xuất BI">
        <div className="ui-inline-wrap">
          <UiBadge tone="success">{`Cất hàng hoàn tất: ${biSnapshot?.completedPutawaysCount ?? 0}`}</UiBadge>
          <UiBadge tone="success">{`Lấy hàng hoàn tất: ${biSnapshot?.completedPicksCount ?? 0}`}</UiBadge>
          <UiBadge tone="success">{`Đóng gói hoàn tất: ${biSnapshot?.completedPacksCount ?? 0}`}</UiBadge>
          <UiBadge>{`Tổng dịch chuyển nội bộ: ${biSnapshot?.totalMovesCount ?? items.reduce((sum, item) => sum + (item.recentMovementCount || 0), 0)}`}</UiBadge>
          <UiBadge tone="warning">{`Mốc đồng bộ sổ cái: #${biSnapshot?.ledgerWatermarkId ?? 0} • Mốc kiểm toán: #${biSnapshot?.auditWatermarkId ?? 0}`}</UiBadge>
          <UiBadge tone={biSnapshot?.readyForBiExport !== false ? 'success' : 'neutral'}>
            {biSnapshot?.readyForBiExport !== false ? `Sẵn sàng đồng bộ BI (${biSnapshot?.stockSnapshotRowCount ?? items.length} dòng)` : 'Đang đồng bộ BI'}
          </UiBadge>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 8 }}>
          <button type="button" disabled={loading || saving || biSyncBusy} onClick={() => void syncBiSnapshot()}>
            Đồng bộ Ảnh chụp Bản đồ Nhiệt &amp; Chuẩn dữ liệu BI
          </button>
          {biSyncResult && (
            <UiBadge tone="success">
              {biSyncResult.snapshotId} • {({ BI_SNAPSHOT_SYNCED: 'Đã đồng bộ chuẩn dữ liệu BI', READY_FOR_BI_EXPORT: 'Sẵn sàng xuất dữ liệu BI', SYNCED: 'Đã đồng bộ' } as Record<string, string>)[biSyncResult.status] || biSyncResult.status}
            </UiBadge>
          )}
        </div>
      </UiCard>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho bản đồ" value={warehouseId} onChange={event => setWarehouseId(Number(event.target.value))}>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Chế độ nhiệt">
          <select aria-label="Chế độ heatmap" value={mode} onChange={event => setMode(event.target.value as 'utilization' | 'activity')}>
            <option value="utilization">Mức sử dụng / Sức chứa</option>
            <option value="activity">Cường độ vận hành</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm vị trí">
          <input aria-label="Tìm vị trí trên bản đồ" value={search} onChange={event => setSearch(event.target.value)} placeholder="Mã, tên, đường dẫn, phân lớp" />
        </UiToolbarField>
        <button type="button" onClick={() => void loadMap(warehouseId)} disabled={!warehouseId || loading}>Làm mới</button>
      </UiToolbar>

      <p className={ageMinutes != null && ageMinutes > 5 ? 'warehouse-map-freshness stale' : 'warehouse-map-freshness'}>
        Ảnh chụp dữ liệu: {generatedAt ? generatedAt.toLocaleString('vi-VN') : '—'} • Tuổi dữ liệu: {ageMinutes == null ? '—' : ageMinutes + ' phút'}
        {ageMinutes != null && ageMinutes > 5 ? ' • Dữ liệu cũ, cần làm mới trước khi điều phối.' : ''}
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

          <UiCard title="Chi tiết Bản đồ Nhiệt Vận hành">
            <UiTableScroll>
              <table aria-label="Chi tiết heatmap kho thật" className="ui-table-compact">
                <thead><tr><th>Vị trí</th><th>Phân lớp lưu trữ</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Mức sử dụng</th><th>Sức chứa</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Dịch chuyển 60p</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Cất hàng mở</th><th>Hoạt động</th>{canManage && <th style={{ whiteSpace: 'nowrap' }}>Bố trí</th>}</tr></thead>
                <tbody>{items.map(item => (
                  <tr key={item.locationId}>
                    <td><strong style={{ whiteSpace: 'nowrap' }}>{item.code}</strong><br /><small>{item.structurePath?.split('/').join(' / ') || item.name}</small></td>
                    <td>{item.storageClass || '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.utilizationPercent != null ? item.utilizationPercent + '%' : 'Chưa có'}</td>
                    <td><UiBadge tone={capacityTone(item)}>{stateLabel[item.capacityState] || item.capacityState}</UiBadge></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.recentMovementCount}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.activePutawayCount}</td>
                    <td>{activityLabel[item.activityLevel] || item.activityLevel}</td>
                    {canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => openLayout(item)} disabled={!item.rowVersion} style={{ whiteSpace: 'nowrap' }}>{mapped(item) ? 'Sửa bố trí' : 'Bố trí'}</button></td>}
                  </tr>
                ))}</tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          {unmappedItems.length > 0 && (
            <UiCard title="Vị trí chưa bố trí">
              <p className="ui-muted-text">Hệ thống không tự suy đoán tọa độ từ đường dẫn cấu trúc. Quản lý kho phải bố trí bằng tọa độ phần trăm.</p>
              <UiTableScroll>
                <table aria-label="Location chưa bố trí bản đồ" className="ui-table-compact">
                  <thead><tr><th>Mã</th><th>Tên</th><th>Đường dẫn cấu trúc</th><th>Phân lớp lưu trữ</th>{canManage && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
                  <tbody>{unmappedItems.map(item => (
                    <tr key={item.locationId}>
                      <td style={{ whiteSpace: 'nowrap' }}><strong>{item.code}</strong></td><td>{item.name}</td><td>{item.structurePath?.split('/').join(' / ') || '—'}</td><td>{item.storageClass || '—'}</td>
                      {canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => openLayout(item)} disabled={!item.rowVersion} style={{ whiteSpace: 'nowrap' }}>Bố trí</button></td>}
                    </tr>
                  ))}</tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          )}

          {layout && canManage && (
            <UiCard title={'Bố trí bản đồ • ' + layout.code}>
              <p className="ui-muted-text">Tọa độ dùng phần trăm khung bản đồ 0–100. X + Rộng và Y + Cao không được vượt 100.</p>
              <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12, maxWidth: '100%' }}>
                <label className="ui-stack"><span>X (%)</span><input aria-label="Map X" type="number" min="0" max="100" step="0.01" value={layout.x} onChange={event => setLayout(current => current && ({ ...current, x: event.target.value }))} /></label>
                <label className="ui-stack"><span>Y (%)</span><input aria-label="Map Y" type="number" min="0" max="100" step="0.01" value={layout.y} onChange={event => setLayout(current => current && ({ ...current, y: event.target.value }))} /></label>
                <label className="ui-stack"><span>Rộng (%)</span><input aria-label="Map Width" type="number" min="0.01" max="100" step="0.01" value={layout.width} onChange={event => setLayout(current => current && ({ ...current, width: event.target.value }))} /></label>
                <label className="ui-stack"><span>Cao (%)</span><input aria-label="Map Height" type="number" min="0.01" max="100" step="0.01" value={layout.height} onChange={event => setLayout(current => current && ({ ...current, height: event.target.value }))} /></label>
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
