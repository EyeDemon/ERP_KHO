import React from 'react';
import { UiBadge, UiCard, UiDrawer, UiEmptyState, UiTableScroll } from '../../ui/ProductionUi';
import {
  capacityLabel,
  capacityTone,
  capacityUsage,
  type LocationCapacity,
  type LocationForm,
  locationStatus,
  locationTypeLabels,
  splitStructurePath,
  type WarehouseLocation,
} from './WarehouseLocationTypes';

export { WarehouseLocationGovernanceCard } from './WarehouseLocationGovernanceCard';

export function WarehouseLocationCapacityAndSystemSection({
  activeViewTab,
  capacities,
  filtered,
  systemLocations,
  canManage,
  openEdit,
}: {
  activeViewTab: 'hierarchy' | 'capacity' | 'system';
  capacities: LocationCapacity[];
  filtered: WarehouseLocation[];
  systemLocations: WarehouseLocation[];
  canManage: boolean;
  openEdit: (location: WarehouseLocation) => void;
}) {
  if (activeViewTab === 'capacity') {
    return (
      <>
        <UiCard title="Sức chứa, tải trọng & kích thước 3 chiều vị trí kệ">
          {capacities.length === 0 ? (
            <UiEmptyState title="Chưa có vị trí vật lý để theo dõi sức chứa." />
          ) : (
            <div tabIndex={0} role="region" aria-label="Vùng cuộn sức chứa vị trí kho" style={{ maxHeight: 240, overflowY: 'auto' }}>
              <UiTableScroll>
                <table aria-label="Sức chứa vị trí kho thật" className="ui-table-compact">
                  <thead>
                    <tr>
                      <th style={{ minWidth: 110 }}>Vị trí</th>
                      <th style={{ minWidth: 125 }}>Phân lớp lưu trữ</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 110, whiteSpace: 'nowrap' }}>Trọng lượng</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 105, whiteSpace: 'nowrap' }}>Thể tích</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 115, whiteSpace: 'nowrap' }}>Quy đổi Pallet</th>
                      <th style={{ minWidth: 105, whiteSpace: 'nowrap' }}>Trạng thái</th>
                    </tr>
                  </thead>
                  <tbody>
                    {capacities.map(item => (
                      <tr key={item.locationId}>
                        <td><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.code}</strong><br /><small>{item.structurePath?.split('/').join(' / ') || item.name}</small></td>
                        <td>{item.storageClass === 'AMBIENT' ? 'Nhiệt độ thường' : item.storageClass || 'Không giới hạn phân lớp'}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{capacityUsage(item.usedWeightKg, item.maxWeightKg, 'kg')}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{capacityUsage(item.usedVolumeM3, item.maxVolumeM3, 'm³')}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{capacityUsage(item.usedPalletEquivalent, item.maxPalletEquivalent)}</td>
                        <td><UiBadge tone={capacityTone(item.state)}>{capacityLabel[item.state] || item.state}</UiBadge></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>
            </div>
          )}
        </UiCard>

        <UiCard title="Danh sách vị trí theo bộ lọc">
          <div tabIndex={0} role="region" aria-label="Vùng cuộn danh sách vị trí theo bộ lọc" style={{ maxHeight: 240, overflowY: 'auto' }}>
            <UiTableScroll>
              <table aria-label="Danh sách vị trí kho thật" className="ui-table-compact">
                <thead>
                  <tr>
                    <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Mã vị trí</th>
                    <th style={{ minWidth: 120 }}>Tên vị trí</th>
                    <th style={{ minWidth: 150 }}>Đường dẫn cấu trúc</th>
                    <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Phân loại</th>
                    <th style={{ minWidth: 105, whiteSpace: 'nowrap' }}>Trạng thái</th>
                    <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Lấy hàng</th>
                    {canManage && <th style={{ minWidth: 84, whiteSpace: 'nowrap' }}>Thao tác</th>}
                  </tr>
                </thead>
                <tbody>
                  {filtered.map(location => {
                    const status = locationStatus(location);
                    return (
                      <tr key={location.id}>
                        <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{location.code}</strong>{location.isSystemManaged && <><br /><small>Hệ thống quản lý</small></>}</td>
                        <td>{location.name.replace(/\s*\(RMA\)/gi, '')}</td>
                        <td>{splitStructurePath(location.structurePath)?.join(' / ') || '—'}</td>
                        <td>{locationTypeLabels[location.locationType] || location.locationType}</td>
                        <td><UiBadge tone={status.tone}>{status.label}</UiBadge></td>
                        <td>{location.isPickable ? 'Có' : 'Không'}</td>
                        {canManage && <td style={{ whiteSpace: 'nowrap' }}>{!location.isSystemManaged ? <button type="button" onClick={() => openEdit(location)} style={{ whiteSpace: 'nowrap' }}>Sửa</button> : '—'}</td>}
                      </tr>
                    );
                  })}
                  {filtered.length === 0 && <tr><td className="ui-empty-cell" colSpan={canManage ? 7 : 6}>Không có vị trí phù hợp bộ lọc.</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        </UiCard>
      </>
    );
  }

  if (activeViewTab === 'system') {
    return (
      <UiCard title="Vị trí hệ thống">
        {systemLocations.length === 0 ? (
          <UiEmptyState title="Kho chưa có vị trí hệ thống." />
        ) : (
          <div tabIndex={0} role="region" aria-label="Vùng cuộn danh sách vị trí hệ thống" style={{ maxHeight: 260, overflowY: 'auto' }}>
            <UiTableScroll>
              <table aria-label="Vị trí hệ thống thật" className="ui-table-compact">
                <thead>
                  <tr>
                    <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Mã vị trí</th>
                    <th style={{ minWidth: 145 }}>Tên vị trí</th>
                    <th style={{ minWidth: 115 }}>Phân loại</th>
                    <th style={{ minWidth: 92, whiteSpace: 'nowrap' }}>Nhận hàng</th>
                    <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Lấy hàng</th>
                  </tr>
                </thead>
                <tbody>
                  {systemLocations.map(location => (
                    <tr key={location.id}>
                      <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{location.code}</strong></td>
                      <td>{location.name.replace(/\s*\(RMA\)/gi, '')}</td>
                      <td>{locationTypeLabels[location.locationType] || location.locationType}</td>
                      <td>{location.isReceivable ? 'Có' : 'Không'}</td>
                      <td>{location.isPickable ? 'Có' : 'Không'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        )}
      </UiCard>
    );
  }

  return null;
}

export function WarehouseLocationFormDrawer({
  open,
  editing,
  hasLockedStructure,
  saving,
  form,
  setForm,
  formError,
  closeForm,
  save,
}: {
  open: boolean;
  editing: boolean;
  hasLockedStructure: boolean;
  saving: boolean;
  form: LocationForm;
  setForm: React.Dispatch<React.SetStateAction<LocationForm>>;
  formError: string;
  closeForm: () => void;
  save: (event?: React.FormEvent) => Promise<void>;
}) {
  return (
    <UiDrawer
      open={open}
      title={editing ? 'Sửa vị trí ' + form.code : 'Thêm vị trí mới'}
      busy={saving}
      onClose={closeForm}
      footer={
        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', width: '100%' }}>
          <button type="button" className="ui-btn ui-btn-secondary" disabled={saving} onClick={closeForm}>Hủy</button>
          <button type="button" className="ui-btn ui-btn-primary ui-primary-button" disabled={saving} onClick={e => void save(e)}>{saving ? 'Đang lưu...' : 'Lưu vị trí'}</button>
        </div>
      }
    >
      <form id="warehouse-location-form" className="warehouse-location-form" onSubmit={save}>
        {formError && <p role="alert">{formError}</p>}

        <div className="warehouse-location-form-grid">
          <label className="ui-stack"><span>Mã vị trí *</span><input aria-label="Mã vị trí" value={form.code} onChange={event => setForm(current => ({ ...current, code: event.target.value }))} disabled={saving || editing} maxLength={64} placeholder="VD: A01-R02-L03-B04" /></label>
          <label className="ui-stack"><span>Loại vị trí *</span><select aria-label="Loại vị trí form" value={form.locationType} onChange={event => setForm(current => ({ ...current, locationType: event.target.value as LocationForm['locationType'] }))} disabled={saving || editing}><option value="Storage">Lưu trữ</option><option value="Damaged">Hư hỏng</option><option value="Rejected">Hàng bị từ chối</option><option value="Staging">Khu tập kết xuất hàng</option></select></label>
          <label className="ui-stack" style={{ gridColumn: '1 / -1' }}><span>Tên vị trí *</span><input aria-label="Tên vị trí" value={form.name} onChange={event => setForm(current => ({ ...current, name: event.target.value }))} disabled={saving} maxLength={200} placeholder="Nhập tên vị trí" /></label>
        </div>

        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Phân lớp Lưu trữ & Sức chứa</div>
          <p className="ui-muted-text">Để trống nghĩa là không áp giới hạn theo chiều đó. Nếu đã có tồn kho, hệ thống chỉ cho lưu ràng buộc khi tải hiện tại vẫn hợp lệ.</p>
          <div className="warehouse-location-capacity-grid">
            <label className="ui-stack">
              <span>Phân lớp lưu trữ</span>
              <select
                aria-label="Storage Class vị trí"
                disabled={saving}
                value={form.storageClass.toUpperCase()}
                onChange={event => setForm(current => ({ ...current, storageClass: event.target.value.toUpperCase() }))}
              >
                <option value="">Không giới hạn phân lớp</option>
                <option value="AMBIENT">Nhiệt độ thường (AMBIENT)</option>
                <option value="CHILLED">Kho mát (CHILLED)</option>
                <option value="FROZEN">Kho đông (FROZEN)</option>
                <option value="HAZMAT">Hàng nguy hiểm (HAZMAT)</option>
                <option value="ambient" hidden>Nhiệt độ thường (AMBIENT)</option>
                {form.storageClass && !['AMBIENT', 'CHILLED', 'FROZEN', 'HAZMAT', 'ambient'].includes(form.storageClass) && (
                  <option value={form.storageClass.toUpperCase()}>{form.storageClass.toUpperCase()}</option>
                )}
              </select>
            </label>
            <label className="ui-stack"><span>Quy đổi pallet tối đa</span><input aria-label="Giới hạn pallet equivalent" disabled={saving} type="number" min="0.00000001" step="0.00000001" value={form.maxPalletEquivalent} onChange={event => setForm(current => ({ ...current, maxPalletEquivalent: event.target.value }))} placeholder="5" /></label>
            <label className="ui-stack"><span>Trọng lượng tối đa (kg)</span><input aria-label="Giới hạn trọng lượng kg" disabled={saving} type="number" min="0.000001" step="0.000001" value={form.maxWeightKg} onChange={event => setForm(current => ({ ...current, maxWeightKg: event.target.value }))} placeholder="1500" /></label>
            <label className="ui-stack"><span>Thể tích tối đa (m³)</span><input aria-label="Giới hạn thể tích m3" disabled={saving} type="number" min="0.00000001" step="0.00000001" value={form.maxVolumeM3} onChange={event => setForm(current => ({ ...current, maxVolumeM3: event.target.value }))} placeholder="10" /></label>
          </div>
        </div>

        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Cấu trúc vật lý • Khu vực / Dãy / Kệ / Tầng / Ô lưu trữ</div>
          {!editing || hasLockedStructure ? null : <p className="ui-muted-text">Vị trí cũ có thể giữ nguyên chưa gắn cấu trúc. Nếu gán mới, cần nhập đủ cả 5 cấp.</p>}
          <div className="warehouse-location-path-grid">
            <label className="ui-stack"><span style={{ whiteSpace: 'nowrap' }}>Khu vực *</span><input aria-label="Zone" disabled={saving || hasLockedStructure} value={form.zone} onChange={event => setForm(current => ({ ...current, zone: event.target.value }))} maxLength={32} placeholder="ZONE-A" /></label>
            <label className="ui-stack"><span style={{ whiteSpace: 'nowrap' }}>Dãy *</span><input aria-label="Aisle" disabled={saving || hasLockedStructure} value={form.aisle} onChange={event => setForm(current => ({ ...current, aisle: event.target.value }))} maxLength={32} placeholder="A01" /></label>
            <label className="ui-stack"><span style={{ whiteSpace: 'nowrap' }}>Kệ *</span><input aria-label="Rack" disabled={saving || hasLockedStructure} value={form.rack} onChange={event => setForm(current => ({ ...current, rack: event.target.value }))} maxLength={32} placeholder="R02" /></label>
            <label className="ui-stack"><span style={{ whiteSpace: 'nowrap' }}>Tầng *</span><input aria-label="Level" disabled={saving || hasLockedStructure} value={form.level} onChange={event => setForm(current => ({ ...current, level: event.target.value }))} maxLength={32} placeholder="L03" /></label>
            <label className="ui-stack"><span style={{ whiteSpace: 'nowrap' }}>Ô lưu trữ *</span><input aria-label="Bin" disabled={saving || hasLockedStructure} value={form.bin} onChange={event => setForm(current => ({ ...current, bin: event.target.value }))} maxLength={32} placeholder="B04" /></label>
          </div>
          {hasLockedStructure && <p className="ui-muted-text">Đường dẫn cấu trúc đã được khóa. Muốn đổi vị trí vật lý phải tạo mã vị trí mới để không làm sai lịch sử dịch chuyển.</p>}
        </div>

        {editing && (
          <div className="warehouse-location-flags">
            <label className="ui-checkbox-label"><input type="checkbox" checked={form.isActive} onChange={event => setForm(current => ({ ...current, isActive: event.target.checked }))} disabled={saving} />Đang hoạt động</label>
            <label className="ui-checkbox-label"><input type="checkbox" checked={form.isBlocked} onChange={event => setForm(current => ({ ...current, isBlocked: event.target.checked }))} disabled={saving} />Bị khóa</label>
          </div>
        )}
      </form>
    </UiDrawer>
  );
}
