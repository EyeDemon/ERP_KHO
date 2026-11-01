import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import {
  UiBadge, UiCard, UiEmptyState, UiMetric, UiPage, UiPageHeader,
  UiTableScroll, UiTabs, UiToolbar, UiToolbarField,
} from '../ui/ProductionUi';
import './WarehouseLocations.css';
import {
  buildHierarchyRows, emptyForm, formFromLocation, type LocationCapacity, type LocationForm,
  locationStatus, locationTypeLabels, type LocationViewTab, messageOf, nullableNumber,
  splitStructurePath, structurePathOf, type Warehouse, type WarehouseLocation,
} from '../modules/catalog/WarehouseLocationTypes';
import {
  WarehouseLocationCapacityAndSystemSection, WarehouseLocationFormDrawer, WarehouseLocationGovernanceCard,
} from '../modules/catalog/WarehouseLocationViews';

const WarehouseLocations = () => {
  const canManage = usePermission('location.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState<number>(0);
  const [locations, setLocations] = useState<WarehouseLocation[]>([]);
  const [capacities, setCapacities] = useState<LocationCapacity[]>([]);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [typeFilter, setTypeFilter] = useState('all');
  const [activeViewTab, setActiveViewTab] = useState<LocationViewTab>('hierarchy');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<LocationForm>(emptyForm);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  const [topologyAuditResult, setTopologyAuditResult] = useState<{
    verificationId: string; zoneBinHierarchyStatus: string; capacityGuardrailActive: boolean;
  } | null>(null);
  const [topologyAuditBusy, setTopologyAuditBusy] = useState(false);
  const [virtualVerification, setVirtualVerification] = useState<{ verificationId: string; status: string } | null>(null);
  const [binBarcodeList, setBinBarcodeList] = useState('BIN-A01-01, BIN-A01-02');

  const loadWarehouses = useCallback(async () => {
    const response = await apiClient.get('/api/v1/putaway-tasks/location-warehouses');
    const items = response.data as Warehouse[];
    setWarehouses(items);
    setWarehouseId(current => current || items[0]?.id || 0);
    return items;
  }, []);

  const loadLocations = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) {
      setLocations([]); setCapacities([]); return;
    }
    const [locationResponse, capacityResponse] = await Promise.all([
      apiClient.get('/api/v1/putaway-tasks/locations', { params: { warehouseId: targetWarehouseId } }),
      apiClient.get('/api/v1/putaway-tasks/location-capacity', { params: { warehouseId: targetWarehouseId } }),
    ]);
    setLocations(locationResponse.data as WarehouseLocation[]);
    setCapacities(capacityResponse.data as LocationCapacity[]);
  }, []);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const items = await loadWarehouses();
      const target = warehouseId || items[0]?.id || 0;
      await loadLocations(target);
    } catch (failure) {
      setWarehouses([]); setLocations([]); setError(messageOf(failure, 'Không thể tải cấu trúc vị trí kho.'));
    } finally { setLoading(false); }
  }, [loadLocations, loadWarehouses, warehouseId]);

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!warehouseId || loading) return;
    setError('');
    void loadLocations(warehouseId).catch(failure => {
      setLocations([]); setCapacities([]);
      setError(messageOf(failure, 'Không thể tải vị trí hoặc sức chứa của kho đã chọn.'));
    });
  }, [warehouseId, loadLocations, loading]);

  const mapped = useMemo(() => locations.filter(location => !location.isSystemManaged && splitStructurePath(location.structurePath)), [locations]);
  const unmapped = useMemo(() => locations.filter(location => !location.isSystemManaged && !splitStructurePath(location.structurePath)), [locations]);
  const systemLocations = useMemo(() => locations.filter(location => location.isSystemManaged), [locations]);
  const hierarchyRows = useMemo(() => buildHierarchyRows(mapped), [mapped]);

  const filtered = useMemo(() => {
    const query = search.trim().toLocaleLowerCase('vi');
    return locations.filter(location => {
      const statusMatches =
        statusFilter === 'all' ||
        (statusFilter === 'active' && location.isActive && !location.isBlocked) ||
        (statusFilter === 'blocked' && location.isBlocked) ||
        (statusFilter === 'inactive' && !location.isActive);
      const typeMatches = typeFilter === 'all' || location.locationType === typeFilter;
      const searchMatches =
        !query ||
        [location.code, location.name, location.structurePath ?? '', location.storageClass ?? '', location.locationType]
          .join(' ')
          .toLocaleLowerCase('vi')
          .includes(query);
      return statusMatches && typeMatches && searchMatches;
    });
  }, [locations, search, statusFilter, typeFilter]);

  const zoneCount = new Set(mapped.map(location => splitStructurePath(location.structurePath)?.[0]).filter(Boolean)).size;
  const blockedCount = locations.filter(location => location.isBlocked).length;
  const inactiveCount = locations.filter(location => !location.isActive).length;
  const nearCapacityCount = capacities.filter(item => item.state === 'NearCapacity').length;
  const capacityIssueCount = capacities.filter(item => ['OverCapacity', 'ProfileIncomplete', 'CompatibilityConflict'].includes(item.state)).length;
  const editing = form.id !== undefined;
  const editingLocation = editing ? locations.find(location => location.id === form.id) : undefined;
  const hasLockedStructure = Boolean(editingLocation?.structurePath);
  const constraintsChanged =
    Boolean(editingLocation) &&
    ((editingLocation?.storageClass || '') !== form.storageClass.trim().toUpperCase() ||
      (editingLocation?.maxWeightKg ?? null) !== nullableNumber(form.maxWeightKg) ||
      (editingLocation?.maxVolumeM3 ?? null) !== nullableNumber(form.maxVolumeM3) ||
      (editingLocation?.maxPalletEquivalent ?? null) !== nullableNumber(form.maxPalletEquivalent));

  const openCreate = () => { setForm(emptyForm()); setFormError(''); setSuccess(''); setShowForm(true); };
  const openEdit = (location: WarehouseLocation) => { setForm(formFromLocation(location)); setFormError(''); setSuccess(''); setShowForm(true); };
  const closeForm = () => { if (saving || savingRef.current) return; setShowForm(false); setFormError(''); };

  const save = async (event?: React.FormEvent) => {
    event?.preventDefault();
    if (saving || savingRef.current || !warehouseId) return;
    setFormError(''); setSuccess('');

    const required = [['Mã vị trí', form.code], ['Tên vị trí', form.name]] as const;
    const missing = required.find(([, value]) => !value.trim());
    if (missing) return setFormError(missing[0] + ' là bắt buộc.');

    const structureFields = [['Zone', form.zone], ['Aisle', form.aisle], ['Rack', form.rack], ['Level', form.level], ['Bin', form.bin]] as const;
    const hasAnyStructure = structureFields.some(([, value]) => value.trim());
    const structureMissing = structureFields.find(([, value]) => !value.trim());
    if ((!editing || hasAnyStructure) && structureMissing) return setFormError(structureMissing[0] + ' là bắt buộc khi gán cấu trúc vật lý.');

    const structurePath = hasAnyStructure ? structurePathOf(form) : null;
    savingRef.current = true; setSaving(true);
    try {
      if (editing && form.id !== undefined) {
        if (!form.rowVersion) return setFormError('Thiếu phiên bản dữ liệu. Vui lòng tải lại danh sách.');
        await apiClient.put('/api/v1/putaway-tasks/locations/' + form.id, {
          name: form.name.trim(), structurePath, updateConstraints: constraintsChanged,
          storageClass: form.storageClass.trim() || null, maxWeightKg: nullableNumber(form.maxWeightKg),
          maxVolumeM3: nullableNumber(form.maxVolumeM3), maxPalletEquivalent: nullableNumber(form.maxPalletEquivalent),
          isActive: form.isActive, isBlocked: form.isBlocked, isPickable: form.locationType === 'Storage', isReceivable: false, rowVersion: form.rowVersion,
        });
        setSuccess('Cập nhật vị trí thành công.');
      } else {
        await apiClient.post('/api/v1/putaway-tasks/locations', {
          warehouseId, code: form.code.trim(), name: form.name.trim(), structurePath,
          storageClass: form.storageClass.trim() || null, maxWeightKg: nullableNumber(form.maxWeightKg),
          maxVolumeM3: nullableNumber(form.maxVolumeM3), maxPalletEquivalent: nullableNumber(form.maxPalletEquivalent),
          locationType: form.locationType, isPickable: form.locationType === 'Storage', isReceivable: false,
        });
        setSuccess('Tạo vị trí thành công.');
      }
      setShowForm(false);
      await loadLocations(warehouseId);
    } catch (failure) {
      setFormError(messageOf(failure, 'Không thể lưu vị trí.'));
    } finally { savingRef.current = false; setSaving(false); }
  };

  const currentWarehouse = warehouses.find(warehouse => warehouse.id === warehouseId);
  const inRouter = useInRouterContext();

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Dữ liệu nền"
        title="Cấu trúc vị trí kho"
        description="Quản lý Khu vực → Dãy → Kệ → Tầng → Ô lưu trữ, phân lớp lưu trữ và sức chứa trên vị trí kho thật. Luồng cất hàng dùng chính ràng buộc này để chặn vị trí không tương thích hoặc vượt sức chứa."
        actions={canManage ? <button type="button" className="ui-primary-button" onClick={openCreate}>+ Thêm vị trí mới</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void load()}>Thử lại</button></p>}

      <div className="ui-metric-grid" style={{ gridTemplateColumns: 'repeat(4, minmax(0, 1fr))' }}>
        <UiMetric value={locations.length} label="Tổng vị trí" />
        <UiMetric value={zoneCount} label="Khu vực đã cấu trúc" />
        <UiMetric value={mapped.length} label="Vị trí đã gắn cấu trúc" />
        <UiMetric value={unmapped.length} label="Chưa gắn cấu trúc" />
        <UiMetric value={blockedCount} label="Bị khóa" />
        <UiMetric value={inactiveCount} label="Ngừng hoạt động" />
        <UiMetric value={nearCapacityCount} label="Gần đầy" />
        <UiMetric value={capacityIssueCount} label="Sức chứa cần xử lý" />
      </div>

      <div data-testid="warehouse-topology-tabs" style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 12, padding: 6, borderRadius: 10, background: '#f1f5f9', border: '1px solid #cbd5e1' }}>
        {[
          { to: '/warehouses', label: 'Danh mục Kho hàng', testId: 'tab-warehouses', active: false },
          { to: '/warehouse-locations', label: 'Cấu trúc & Mã vạch Vị trí Kệ', testId: 'tab-warehouse-locations', active: true },
          { to: '/warehouse-map', label: 'Bản đồ Nhiệt Kho 2D', testId: 'tab-warehouse-map', active: false },
        ].map(tab => {
          const style: React.CSSProperties = {
            padding: '8px 14px', borderRadius: 8, fontWeight: 700, fontSize: 13, textDecoration: 'none',
            background: tab.active ? '#0f172a' : '#ffffff', color: tab.active ? '#ffffff' : '#334155',
            border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
          };
          return inRouter ? (
            <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>{tab.label}</Link>
          ) : (
            <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>{tab.label}</a>
          );
        })}
      </div>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho vị trí" value={warehouseId} onChange={event => setWarehouseId(Number(event.target.value))}>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm vị trí">
          <input aria-label="Tìm vị trí" value={search} onChange={event => setSearch(event.target.value)} placeholder="Mã, tên hoặc đường dẫn" />
        </UiToolbarField>
        <UiToolbarField label="Trạng thái">
          <select aria-label="Trạng thái vị trí" value={statusFilter} onChange={event => setStatusFilter(event.target.value)}>
            <option value="all">Tất cả</option>
            <option value="active">Đang hoạt động</option>
            <option value="blocked">Bị khóa</option>
            <option value="inactive">Ngừng hoạt động</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Loại">
          <select aria-label="Loại vị trí" value={typeFilter} onChange={event => setTypeFilter(event.target.value)}>
            <option value="all">Tất cả</option>
            <option value="Storage">Lưu trữ</option>
            <option value="Damaged">Hư hỏng</option>
            <option value="Rejected">Hàng bị từ chối</option>
            <option value="Staging">Khu tập kết xuất hàng</option>
            <option value="Receiving">Nhận hàng</option>
            <option value="Legacy">Tương thích hệ thống</option>
          </select>
        </UiToolbarField>
      </UiToolbar>

      <UiTabs<LocationViewTab>
        ariaLabel="Phân mục cấu trúc vị trí kho"
        activeTab={activeViewTab}
        onChange={setActiveViewTab}
        tabs={[
          { id: 'hierarchy', label: 'Cây cấu trúc vật lý (5 tầng)', badge: mapped.length, testId: 'tab-view-hierarchy' },
          { id: 'capacity', label: 'Danh sách vị trí & Sức chứa (3 chiều)', badge: filtered.length, testId: 'tab-view-capacity' },
          { id: 'system', label: 'Vị trí hệ thống (Tiếp nhận / Tập kết / Kiểm định / Hủy bỏ)', badge: systemLocations.length, testId: 'tab-view-system' },
        ]}
      />

      {loading ? (
        <p role="status">Đang tải cấu trúc vị trí...</p>
      ) : warehouses.length === 0 ? (
        <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
      ) : (
        <>
          {activeViewTab === 'hierarchy' && (
            <>
              <UiCard title={'Cây cấu trúc vật lý' + (currentWarehouse ? ' • ' + currentWarehouse.code : '')}>
                {hierarchyRows.length === 0 ? (
                  <UiEmptyState title="Kho chưa có vị trí được gắn đường dẫn cấu trúc." detail="Vị trí cũ vẫn được giữ nguyên ở bảng Chưa gắn cấu trúc bên dưới." />
                ) : (
                  <div style={{ maxHeight: 260, overflowY: 'auto' }}>
                    <UiTableScroll>
                      <table aria-label="Cây cấu trúc vị trí thật" className="ui-table-compact">
                        <thead><tr><th>Cấp</th><th>Mã / Tên</th><th>Đường dẫn</th><th>Loại</th><th>Trạng thái</th><th>Khả dụng</th>{canManage && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
                        <tbody>
                          {hierarchyRows.map(row => {
                            const status = row.location ? locationStatus(row.location) : null;
                            return (
                              <tr key={row.key}>
                                <td><span className={'warehouse-location-depth depth-' + row.depth}>{row.level}</span></td>
                                <td><strong style={{ whiteSpace: 'nowrap' }}>{row.code}</strong>{row.location && <><br /><small>{row.location.name}</small></>}</td>
                                <td>{row.path}</td>
                                <td>{row.location ? locationTypeLabels[row.location.locationType] || row.location.locationType : '—'}</td>
                                <td>{status ? <UiBadge tone={status.tone}>{status.label}</UiBadge> : '—'}</td>
                                <td>{row.location ? (row.location.isPickable ? 'Có thể lấy hàng' : row.location.isReceivable ? 'Có thể nhận' : 'Không lấy hàng') : '—'}</td>
                                {canManage && <td style={{ whiteSpace: 'nowrap' }}>{row.location && !row.location.isSystemManaged ? <button type="button" onClick={() => openEdit(row.location!)} style={{ whiteSpace: 'nowrap' }}>Sửa</button> : '—'}</td>}
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </UiTableScroll>
                  </div>
                )}
              </UiCard>

              {unmapped.length > 0 && (
                <UiCard title="Vị trí chưa gắn cấu trúc">
                  <div style={{ maxHeight: 160, overflowY: 'auto' }}>
                    <UiTableScroll>
                      <table aria-label="Vị trí thật chưa gắn cấu trúc" className="ui-table-compact">
                        <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Trạng thái</th>{canManage && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
                        <tbody>
                          {unmapped.map(location => {
                            const status = locationStatus(location);
                            return <tr key={location.id}><td style={{ whiteSpace: 'nowrap' }}><strong>{location.code}</strong></td><td>{location.name}</td><td>{locationTypeLabels[location.locationType] || location.locationType}</td><td><UiBadge tone={status.tone}>{status.label}</UiBadge></td>{canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => openEdit(location)} style={{ whiteSpace: 'nowrap' }}>Gắn cấu trúc / Sửa</button></td>}</tr>;
                          })}
                        </tbody>
                      </table>
                    </UiTableScroll>
                  </div>
                </UiCard>
              )}
            </>
          )}

          <WarehouseLocationCapacityAndSystemSection activeViewTab={activeViewTab} capacities={capacities} filtered={filtered} systemLocations={systemLocations} canManage={canManage} openEdit={openEdit} />
        </>
      )}

      <WarehouseLocationGovernanceCard
        warehouseId={warehouseId} warehouses={warehouses} topologyAuditBusy={topologyAuditBusy} setTopologyAuditBusy={setTopologyAuditBusy}
        topologyAuditResult={topologyAuditResult} setTopologyAuditResult={setTopologyAuditResult} virtualVerification={virtualVerification}
        setVirtualVerification={setVirtualVerification} binBarcodeList={binBarcodeList} setBinBarcodeList={setBinBarcodeList} setError={setError} setSuccess={setSuccess}
      />

      <WarehouseLocationFormDrawer open={showForm && canManage} editing={editing} hasLockedStructure={hasLockedStructure} saving={saving} form={form} setForm={setForm} formError={formError} closeForm={closeForm} save={save} />
    </UiPage>
  );
};

export default WarehouseLocations;
