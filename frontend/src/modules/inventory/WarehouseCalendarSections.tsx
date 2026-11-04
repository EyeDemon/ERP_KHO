import React from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  exceptionTypeLabel,
  exceptionTypeTone,
  type CalendarException,
  type CalendarResponse,
  type Shift,
  type ShiftForm,
} from './warehouseCalendarShared';

const rightNumHeaderStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
  whiteSpace: 'normal',
  lineHeight: 1.25,
};

const rightNumCellStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
  whiteSpace: 'nowrap',
};

interface WarehouseShiftsSectionProps {
  calendar: CalendarResponse;
  canManage: boolean;
  saving: boolean;
  shiftForm: ShiftForm | null;
  setShiftForm: React.Dispatch<React.SetStateAction<ShiftForm | null>>;
  onOpenShift: (shift?: Shift) => void;
  onSaveShift: (event: React.FormEvent) => void;
}

export const WarehouseShiftsSection: React.FC<WarehouseShiftsSectionProps> = ({
  calendar,
  canManage,
  saving,
  shiftForm,
  setShiftForm,
  onOpenShift,
  onSaveShift,
}) => (
  <>
    <UiCard title="Ca làm việc & Năng lực xử lý">
      <div className="ui-inline-actions">
        {canManage && (
          <button type="button" className="ui-primary-button" onClick={() => onOpenShift()} disabled={!calendar.isConfigured}>
            Thêm ca
          </button>
        )}
      </div>
      <UiTableScroll>
        <table aria-label="Ca làm việc và capacity" className="ui-table-compact" style={{ tableLayout: 'fixed', width: '100%' }}>
          <colgroup>
            <col style={{ width: canManage ? '17%' : '19%' }} />
            <col style={{ width: canManage ? '10%' : '12%' }} />
            <col style={{ width: '7%' }} />
            <col style={{ width: '8.5%' }} />
            <col style={{ width: '8%' }} />
            <col style={{ width: '8%' }} />
            <col style={{ width: '6.5%' }} />
            <col style={{ width: '8.5%' }} />
            <col style={{ width: '7.5%' }} />
            <col style={{ width: '6%' }} />
            <col style={{ width: '7%' }} />
            {canManage && <col style={{ width: '6%' }} />}
          </colgroup>
          <thead>
            <tr>
              <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Ca</th>
              <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Khung giờ</th>
              <th style={rightNumHeaderStyle}>Nhân sự</th>
              <th style={rightNumHeaderStyle}>Pallet nhập/giờ</th>
              <th style={rightNumHeaderStyle}>Đơn xuất/giờ</th>
              <th style={rightNumHeaderStyle}>Cửa nhận/xuất</th>
              <th style={rightNumHeaderStyle}>Giờ công</th>
              <th style={rightNumHeaderStyle}>Sức chứa tập kết</th>
              <th style={rightNumHeaderStyle}>Trạm đóng gói</th>
              <th style={rightNumHeaderStyle}>Thiết bị</th>
              <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Trạng thái</th>
              {canManage && <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Thao tác</th>}
            </tr>
          </thead>
          <tbody>
            {calendar.shifts.map((shift) => (
              <tr key={shift.id}>
                <td><strong style={{ whiteSpace: 'nowrap' }}>{shift.code}</strong><br /><small>{shift.name}</small></td>
                <td>{shift.startTimeLocal}–{shift.endTimeLocal}{shift.overnight ? ' • +1 ngày' : ''}<br /><small>Nghỉ {shift.breakMinutes} phút</small></td>
                <td style={rightNumCellStyle}>{shift.plannedHeadcount}</td>
                <td style={rightNumCellStyle}>{shift.inboundPalletsPerHour ?? '—'}</td>
                <td style={rightNumCellStyle}>{shift.outboundOrdersPerHour ?? '—'}</td>
                <td style={rightNumCellStyle}>{shift.dockSlots ?? '—'}</td>
                <td style={rightNumCellStyle}>{shift.laborHours ?? '—'}</td>
                <td style={rightNumCellStyle}>{shift.stagingCapacity ?? '—'}</td>
                <td style={rightNumCellStyle}>{shift.packingStations ?? '—'}</td>
                <td style={rightNumCellStyle}>{shift.equipmentAvailable ?? '—'}</td>
                <td><UiBadge tone={shift.isActive ? 'success' : 'neutral'}>{shift.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                {canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => onOpenShift(shift)} style={{ whiteSpace: 'nowrap' }}>Sửa</button></td>}
              </tr>
            ))}
            {calendar.shifts.length === 0 && (
              <tr><td className="ui-empty-cell" colSpan={canManage ? 12 : 11}>Chưa có ca làm việc.</td></tr>
            )}
          </tbody>
        </table>
      </UiTableScroll>
    </UiCard>

    {shiftForm && canManage && (
      <UiCard title={shiftForm.id ? 'Sửa ca ' + shiftForm.code : 'Thêm ca làm việc'}>
        <form className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }} onSubmit={onSaveShift}>
          <label className="ui-stack"><span>Mã ca *</span><input aria-label="Mã ca" placeholder="VD: SHIFT-1" value={shiftForm.code} disabled={saving || Boolean(shiftForm.id)} onChange={(event) => setShiftForm((current) => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Tên ca *</span><input aria-label="Tên ca" placeholder="VD: Ca sáng" value={shiftForm.name} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, name: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Bắt đầu *</span><input aria-label="Giờ bắt đầu ca" type="time" value={shiftForm.startTimeLocal} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, startTimeLocal: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Kết thúc *</span><input aria-label="Giờ kết thúc ca" type="time" value={shiftForm.endTimeLocal} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, endTimeLocal: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Nghỉ (phút)</span><input aria-label="Phút nghỉ ca" type="number" min="0" value={shiftForm.breakMinutes} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, breakMinutes: event.target.value }))} /></label>
          <label className="ui-stack"><span>Định biên nhân sự</span><input aria-label="Planned headcount" type="number" min="0" value={shiftForm.plannedHeadcount} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, plannedHeadcount: event.target.value }))} /></label>
          <label className="ui-stack"><span>Pallet nhập / giờ</span><input aria-label="Inbound pallets per hour" type="number" min="0" step="0.01" value={shiftForm.inboundPalletsPerHour} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, inboundPalletsPerHour: event.target.value }))} /></label>
          <label className="ui-stack"><span>Đơn xuất / giờ</span><input aria-label="Outbound orders per hour" type="number" min="0" step="0.01" value={shiftForm.outboundOrdersPerHour} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, outboundOrdersPerHour: event.target.value }))} /></label>
          <label className="ui-stack"><span>Số cửa nhận/xuất</span><input aria-label="Dock slots" type="number" min="0" value={shiftForm.dockSlots} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, dockSlots: event.target.value }))} /></label>
          <label className="ui-stack"><span>Tổng giờ công</span><input aria-label="Labor hours" type="number" min="0" step="0.01" value={shiftForm.laborHours} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, laborHours: event.target.value }))} /></label>
          <label className="ui-stack"><span>Sức chứa khu tập kết</span><input aria-label="Staging capacity" type="number" min="0" step="0.01" value={shiftForm.stagingCapacity} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, stagingCapacity: event.target.value }))} /></label>
          <label className="ui-stack"><span>Số trạm đóng gói</span><input aria-label="Packing stations" type="number" min="0" value={shiftForm.packingStations} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, packingStations: event.target.value }))} /></label>
          <label className="ui-stack"><span>Thiết bị khả dụng</span><input aria-label="Equipment available" type="number" min="0" value={shiftForm.equipmentAvailable} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, equipmentAvailable: event.target.value }))} /></label>
          {shiftForm.id && (
            <label className="ui-checkbox-label">
              <input type="checkbox" checked={shiftForm.isActive} disabled={saving} onChange={(event) => setShiftForm((current) => current && ({ ...current, isActive: event.target.checked }))} /> Ca đang hoạt động
            </label>
          )}
          <div className="ui-inline-actions">
            <button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu ca'}</button>
            <button type="button" disabled={saving} onClick={() => setShiftForm(null)}>Hủy</button>
          </div>
        </form>
      </UiCard>
    )}
  </>
);

interface WarehouseExceptionsSectionProps {
  canManage: boolean;
  saving: boolean;
  governanceBusy: boolean;
  shiftVerification: { verificationId: string; shiftCoverageStatus: string } | null;
  laborOptimization: { optimizationId: string; interleavingMode: string; ergonomicSafetyStatus: string } | null;
  exceptionForm: { exceptionType: string; windowDateOrRange: string; reason: string };
  setExceptionForm: React.Dispatch<React.SetStateAction<{ exceptionType: string; windowDateOrRange: string; reason: string }>>;
  exceptions: CalendarException[];
  onVerifyShiftCoverage: () => void;
  onOptimizeLaborInterleaving: () => void;
  onSaveException: (event: React.FormEvent) => void;
}

export const WarehouseExceptionsSection: React.FC<WarehouseExceptionsSectionProps> = ({
  canManage,
  saving,
  governanceBusy,
  shiftVerification,
  laborOptimization,
  exceptionForm,
  setExceptionForm,
  exceptions,
  onVerifyShiftCoverage,
  onOptimizeLaborInterleaving,
  onSaveException,
}) => (
  <UiCard title="Lịch nghỉ lễ, Bảo trì & Ghi đè khẩn cấp">
    <div className="ui-inline-wrap">
      <button type="button" disabled={saving || governanceBusy} onClick={onVerifyShiftCoverage}>
        Kiểm định Độ phủ Ca trực & Cửa sổ Cắt đơn
      </button>
      {shiftVerification && (
        <UiBadge tone="success">
          {`Đã kiểm định ${shiftVerification.verificationId} • ${({ '24_7_CUTOFF_ALIGNED': 'Đã đồng bộ độ phủ ca trực & cửa sổ cắt đơn', SHIFT_COVERAGE_VALIDATED: 'Độ phủ ca trực đạt chuẩn' } as Record<string, string>)[shiftVerification.shiftCoverageStatus] || shiftVerification.shiftCoverageStatus}`}
        </UiBadge>
      )}
      <button type="button" disabled={saving || governanceBusy} onClick={onOptimizeLaborInterleaving}>
        Tối ưu Đan xen Nhiệm vụ, Định biên Nhân lực & An toàn Công thái học
      </button>
      {laborOptimization && (
        <UiBadge tone="success">
          {`Đã tối ưu ${laborOptimization.optimizationId} • ${({ DUAL_COMMAND_INTERLEAVED: 'Đan xen chu kỳ kép Cất/Lấy hàng' } as Record<string, string>)[laborOptimization.interleavingMode] || laborOptimization.interleavingMode} • ${({ OSHA_ERGONOMIC_COMPLIANT: 'Đạt chuẩn an toàn công thái học', ERGONOMIC_SAFE: 'An toàn công thái học' } as Record<string, string>)[laborOptimization.ergonomicSafetyStatus] || laborOptimization.ergonomicSafetyStatus}`}
        </UiBadge>
      )}
    </div>
    {canManage && (
      <form className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }} onSubmit={onSaveException}>
        <label className="ui-stack">
          <span>Loại ngoại lệ *</span>
          <select
            aria-label="Loại ngoại lệ lịch vận hành"
            value={exceptionForm.exceptionType}
            disabled={saving}
            onChange={(event) => setExceptionForm((current) => ({ ...current, exceptionType: event.target.value }))}
          >
            <option value="HOLIDAY_CLOSURE">Nghỉ lễ (Đóng kho)</option>
            <option value="MAINTENANCE_WINDOW">Bảo trì định kỳ</option>
            <option value="EMERGENCY_OPEN_OVERRIDE">Ghi đè mở kho khẩn cấp</option>
          </select>
        </label>
        <label className="ui-stack">
          <span>Ngày / Khung giờ áp dụng *</span>
          <input
            aria-label="Khung giờ ngoại lệ lịch kho"
            value={exceptionForm.windowDateOrRange}
            disabled={saving}
            placeholder="VD: 30/04/2026 - 01/05/2026"
            onChange={(event) => setExceptionForm((current) => ({ ...current, windowDateOrRange: event.target.value }))}
            required
          />
        </label>
        <label className="ui-stack">
          <span>Lý do điều hành *</span>
          <input
            aria-label="Lý do ngoại lệ lịch kho"
            value={exceptionForm.reason}
            disabled={saving}
            placeholder="VD: Nghỉ lễ hoặc bảo trì định kỳ"
            onChange={(event) => setExceptionForm((current) => ({ ...current, reason: event.target.value }))}
            required
          />
        </label>
        <div className="ui-inline-actions">
          <button type="submit" disabled={saving || !exceptionForm.windowDateOrRange.trim() || !exceptionForm.reason.trim()}>
            {saving ? 'Đang lưu...' : 'Ghi nhận ngoại lệ lịch vận hành'}
          </button>
        </div>
      </form>
    )}
    <UiTableScroll>
      <table aria-label="Danh sách ngoại lệ lịch vận hành" className="ui-table-compact">
        <thead>
          <tr>
            <th>Loại ngoại lệ</th>
            <th>Khung thời gian áp dụng</th>
            <th>Lý do điều hành</th>
            <th>Thời điểm ghi nhận</th>
          </tr>
        </thead>
        <tbody>
          {exceptions.map((item) => (
            <tr key={item.id}>
              <td>
                <UiBadge tone={exceptionTypeTone(item.exceptionType)}>
                  {exceptionTypeLabel[item.exceptionType] ?? item.exceptionType}
                </UiBadge>
              </td>
              <td><strong>{item.windowSummary}</strong></td>
              <td>{item.reason}</td>
              <td>{new Date(item.createdAtUtc).toLocaleString('vi-VN')}</td>
            </tr>
          ))}
          {exceptions.length === 0 && (
            <tr>
              <td className="ui-empty-cell" colSpan={4}>
                Chưa có ngoại lệ nghỉ lễ, bảo trì hoặc ghi đè khẩn cấp nào được ghi nhận cho kho này.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </UiTableScroll>
  </UiCard>
);
