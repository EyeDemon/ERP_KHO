import { useMemo, useState } from 'react';
import { demoWarehouseStructures, demoWarehouses } from '../mocks/demoApiData';
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

type DemoLocation = {
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
};
type DemoLevel = { id: number; rackId: number; levelNo: number; locations: DemoLocation[] };
type DemoRack = { id: number; aisleId: number; code: string; name?: string | null; levels: DemoLevel[] };
type DemoAisle = { id: number; zoneId: number; code: string; name?: string | null; racks: DemoRack[] };
type DemoZone = { id: number; warehouseId: number; code: string; name: string; zoneType: string; isActive: boolean; aisles: DemoAisle[]; locations: DemoLocation[] };
type DemoStructure = { warehouseId: number; warehouseCode: string; warehouseName: string; zones: DemoZone[]; systemLocations: DemoLocation[]; unmappedLocations: DemoLocation[] };
type HierarchyRow = { key: string; depth: number; level: string; code: string; name: string; path: string; location?: DemoLocation };

const locationLabels: Record<string, string> = {
  Receiving: 'Nhận hàng',
  Storage: 'Lưu trữ',
  Damaged: 'Hư hỏng',
  Rejected: 'Hàng bị từ chối',
  Legacy: 'Tương thích hệ thống',
};

const locationStatus = (location: DemoLocation) => {
  if (!location.isActive) return { label: 'Ngừng hoạt động', tone: 'neutral' as const };
  if (location.isBlocked) return { label: 'Bị khóa', tone: 'danger' as const };
  return { label: 'Đang hoạt động', tone: 'success' as const };
};

const availabilityLabel = (location: DemoLocation) => {
  if (!location.isActive || location.isBlocked) return 'Không khả dụng';
  return location.isPickable ? 'Có thể pick' : 'Không pick';
};

const WarehouseStructure = () => {
  const [warehouseId, setWarehouseId] = useState<number>(demoWarehouses[0]?.id ?? 0);
  const [statusFilter, setStatusFilter] = useState('all');
  const [typeFilter, setTypeFilter] = useState('all');
  const [selectedLocationCode, setSelectedLocationCode] = useState<string | null>(null);

  const structure = useMemo(
    () => demoWarehouseStructures.find((item) => item.warehouseId === warehouseId) as DemoStructure | undefined,
    [warehouseId],
  );

  const hierarchyRows = useMemo(() => {
    const rows: HierarchyRow[] = [];
    if (!structure) return rows;

    structure.zones.forEach((zone) => {
      rows.push({ key: 'zone-' + zone.id, depth: 0, level: 'Khu vực', code: zone.code, name: zone.name, path: zone.code });

      zone.locations.forEach((location) => {
        rows.push({
          key: 'zone-location-' + location.id,
          depth: 1,
          level: 'Ô / Vị trí',
          code: location.code,
          name: location.name,
          path: zone.code + ' / ' + location.code,
          location,
        });
      });

      zone.aisles.forEach((aisle) => {
        const aislePath = zone.code + ' / ' + aisle.code;
        rows.push({ key: 'aisle-' + aisle.id, depth: 1, level: 'Dãy kệ', code: aisle.code, name: aisle.name || '—', path: aislePath });

        aisle.racks.forEach((rack) => {
          const rackPath = aislePath + ' / ' + rack.code;
          rows.push({ key: 'rack-' + rack.id, depth: 2, level: 'Kệ', code: rack.code, name: rack.name || '—', path: rackPath });

          rack.levels.forEach((level) => {
            const levelCode = 'L' + String(level.levelNo).padStart(2, '0');
            const levelPath = rackPath + ' / ' + levelCode;
            rows.push({ key: 'level-' + level.id, depth: 3, level: 'Tầng', code: levelCode, name: 'Tầng ' + level.levelNo, path: levelPath });

            level.locations.forEach((location) => {
              const leafCode = location.code.split('-').slice(-1)[0];
              rows.push({
                key: 'location-' + location.id,
                depth: 4,
                level: 'Ô / Vị trí',
                code: location.code,
                name: location.name,
                path: levelPath + ' / ' + leafCode,
                location,
              });
            });
          });
        });
      });
    });

    return rows;
  }, [structure]);

  const physicalLocations = useMemo(
    () => hierarchyRows.filter((row) => row.location).map((row) => ({ row, location: row.location as DemoLocation })),
    [hierarchyRows],
  );

  const allLocations = useMemo(
    () => [
      ...physicalLocations.map((item) => item.location),
      ...(structure?.systemLocations ?? []),
      ...(structure?.unmappedLocations ?? []),
    ],
    [physicalLocations, structure],
  );

  const filteredLocations = useMemo(
    () => physicalLocations.filter(({ location }) => {
      const statusMatches =
        statusFilter === 'all'
        || (statusFilter === 'active' && location.isActive && !location.isBlocked)
        || (statusFilter === 'blocked' && location.isBlocked)
        || (statusFilter === 'inactive' && !location.isActive);
      const typeMatches = typeFilter === 'all' || location.locationType === typeFilter;
      return statusMatches && typeMatches;
    }),
    [physicalLocations, statusFilter, typeFilter],
  );

  const selectedLocation = allLocations.find((location) => location.code === selectedLocationCode) ?? null;
  const zoneCount = structure?.zones.length ?? 0;
  const aisleCount = structure?.zones.reduce((count, zone) => count + zone.aisles.length, 0) ?? 0;
  const rackCount = structure?.zones.reduce(
    (count, zone) => count + zone.aisles.reduce((aisleTotal, aisle) => aisleTotal + aisle.racks.length, 0),
    0,
  ) ?? 0;
  const locationCount = allLocations.length;
  const blockedCount = allLocations.filter((location) => location.isBlocked).length;
  const inactiveCount = allLocations.filter((location) => !location.isActive).length;

  return (
    <div className="production-ui warehouse-structure-surface">
      <UiPage>
        <div className="warehouse-structure-page">
        <UiPageHeader
          eyebrow="Kho & Vị trí"
          title="Cấu trúc vị trí kho"
          description="Mock tương tác Warehouse → Zone → Aisle → Rack → Level → Bin / Location để kiểm tra IA, trạng thái và quy tắc WH-02 trước khi triển khai production."
        />

        <UiCard title="Phạm vi Blueprint WH-02">
          <p className="ui-muted-text">
            Đây là frontend mock-only. Production backend, database, migration và API mutation cho WH-02 chưa được triển khai trong branch Blueprint.
          </p>
          <p className="ui-muted-text">
            Mã vị trí được minh họa là ổn định; vị trí ngừng hoạt động hoặc bị khóa vẫn giữ để truy vết lịch sử. Capacity/weight/volume thuộc WH-03.
          </p>
        </UiCard>

        <UiToolbar>
          <UiToolbarField label="Kho">
            <select
              aria-label="Kho cấu trúc vị trí"
              value={warehouseId}
              onChange={(event) => {
                setWarehouseId(Number(event.target.value));
                setSelectedLocationCode(null);
              }}
            >
              {demoWarehouses.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>
                  {warehouse.code} — {warehouse.name}
                </option>
              ))}
            </select>
          </UiToolbarField>

          <UiToolbarField label="Trạng thái vị trí">
            <select aria-label="Trạng thái vị trí" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="all">Tất cả</option>
              <option value="active">Đang hoạt động</option>
              <option value="blocked">Bị khóa</option>
              <option value="inactive">Ngừng hoạt động</option>
            </select>
          </UiToolbarField>

          <UiToolbarField label="Loại vị trí">
            <select aria-label="Loại vị trí" value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}>
              <option value="all">Tất cả</option>
              <option value="Storage">Lưu trữ</option>
              <option value="Damaged">Hư hỏng</option>
              <option value="Rejected">Hàng bị từ chối</option>
            </select>
          </UiToolbarField>
        </UiToolbar>

        {!structure ? (
          <UiEmptyState title="Không có dữ liệu mock cho kho đã chọn." />
        ) : (
          <>
            <UiMetricGrid>
              <UiMetric value={zoneCount} label="Khu vực" />
              <UiMetric value={aisleCount} label="Dãy kệ" />
              <UiMetric value={rackCount} label="Kệ" />
              <UiMetric value={locationCount} label="Tổng vị trí" />
              <UiMetric value={blockedCount} label="Bị khóa" />
              <UiMetric value={inactiveCount} label="Ngừng hoạt động" />
            </UiMetricGrid>

            <UiCard title="Cây cấu trúc vật lý">
              <UiTableScroll>
                <table aria-label="Cấu trúc vị trí kho">
                  <thead>
                    <tr>
                      <th>Cấp</th>
                      <th>Mã / Tên</th>
                      <th>Đường dẫn đầy đủ</th>
                      <th>Loại</th>
                      <th>Trạng thái</th>
                      <th>Khả dụng</th>
                      <th>Chi tiết</th>
                    </tr>
                  </thead>
                  <tbody>
                    {hierarchyRows.map((row) => {
                      const status = row.location ? locationStatus(row.location) : null;
                      return (
                        <tr key={row.key}>
                          <td><span className={'warehouse-structure-depth depth-' + row.depth}>{row.level}</span></td>
                          <td><strong>{row.code}</strong><br /><small>{row.name}</small></td>
                          <td>{row.path}</td>
                          <td>{row.location ? (locationLabels[row.location.locationType] || row.location.locationType) : '—'}</td>
                          <td>{status ? <UiBadge tone={status.tone}>{status.label}</UiBadge> : '—'}</td>
                          <td>{row.location ? availabilityLabel(row.location) : '—'}</td>
                          <td>
                            {row.location ? (
                              <button type="button" onClick={() => setSelectedLocationCode(row.location?.code ?? null)}>Xem</button>
                            ) : '—'}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </UiTableScroll>
            </UiCard>

            <UiCard title="Vị trí vật lý theo bộ lọc">
              {filteredLocations.length === 0 ? (
                <UiEmptyState title="Không có vị trí phù hợp bộ lọc." />
              ) : (
                <UiTableScroll>
                  <table aria-label="Vị trí vật lý theo bộ lọc">
                    <thead><tr><th>Mã</th><th>Đường dẫn</th><th>Loại</th><th>Trạng thái</th><th>Pick</th></tr></thead>
                    <tbody>
                      {filteredLocations.map(({ row, location }) => {
                        const status = locationStatus(location);
                        return (
                          <tr key={'filtered-' + location.id}>
                            <td><strong>{location.code}</strong><br /><small>{location.name}</small></td>
                            <td>{row.path}</td>
                            <td>{locationLabels[location.locationType] || location.locationType}</td>
                            <td><UiBadge tone={status.tone}>{status.label}</UiBadge></td>
                            <td>{availabilityLabel(location)}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </UiTableScroll>
              )}
            </UiCard>

            {selectedLocation && (
              <UiCard title={'Chi tiết mock • ' + selectedLocation.code}>
                <div className="warehouse-structure-form-grid">
                  <div><strong>Loại</strong><br />{locationLabels[selectedLocation.locationType] || selectedLocation.locationType}</div>
                  <div><strong>Trạng thái</strong><br />{locationStatus(selectedLocation).label}</div>
                  <div><strong>Pick</strong><br />{availabilityLabel(selectedLocation)}</div>
                  <div><strong>System managed</strong><br />{selectedLocation.isSystemManaged ? 'Có' : 'Không'}</div>
                </div>
              </UiCard>
            )}

            {structure.unmappedLocations.length > 0 && (
              <UiCard title="Vị trí chưa gắn cấu trúc">
                <p className="ui-muted-text">Dữ liệu legacy/non-system được giữ nguyên, không tự bịa Zone/Rack lịch sử.</p>
                <UiTableScroll>
                  <table aria-label="Vị trí chưa gắn cấu trúc">
                    <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Trạng thái</th></tr></thead>
                    <tbody>
                      {structure.unmappedLocations.map((location) => {
                        const status = locationStatus(location);
                        return (
                          <tr key={location.id}>
                            <td><strong>{location.code}</strong></td>
                            <td>{location.name}</td>
                            <td>{locationLabels[location.locationType] || location.locationType}</td>
                            <td><UiBadge tone={status.tone}>{status.label}</UiBadge></td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </UiTableScroll>
              </UiCard>
            )}

            <UiCard title="Vị trí hệ thống">
              <p className="ui-muted-text">
                RECEIVING và LEGACY là ví dụ system-managed để minh họa compatibility; đây không phải bằng chứng production seed/API.
              </p>
              <UiTableScroll>
                <table aria-label="Vị trí hệ thống">
                  <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Receivable</th><th>Pick</th></tr></thead>
                  <tbody>
                    {structure.systemLocations.map((location) => (
                      <tr key={location.id}>
                        <td><strong>{location.code}</strong></td>
                        <td>{location.name}</td>
                        <td>{locationLabels[location.locationType] || location.locationType}</td>
                        <td>{location.isReceivable ? 'Có' : 'Không'}</td>
                        <td>{location.isPickable ? 'Có thể pick' : 'Không pick'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          </>
        )}
        </div>
      </UiPage>
    </div>
  );
};

export default WarehouseStructure;
