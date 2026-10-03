import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { permissionError } from '../services/permissionPresentation';
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
import './WarehouseStructure.css';

type Warehouse = { id: number; code: string; name: string; isActive: boolean };
type Location = {
  id: number;
  warehouseId: number;
  zoneId?: number | null;
  zoneCode?: string | null;
  rackLevelId?: number | null;
  aisleCode?: string | null;
  rackCode?: string | null;
  levelNo?: number | null;
  code: string;
  name: string;
  barcode?: string | null;
  pickPriority?: number | null;
  putawayPriority?: number | null;
  locationType: string;
  isActive: boolean;
  isBlocked: boolean;
  isPickable: boolean;
  isReceivable: boolean;
  isSystemManaged: boolean;
  rowVersion?: string | null;
};
type Level = { id: number; rackId: number; levelNo: number; rowVersion?: string | null; locations: Location[] };
type Rack = { id: number; aisleId: number; code: string; name?: string | null; rowVersion?: string | null; levels: Level[] };
type Aisle = { id: number; zoneId: number; code: string; name?: string | null; rowVersion?: string | null; racks: Rack[] };
type Zone = {
  id: number;
  warehouseId: number;
  code: string;
  name: string;
  zoneType: string;
  pickPriority?: number | null;
  putawayPriority?: number | null;
  isActive: boolean;
  rowVersion?: string | null;
  aisles: Aisle[];
  locations: Location[];
};
type Structure = {
  warehouseId: number;
  warehouseCode: string;
  warehouseName: string;
  zones: Zone[];
  systemLocations: Location[];
  unmappedLocations: Location[];
};

type StructureKind = 'zone' | 'aisle' | 'rack' | 'level';
type EditNode =
  | { kind: 'zone'; zone: Zone }
  | { kind: 'aisle'; zoneId: number; aisle: Aisle }
  | { kind: 'rack'; aisleId: number; rack: Rack }
  | null;

const zoneLabels: Record<string, string> = {
  RECEIVING: 'Khu nhận hàng',
  STORAGE: 'Khu lưu trữ',
  PICKING: 'Khu lấy hàng',
  QC: 'Khu kiểm tra chất lượng',
  QUARANTINE: 'Khu cách ly',
  STAGING: 'Khu chờ xuất',
  SHIPPING: 'Khu xuất hàng',
};

const locationLabels: Record<string, string> = {
  Receiving: 'Nhận hàng',
  Storage: 'Lưu trữ',
  Damaged: 'Hư hỏng',
  Rejected: 'Hàng bị từ chối',
  Legacy: 'Tương thích hệ thống',
};

const toNumber = (value: string): number | null => value.trim() ? Number(value) : null;

const WarehouseStructure = () => {
  const canManageStructure = usePermission('warehouse_zone.manage');
  const canManageLocations = usePermission('location.manage');

  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [structure, setStructure] = useState<Structure | null>(null);
  const [loading, setLoading] = useState(true);
  const [structureLoading, setStructureLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [structureKind, setStructureKind] = useState<StructureKind>('zone');
  const [parentId, setParentId] = useState<number | ''>('');
  const [nodeCode, setNodeCode] = useState('');
  const [nodeName, setNodeName] = useState('');
  const [zoneType, setZoneType] = useState('STORAGE');
  const [pickPriority, setPickPriority] = useState('');
  const [putawayPriority, setPutawayPriority] = useState('');
  const [levelNo, setLevelNo] = useState('');
  const [nodeSaving, setNodeSaving] = useState(false);
  const [editNode, setEditNode] = useState<EditNode>(null);

  const [editingLocation, setEditingLocation] = useState<Location | null>(null);
  const [locationCode, setLocationCode] = useState('');
  const [locationName, setLocationName] = useState('');
  const [locationZoneId, setLocationZoneId] = useState<number | ''>('');
  const [locationLevelId, setLocationLevelId] = useState<number | ''>('');
  const [locationType, setLocationType] = useState('Storage');
  const [locationBarcode, setLocationBarcode] = useState('');
  const [locationPickPriority, setLocationPickPriority] = useState('');
  const [locationPutawayPriority, setLocationPutawayPriority] = useState('');
  const [locationActive, setLocationActive] = useState(true);
  const [locationBlocked, setLocationBlocked] = useState(false);
  const [locationPickable, setLocationPickable] = useState(true);
  const [locationSaving, setLocationSaving] = useState(false);

  const loadStructure = useCallback(async (id: number) => {
    setStructureLoading(true);
    setError('');
    try {
      const response = await apiClient.get<Structure>(`/api/warehouses/${id}/structure`);
      setStructure(response.data);
    } catch (failure) {
      setStructure(null);
      setError(permissionError(failure, 'Không thể tải cấu trúc vị trí. Vui lòng thử lại.'));
    } finally {
      setStructureLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      try {
        const response = await apiClient.get<Warehouse[]>('/api/warehouses');
        if (!active) return;
        const available = response.data.filter(item => item.isActive);
        setWarehouses(available);
        const initial = available[0]?.id ?? '';
        setWarehouseId(initial);
        if (initial) await loadStructure(initial);
      } catch (failure) {
        if (active) setError(permissionError(failure, 'Không thể tải danh sách kho. Vui lòng thử lại.'));
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [loadStructure]);

  const aisles = useMemo(() => structure?.zones.flatMap(zone => zone.aisles.map(aisle => ({ ...aisle, zoneCode: zone.code }))) ?? [], [structure]);
  const racks = useMemo(() => aisles.flatMap(aisle => aisle.racks.map(rack => ({ ...rack, zoneCode: aisle.zoneCode, aisleCode: aisle.code }))), [aisles]);
  const levels = useMemo(() => racks.flatMap(rack => rack.levels.map(level => ({ ...level, zoneCode: rack.zoneCode, aisleCode: rack.aisleCode, rackCode: rack.code }))), [racks]);
  const userLocations = useMemo(() => {
    if (!structure) return [];
    return structure.zones.flatMap(zone => [
      ...zone.locations,
      ...zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level => level.locations))),
    ]);
  }, [structure]);

  const resetStructureForm = () => {
    setParentId('');
    setNodeCode('');
    setNodeName('');
    setZoneType('STORAGE');
    setPickPriority('');
    setPutawayPriority('');
    setLevelNo('');
  };

  const resetLocationForm = () => {
    setEditingLocation(null);
    setLocationCode('');
    setLocationName('');
    setLocationZoneId('');
    setLocationLevelId('');
    setLocationType('Storage');
    setLocationBarcode('');
    setLocationPickPriority('');
    setLocationPutawayPriority('');
    setLocationActive(true);
    setLocationBlocked(false);
    setLocationPickable(true);
  };

  const selectWarehouse = (value: string) => {
    const id = value ? Number(value) : '';
    setWarehouseId(id);
    setSuccess('');
    setEditNode(null);
    resetLocationForm();
    if (id) void loadStructure(id);
    else setStructure(null);
  };

  const submitStructure = async (event: FormEvent) => {
    event.preventDefault();
    if (!warehouseId || nodeSaving) return;
    setNodeSaving(true);
    setError('');
    setSuccess('');
    try {
      if (structureKind === 'zone') {
        await apiClient.post(`/api/warehouses/${warehouseId}/zones`, {
          code: nodeCode,
          name: nodeName,
          zoneType,
          pickPriority: toNumber(pickPriority),
          putawayPriority: toNumber(putawayPriority),
        });
      } else if (structureKind === 'aisle') {
        await apiClient.post(`/api/warehouses/${warehouseId}/zones/${parentId}/aisles`, { code: nodeCode, name: nodeName || null });
      } else if (structureKind === 'rack') {
        await apiClient.post(`/api/warehouses/${warehouseId}/aisles/${parentId}/racks`, { code: nodeCode, name: nodeName || null });
      } else {
        await apiClient.post(`/api/warehouses/${warehouseId}/racks/${parentId}/levels`, { levelNo: Number(levelNo) });
      }
      setSuccess('Đã tạo cấu trúc vị trí.');
      resetStructureForm();
      await loadStructure(warehouseId);
    } catch (failure) {
      setError(permissionError(failure, 'Không thể tạo cấu trúc vị trí. Vui lòng kiểm tra dữ liệu và thử lại.'));
    } finally {
      setNodeSaving(false);
    }
  };

  const submitNodeEdit = async (event: FormEvent) => {
    event.preventDefault();
    if (!warehouseId || !editNode || nodeSaving) return;
    setNodeSaving(true);
    setError('');
    setSuccess('');
    try {
      if (editNode.kind === 'zone') {
        await apiClient.put(`/api/warehouses/${warehouseId}/zones/${editNode.zone.id}`, {
          name: nodeName,
          zoneType,
          pickPriority: toNumber(pickPriority),
          putawayPriority: toNumber(putawayPriority),
          isActive: locationActive,
          rowVersion: editNode.zone.rowVersion,
        });
      } else if (editNode.kind === 'aisle') {
        await apiClient.put(`/api/warehouses/${warehouseId}/zones/${editNode.zoneId}/aisles/${editNode.aisle.id}`, {
          name: nodeName || null,
          rowVersion: editNode.aisle.rowVersion,
        });
      } else {
        await apiClient.put(`/api/warehouses/${warehouseId}/aisles/${editNode.aisleId}/racks/${editNode.rack.id}`, {
          name: nodeName || null,
          rowVersion: editNode.rack.rowVersion,
        });
      }
      setSuccess('Đã cập nhật cấu trúc vị trí.');
      setEditNode(null);
      resetStructureForm();
      await loadStructure(warehouseId);
    } catch (failure) {
      setError(permissionError(failure, 'Không thể cập nhật cấu trúc vị trí. Vui lòng tải lại và thử lại.'));
    } finally {
      setNodeSaving(false);
    }
  };

  const beginNodeEdit = (target: Exclude<EditNode, null>) => {
    setEditNode(target);
    if (target.kind === 'zone') {
      setNodeName(target.zone.name);
      setZoneType(target.zone.zoneType);
      setPickPriority(target.zone.pickPriority?.toString() ?? '');
      setPutawayPriority(target.zone.putawayPriority?.toString() ?? '');
      setLocationActive(target.zone.isActive);
    } else if (target.kind === 'aisle') {
      setNodeName(target.aisle.name ?? '');
    } else {
      setNodeName(target.rack.name ?? '');
    }
  };

  const beginLocationEdit = (location: Location) => {
    setEditingLocation(location);
    setLocationCode(location.code);
    setLocationName(location.name);
    setLocationZoneId(location.zoneId ?? '');
    setLocationLevelId(location.rackLevelId ?? '');
    setLocationType(location.locationType);
    setLocationBarcode(location.barcode ?? '');
    setLocationPickPriority(location.pickPriority?.toString() ?? '');
    setLocationPutawayPriority(location.putawayPriority?.toString() ?? '');
    setLocationActive(location.isActive);
    setLocationBlocked(location.isBlocked);
    setLocationPickable(location.isPickable);
  };

  const submitLocation = async (event: FormEvent) => {
    event.preventDefault();
    if (!warehouseId || locationSaving) return;
    setLocationSaving(true);
    setError('');
    setSuccess('');
    try {
      if (editingLocation) {
        await apiClient.patch(`/api/locations/${editingLocation.id}`, {
          zoneId: locationZoneId || null,
          rackLevelId: locationLevelId || null,
          name: locationName,
          barcode: locationBarcode || null,
          pickPriority: toNumber(locationPickPriority),
          putawayPriority: toNumber(locationPutawayPriority),
          isActive: locationActive,
          isBlocked: locationBlocked,
          isPickable: locationPickable,
          isReceivable: editingLocation.isReceivable,
          rowVersion: editingLocation.rowVersion,
        });
        setSuccess('Đã cập nhật vị trí.');
      } else {
        await apiClient.post('/api/locations', {
          warehouseId,
          zoneId: locationZoneId || null,
          rackLevelId: locationLevelId || null,
          code: locationCode,
          name: locationName,
          barcode: locationBarcode || null,
          pickPriority: toNumber(locationPickPriority),
          putawayPriority: toNumber(locationPutawayPriority),
          locationType,
          isPickable: locationType === 'Storage' ? locationPickable : false,
          isReceivable: false,
        });
        setSuccess('Đã tạo vị trí.');
      }
      resetLocationForm();
      await loadStructure(warehouseId);
    } catch (failure) {
      setError(permissionError(failure, 'Không thể lưu vị trí. Vui lòng kiểm tra dữ liệu và thử lại.'));
    } finally {
      setLocationSaving(false);
    }
  };

  const selectedZoneLevels = levels.filter(level => {
    const zone = structure?.zones.find(item => item.id === locationZoneId);
    return zone?.aisles.some(aisle => aisle.racks.some(rack => rack.levels.some(item => item.id === level.id)));
  });

  const zoneCount = structure?.zones.length ?? 0;
  const aisleCount = aisles.length;
  const rackCount = racks.length;
  const levelCount = levels.length;
  const locationCount = userLocations.length + (structure?.systemLocations.length ?? 0) + (structure?.unmappedLocations.length ?? 0);

  return (
    <UiPage>
      <div className="warehouse-structure-page">
        <UiPageHeader
          eyebrow="Kho & Vị trí"
          title="Cấu trúc vị trí kho"
          description="Quản lý cấu trúc vật lý Warehouse → Khu vực → Dãy kệ → Kệ → Tầng → Ô/Vị trí. Capacity và storage constraints được quản lý ở capability riêng."
        />

        {error && <p role="alert">{error}</p>}
        {success && <p role="status" className="ui-success-text">{success}</p>}

        <UiToolbar>
          <UiToolbarField label="Kho">
            <select aria-label="Kho cấu trúc vị trí" value={warehouseId} onChange={event => selectWarehouse(event.target.value)} disabled={loading}>
              <option value="">-- Chọn kho --</option>
              {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
            </select>
          </UiToolbarField>
          <div className="ui-auto-actions ui-muted-text">
            Mã cấu trúc và mã vị trí không đổi sau khi tạo.
          </div>
        </UiToolbar>

        {structure && (
          <UiMetricGrid>
            <UiMetric value={zoneCount} label="Khu vực" />
            <UiMetric value={aisleCount} label="Dãy kệ" />
            <UiMetric value={rackCount} label="Kệ" />
            <UiMetric value={levelCount} label="Tầng" />
            <UiMetric value={locationCount} label="Ô / Vị trí" />
          </UiMetricGrid>
        )}

        {canManageStructure && warehouseId && (
          <UiCard title={editNode ? 'Cập nhật cấu trúc' : 'Thêm cấu trúc vật lý'}>
            {editNode ? (
              <form className="warehouse-structure-form" onSubmit={submitNodeEdit}>
                <div className="warehouse-structure-form-grid">
                  <label className="ui-stack">
                    <span>Mã</span>
                    <input value={editNode.kind === 'zone' ? editNode.zone.code : editNode.kind === 'aisle' ? editNode.aisle.code : editNode.rack.code} disabled />
                  </label>
                  <label className="ui-stack">
                    <span>Tên</span>
                    <input value={nodeName} onChange={event => setNodeName(event.target.value)} />
                  </label>
                  {editNode.kind === 'zone' && (
                    <>
                      <label className="ui-stack">
                        <span>Loại khu vực</span>
                        <select value={zoneType} onChange={event => setZoneType(event.target.value)}>
                          {Object.entries(zoneLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                        </select>
                      </label>
                      <label className="ui-stack">
                        <span>Ưu tiên lấy hàng</span>
                        <input type="number" value={pickPriority} onChange={event => setPickPriority(event.target.value)} />
                      </label>
                      <label className="ui-stack">
                        <span>Ưu tiên cất hàng</span>
                        <input type="number" value={putawayPriority} onChange={event => setPutawayPriority(event.target.value)} />
                      </label>
                      <label className="ui-checkbox-label">
                        <input type="checkbox" checked={locationActive} onChange={event => setLocationActive(event.target.checked)} />
                        Khu vực đang hoạt động
                      </label>
                    </>
                  )}
                </div>
                <div className="warehouse-structure-actions">
                  <button type="button" onClick={() => { setEditNode(null); resetStructureForm(); }}>Hủy sửa</button>
                  <button type="submit" disabled={nodeSaving}>{nodeSaving ? 'Đang lưu...' : 'Lưu thay đổi'}</button>
                </div>
              </form>
            ) : (
              <form className="warehouse-structure-form" onSubmit={submitStructure}>
                <div className="warehouse-structure-form-grid">
                  <label className="ui-stack">
                    <span>Cấp cấu trúc</span>
                    <select value={structureKind} onChange={event => { setStructureKind(event.target.value as StructureKind); resetStructureForm(); }}>
                      <option value="zone">Khu vực</option>
                      <option value="aisle">Dãy kệ</option>
                      <option value="rack">Kệ</option>
                      <option value="level">Tầng</option>
                    </select>
                  </label>

                  {structureKind !== 'zone' && (
                    <label className="ui-stack">
                      <span>Cấp cha</span>
                      <select required value={parentId} onChange={event => setParentId(event.target.value ? Number(event.target.value) : '')}>
                        <option value="">-- Chọn cấp cha --</option>
                        {structureKind === 'aisle' && structure?.zones.filter(zone => zone.isActive).map(zone => <option key={zone.id} value={zone.id}>{zone.code} — {zone.name}</option>)}
                        {structureKind === 'rack' && aisles.map(aisle => <option key={aisle.id} value={aisle.id}>{aisle.zoneCode} / {aisle.code}</option>)}
                        {structureKind === 'level' && racks.map(rack => <option key={rack.id} value={rack.id}>{rack.zoneCode} / {rack.aisleCode} / {rack.code}</option>)}
                      </select>
                    </label>
                  )}

                  {structureKind !== 'level' ? (
                    <>
                      <label className="ui-stack">
                        <span>Mã</span>
                        <input required value={nodeCode} onChange={event => setNodeCode(event.target.value.toUpperCase())} placeholder="Ví dụ: ZONE-A" />
                      </label>
                      <label className="ui-stack">
                        <span>Tên</span>
                        <input required={structureKind === 'zone'} value={nodeName} onChange={event => setNodeName(event.target.value)} />
                      </label>
                    </>
                  ) : (
                    <label className="ui-stack">
                      <span>Số tầng</span>
                      <input required type="number" min="1" value={levelNo} onChange={event => setLevelNo(event.target.value)} />
                    </label>
                  )}

                  {structureKind === 'zone' && (
                    <>
                      <label className="ui-stack">
                        <span>Loại khu vực</span>
                        <select value={zoneType} onChange={event => setZoneType(event.target.value)}>
                          {Object.entries(zoneLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                        </select>
                      </label>
                      <label className="ui-stack">
                        <span>Ưu tiên lấy hàng</span>
                        <input type="number" value={pickPriority} onChange={event => setPickPriority(event.target.value)} />
                      </label>
                      <label className="ui-stack">
                        <span>Ưu tiên cất hàng</span>
                        <input type="number" value={putawayPriority} onChange={event => setPutawayPriority(event.target.value)} />
                      </label>
                    </>
                  )}
                </div>
                <div className="warehouse-structure-actions">
                  <button type="submit" disabled={nodeSaving}>{nodeSaving ? 'Đang tạo...' : 'Tạo cấu trúc'}</button>
                </div>
              </form>
            )}
          </UiCard>
        )}

        {canManageLocations && warehouseId && (
          <UiCard title={editingLocation ? 'Cập nhật ô / vị trí' : 'Thêm ô / vị trí'}>
            <form className="warehouse-structure-form" onSubmit={submitLocation}>
              <div className="warehouse-structure-form-grid">
                <label className="ui-stack">
                  <span>Mã vị trí</span>
                  <input required value={locationCode} disabled={!!editingLocation} onChange={event => setLocationCode(event.target.value.toUpperCase())} placeholder="A01-R02-L03-B04" />
                </label>
                <label className="ui-stack">
                  <span>Tên vị trí</span>
                  <input required value={locationName} onChange={event => setLocationName(event.target.value)} />
                </label>
                <label className="ui-stack">
                  <span>Khu vực</span>
                  <select required value={locationZoneId} disabled={!!editingLocation?.zoneId} onChange={event => { setLocationZoneId(event.target.value ? Number(event.target.value) : ''); setLocationLevelId(''); }}>
                    <option value="">-- Chọn khu vực --</option>
                    {structure?.zones.filter(zone => zone.isActive || zone.id === locationZoneId).map(zone => <option key={zone.id} value={zone.id}>{zone.code} — {zone.name}</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Tầng kệ (không bắt buộc)</span>
                  <select value={locationLevelId} disabled={!!editingLocation?.rackLevelId || !locationZoneId} onChange={event => setLocationLevelId(event.target.value ? Number(event.target.value) : '')}>
                    <option value="">Vị trí trực tiếp trong khu vực</option>
                    {selectedZoneLevels.map(level => <option key={level.id} value={level.id}>{level.aisleCode} / {level.rackCode} / Tầng {level.levelNo}</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Loại vị trí</span>
                  <select value={locationType} disabled={!!editingLocation} onChange={event => { const next = event.target.value; setLocationType(next); setLocationPickable(next === 'Storage'); }}>
                    <option value="Storage">Lưu trữ</option>
                    <option value="Damaged">Hư hỏng</option>
                    <option value="Rejected">Hàng bị từ chối</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Mã vạch</span>
                  <input value={locationBarcode} onChange={event => setLocationBarcode(event.target.value)} placeholder="Quét hoặc nhập mã vị trí" />
                </label>
                <label className="ui-stack">
                  <span>Ưu tiên lấy hàng</span>
                  <input type="number" value={locationPickPriority} onChange={event => setLocationPickPriority(event.target.value)} />
                </label>
                <label className="ui-stack">
                  <span>Ưu tiên cất hàng</span>
                  <input type="number" value={locationPutawayPriority} onChange={event => setLocationPutawayPriority(event.target.value)} />
                </label>
                <label className="ui-checkbox-label">
                  <input type="checkbox" checked={locationPickable} disabled={locationType !== 'Storage'} onChange={event => setLocationPickable(event.target.checked)} />
                  Cho phép lấy hàng
                </label>
                {editingLocation && (
                  <>
                    <label className="ui-checkbox-label">
                      <input type="checkbox" checked={locationActive} onChange={event => setLocationActive(event.target.checked)} />
                      Đang hoạt động
                    </label>
                    <label className="ui-checkbox-label">
                      <input type="checkbox" checked={locationBlocked} onChange={event => setLocationBlocked(event.target.checked)} />
                      Tạm khóa vị trí
                    </label>
                  </>
                )}
              </div>
              <div className="warehouse-structure-actions">
                {editingLocation && <button type="button" onClick={resetLocationForm}>Hủy sửa</button>}
                <button type="submit" disabled={locationSaving}>{locationSaving ? 'Đang lưu...' : editingLocation ? 'Lưu vị trí' : 'Tạo vị trí'}</button>
              </div>
            </form>
          </UiCard>
        )}

        <UiCard title="Cây cấu trúc vật lý">
          {structureLoading || loading ? (
            <p role="status">Đang tải cấu trúc vị trí...</p>
          ) : !structure ? (
            <UiEmptyState title="Chọn kho để xem cấu trúc vị trí." />
          ) : structure.zones.length === 0 ? (
            <UiEmptyState title="Kho chưa có khu vực vật lý." detail={canManageStructure ? 'Tạo khu vực đầu tiên để bắt đầu cấu hình Zone / Aisle / Rack / Level / Bin.' : undefined} />
          ) : (
            <UiTableScroll>
              <table aria-label="Cấu trúc vị trí kho">
                <thead>
                  <tr>
                    <th>Cấp</th>
                    <th>Mã / Tên</th>
                    <th>Đường dẫn</th>
                    <th>Loại</th>
                    <th>Trạng thái</th>
                    {(canManageStructure || canManageLocations) && <th>Hành động</th>}
                  </tr>
                </thead>
                <tbody>
                  {structure.zones.flatMap(zone => {
                    const rows: ReactNode[] = [
                      <tr key={'zone-' + zone.id}>
                        <td><UiBadge>Khu vực</UiBadge></td>
                        <td><strong>{zone.code}</strong><br /><small>{zone.name}</small></td>
                        <td>{zone.code}</td>
                        <td>{zoneLabels[zone.zoneType] || zone.zoneType}</td>
                        <td><UiBadge tone={zone.isActive ? 'success' : 'neutral'}>{zone.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                        {(canManageStructure || canManageLocations) && <td>{canManageStructure && <button type="button" onClick={() => beginNodeEdit({ kind: 'zone', zone })}>Sửa khu vực</button>}</td>}
                      </tr>,
                    ];
                    zone.locations.forEach(location => rows.push(
                      <tr key={'location-' + location.id}>
                        <td><span className="warehouse-structure-depth depth-1">Ô / Vị trí</span></td>
                        <td><strong>{location.code}</strong><br /><small>{location.name}</small></td>
                        <td>{zone.code} / {location.code}</td>
                        <td>{locationLabels[location.locationType] || location.locationType}</td>
                        <td><UiBadge tone={!location.isActive || location.isBlocked ? 'danger' : 'success'}>{!location.isActive ? 'Ngừng hoạt động' : location.isBlocked ? 'Đang khóa' : 'Có thể sử dụng'}</UiBadge></td>
                        {(canManageStructure || canManageLocations) && <td>{canManageLocations && !location.isSystemManaged && <button type="button" onClick={() => beginLocationEdit(location)}>Sửa vị trí</button>}</td>}
                      </tr>
                    ));
                    zone.aisles.forEach(aisle => {
                      rows.push(
                        <tr key={'aisle-' + aisle.id}>
                          <td><span className="warehouse-structure-depth depth-1">Dãy kệ</span></td>
                          <td><strong>{aisle.code}</strong><br /><small>{aisle.name || '—'}</small></td>
                          <td>{zone.code} / {aisle.code}</td>
                          <td>—</td>
                          <td>—</td>
                          {(canManageStructure || canManageLocations) && <td>{canManageStructure && <button type="button" onClick={() => beginNodeEdit({ kind: 'aisle', zoneId: zone.id, aisle })}>Sửa dãy</button>}</td>}
                        </tr>
                      );
                      aisle.racks.forEach(rack => {
                        rows.push(
                          <tr key={'rack-' + rack.id}>
                            <td><span className="warehouse-structure-depth depth-2">Kệ</span></td>
                            <td><strong>{rack.code}</strong><br /><small>{rack.name || '—'}</small></td>
                            <td>{zone.code} / {aisle.code} / {rack.code}</td>
                            <td>—</td>
                            <td>—</td>
                            {(canManageStructure || canManageLocations) && <td>{canManageStructure && <button type="button" onClick={() => beginNodeEdit({ kind: 'rack', aisleId: aisle.id, rack })}>Sửa kệ</button>}</td>}
                          </tr>
                        );
                        rack.levels.forEach(level => {
                          rows.push(
                            <tr key={'level-' + level.id}>
                              <td><span className="warehouse-structure-depth depth-3">Tầng</span></td>
                              <td><strong>Tầng {level.levelNo}</strong></td>
                              <td>{zone.code} / {aisle.code} / {rack.code} / Tầng {level.levelNo}</td>
                              <td>—</td>
                              <td>—</td>
                              {(canManageStructure || canManageLocations) && <td>—</td>}
                            </tr>
                          );
                          level.locations.forEach(location => rows.push(
                            <tr key={'location-' + location.id}>
                              <td><span className="warehouse-structure-depth depth-4">Ô / Vị trí</span></td>
                              <td><strong>{location.code}</strong><br /><small>{location.name}</small></td>
                              <td>{zone.code} / {aisle.code} / {rack.code} / Tầng {level.levelNo} / {location.code}</td>
                              <td>{locationLabels[location.locationType] || location.locationType}</td>
                              <td><UiBadge tone={!location.isActive || location.isBlocked ? 'danger' : 'success'}>{!location.isActive ? 'Ngừng hoạt động' : location.isBlocked ? 'Đang khóa' : 'Có thể sử dụng'}</UiBadge></td>
                              {(canManageStructure || canManageLocations) && <td>{canManageLocations && !location.isSystemManaged && <button type="button" onClick={() => beginLocationEdit(location)}>Sửa vị trí</button>}</td>}
                            </tr>
                          ));
                        });
                      });
                    });
                    return rows;
                  })}
                </tbody>
              </table>
            </UiTableScroll>
          )}
        </UiCard>

        {structure && structure.unmappedLocations.length > 0 && (
          <UiCard title="Vị trí chưa gắn cấu trúc">
            <p className="ui-muted-text">
              Đây là dữ liệu vị trí có từ trước WH-02. Hãy gắn vào một khu vực/tầng kệ phù hợp; hệ thống không tự tạo cấu trúc giả cho lịch sử cũ.
            </p>
            <UiTableScroll>
              <table aria-label="Vị trí chưa gắn cấu trúc">
                <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Trạng thái</th>{canManageLocations && <th>Hành động</th>}</tr></thead>
                <tbody>
                  {structure.unmappedLocations.map(location => (
                    <tr key={location.id}>
                      <td><strong>{location.code}</strong></td>
                      <td>{location.name}</td>
                      <td>{locationLabels[location.locationType] || location.locationType}</td>
                      <td><UiBadge tone={!location.isActive || location.isBlocked ? 'danger' : 'warning'}>{!location.isActive ? 'Ngừng hoạt động' : location.isBlocked ? 'Đang khóa' : 'Chưa gắn cấu trúc'}</UiBadge></td>
                      {canManageLocations && <td><button type="button" onClick={() => beginLocationEdit(location)}>Gắn cấu trúc</button></td>}
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>
        )}

        {structure && structure.systemLocations.length > 0 && (
          <UiCard title="Vị trí hệ thống">
            <p className="ui-muted-text">RECEIVING và LEGACY được hệ thống quản lý để giữ tương thích nghiệp vụ và lịch sử tồn kho.</p>
            <UiTableScroll>
              <table aria-label="Vị trí hệ thống">
                <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Trạng thái</th></tr></thead>
                <tbody>
                  {structure.systemLocations.map(location => (
                    <tr key={location.id}>
                      <td><strong>{location.code}</strong></td>
                      <td>{location.name}</td>
                      <td>{locationLabels[location.locationType] || location.locationType}</td>
                      <td><UiBadge tone={location.isActive ? 'success' : 'neutral'}>{location.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>
        )}
      </div>
    </UiPage>
  );
};

export default WarehouseStructure;
