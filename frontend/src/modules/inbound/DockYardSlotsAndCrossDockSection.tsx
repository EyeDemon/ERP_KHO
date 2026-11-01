import React from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import {
  directionLabel,
  type Dock,
  type DwellMonitorItem,
  emptyYard,
  isMaintenanceItem,
  messageOf,
  normalizeVietnameseText,
  statusLabel,
  type YardForm,
  type YardSlot,
} from './DockYardTypes';

export function DockYardSlotsAndCrossDockSection({
  activeSectionTab,
  canManageDock,
  setYardForm,
  yardSlots,
  dwellItems,
  inboundActiveCount,
  outboundActiveCount,
  crossDockMatchedCount,
  crossDockBusy,
  setCrossDockBusy,
  crossDockResult,
  setCrossDockResult,
  warehouseId,
  docks,
  setError,
}: {
  activeSectionTab: 'appointments' | 'yard' | 'crossdock' | 'all';
  canManageDock: boolean;
  setYardForm: React.Dispatch<React.SetStateAction<YardForm | null>>;
  yardSlots: YardSlot[];
  dwellItems: DwellMonitorItem[];
  inboundActiveCount: number;
  outboundActiveCount: number;
  crossDockMatchedCount: number;
  crossDockBusy: boolean;
  setCrossDockBusy: React.Dispatch<React.SetStateAction<boolean>>;
  crossDockResult: { dispatchId: string; dockDoorCode: string; stagedHuCount: number; status: string } | null;
  setCrossDockResult: React.Dispatch<React.SetStateAction<{ dispatchId: string; dockDoorCode: string; stagedHuCount: number; status: string } | null>>;
  warehouseId: number;
  docks: Dock[];
  setError: React.Dispatch<React.SetStateAction<string>>;
}) {
  return (
    <>
      <div style={activeSectionTab === 'yard' || activeSectionTab === 'all' ? { display: 'grid', gap: 16 } : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}>
        <UiCard title="Vị trí sân bãi" subtitle="Quản lý các ô đỗ chờ tại sân bãi và trạng thái chiếm dụng thực tế">
          {canManageDock && <div className="ui-inline-actions"><button type="button" onClick={() => setYardForm(emptyYard())}>Thêm vị trí sân bãi</button></div>}
          <UiTableScroll>
            <table className="ui-table-compact" style={{ tableLayout: 'fixed', width: '100%' }} aria-label="Danh sách vị trí sân bãi">
              <thead>
                <tr>
                  <th style={{ width: '22%' }}>Mã bãi</th>
                  <th style={{ width: '28%' }}>Tên vị trí</th>
                  <th style={{ width: '26%' }}>Tình trạng</th>
                  <th style={{ width: '24%' }}>Trạng thái & Sửa</th>
                </tr>
              </thead>
              <tbody>
                {yardSlots.map(slot => {
                  const slotMaintenance = isMaintenanceItem(slot.isActive, slot.code, slot.name);
                  return (
                    <tr key={slot.id}>
                      <td className="ui-cell-nowrap"><strong>{slot.code}</strong></td>
                      <td>{normalizeVietnameseText(slot.name)}</td>
                      <td>{slot.occupied ? <UiBadge tone="warning"><span style={{ whiteSpace: 'nowrap' }}>{slot.occupiedByAppointmentCode || 'Đang dùng'}</span></UiBadge> : <UiBadge tone="success"><span style={{ whiteSpace: 'nowrap' }}>Trống</span></UiBadge>}</td>
                      <td>
                        <div>
                          <UiBadge tone={slotMaintenance ? 'warning' : 'success'}>
                            <span style={{ whiteSpace: 'nowrap' }}>{slotMaintenance ? 'Bảo trì' : 'Hoạt động'}</span>
                          </UiBadge>
                        </div>
                        {canManageDock && (
                          <div className="ui-inline-actions" style={{ marginTop: 6 }}>
                            <button type="button" aria-label={'Sửa vị trí sân bãi ' + slot.code} onClick={() => setYardForm({ id: slot.id, code: slot.code, name: slot.name, isActive: slot.isActive, rowVersion: slot.rowVersion })}>Sửa</button>
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </UiTableScroll>
        </UiCard>

        <UiCard title="Giám sát thời gian lưu bãi" subtitle="Theo dõi thời gian chờ của phương tiện tại sân bãi và cửa kho theo cam kết SLA">
          <UiTableScroll>
            <table className="ui-table-compact" style={{ tableLayout: 'fixed', width: '100%' }} aria-label="Bảng giám sát thời gian lưu bãi phương tiện">
              <thead>
                <tr>
                  <th style={{ width: '26%' }}>Lịch hẹn</th>
                  <th style={{ width: '26%' }}>Phương tiện</th>
                  <th style={{ width: '22%' }}>Bãi / Cửa</th>
                  <th style={{ width: '26%' }}>Lưu bãi & Cam kết</th>
                </tr>
              </thead>
              <tbody>
                {dwellItems.map(item => (
                  <tr key={item.appointmentId}>
                    <td className="ui-cell-nowrap">
                      <strong>{`Phiếu hẹn: ${item.appointmentCode}`}</strong>
                      <br />
                      <small>{directionLabel(item.direction)} • {statusLabel[item.status] || item.status}</small>
                    </td>
                    <td>
                      {item.vehiclePlate || 'Chưa khai báo biển số'}
                      <br />
                      <small>{item.carrierName || '—'}{item.driverName ? ' • ' + item.driverName : ''}</small>
                    </td>
                    <td>
                      Bãi: {item.yardSlotCode || '—'}
                      <br />
                      <small>Cửa: {item.dockCode || '—'}</small>
                    </td>
                    <td>
                      <strong>{item.dwellMinutes} phút</strong>
                      <br />
                      <UiBadge tone={item.slaState === 'OVERDUE' ? 'danger' : item.slaState === 'WARNING' ? 'warning' : 'success'}>
                        {item.slaState === 'OVERDUE' ? 'Quá hạn (> 120p)' : item.slaState === 'WARNING' ? 'Cảnh báo (> 60p)' : 'Đúng hạn'}
                      </UiBadge>
                    </td>
                  </tr>
                ))}
                {dwellItems.length === 0 && (
                  <tr>
                    <td className="ui-empty-cell" colSpan={4}>
                      Hiện không có phương tiện nào đang lưu bãi hoặc chờ phục vụ tại cửa kho.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </UiTableScroll>
        </UiCard>
      </div>

      <div style={activeSectionTab === 'crossdock' || activeSectionTab === 'all' ? undefined : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}>
        <UiCard title="Điều phối trung chuyển thẳng" subtitle="Ghép nhanh hàng vừa tiếp nhận sang bãi tập kết xuất kho không qua cất hàng lên kệ">
          <UiMetricGrid>
            <UiMetric value={inboundActiveCount} label="Chuyến nhập tại cổng / cửa kho" />
            <UiMetric value={outboundActiveCount} label="Chuyến xuất chờ ghép hàng" />
            <UiMetric value={crossDockMatchedCount} label="Cặp trung chuyển khả thi" />
          </UiMetricGrid>
          <details style={{ margin: '8px 0', padding: '8px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <summary style={{ cursor: 'pointer', fontSize: 13, fontWeight: 600, color: '#334155' }}>
              Chi tiết quy tắc & kỹ thuật hệ thống
            </summary>
            <p className="ui-muted-text" style={{ fontSize: 13, margin: '8px 0 0' }}>
              Chính sách điều phối (Cross-docking): <strong>Ghép trực tiếp hàng vừa tiếp nhận sang bãi tập kết xuất kho không qua cất hàng lên kệ</strong>.
            </p>
          </details>
          <div className="ui-inline-actions">
            <button
              type="button"
              disabled={crossDockBusy}
              style={{ background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7' }}
              onClick={() => {
                if (crossDockBusy) return;
                setCrossDockBusy(true);
                setError('');
                void apiClient
                  .post<Record<string, unknown>>('/api/v1/dock-yard/dispatch-crossdock', {
                    warehouseId,
                    dockDoorCode: docks[0]?.code ?? 'DOCK-XD-01',
                  })
                  .then(res => {
                    const data = res?.data ?? {};
                    if (typeof data.dispatchId !== 'string' || !data.dispatchId) {
                      setCrossDockResult(null);
                      setError('Phản hồi điều phối trung chuyển thẳng không hợp lệ từ máy chủ.');
                      return;
                    }
                    setCrossDockResult({
                      dispatchId: data.dispatchId,
                      dockDoorCode: typeof data.dockDoorCode === 'string' && data.dockDoorCode ? data.dockDoorCode : docks[0]?.code ?? 'DOCK-XD-01',
                      stagedHuCount: typeof data.stagedHuCount === 'number' ? data.stagedHuCount : 0,
                      status: typeof data.status === 'string' && data.status ? data.status : 'Đã định tuyến trung chuyển',
                    });
                  })
                  .catch(failure => {
                    setCrossDockResult(null);
                    setError(messageOf(failure, 'Không thể điều phối luồng trung chuyển thẳng.'));
                  })
                  .finally(() => setCrossDockBusy(false));
              }}
            >
              Điều phối nhanh luồng trung chuyển thẳng
            </button>
            {crossDockResult && (
              <UiBadge tone="success">
                {`Mã lệnh: ${crossDockResult.dispatchId} • Cửa kho: ${crossDockResult.dockDoorCode} • Kiện chuyển thẳng: ${crossDockResult.stagedHuCount} • Trạng thái: ${crossDockResult.status}`}
              </UiBadge>
            )}
          </div>
        </UiCard>
      </div>
    </>
  );
}
