import { UiCard, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL } from '../../utils/warehouseLabel';

interface ApprovalsSlaAndFilterBarProps {
  normalCount: number;
  warningCount: number;
  overdueCount: number;
  escalationResult: { escalationBatchId: string; escalatedStatus: string } | null;
  onTriggerEscalateOverdue: () => void;
  type: string;
  onChangeType: (v: string) => void;
  slaStatus: string;
  onChangeSlaStatus: (v: string) => void;
  keyword: string;
  onChangeKeyword: (v: string) => void;
  warehouseId: string;
  onChangeWarehouseId: (v: string) => void;
  creatorId: string;
  onChangeCreatorId: (v: string) => void;
  fromUtc: string;
  onChangeFromUtc: (v: string) => void;
  toUtc: string;
  onChangeToUtc: (v: string) => void;
}

export function ApprovalsSlaAndFilterBar({
  normalCount,
  warningCount,
  overdueCount,
  escalationResult,
  onTriggerEscalateOverdue,
  type,
  onChangeType,
  slaStatus,
  onChangeSlaStatus,
  keyword,
  onChangeKeyword,
  warehouseId,
  onChangeWarehouseId,
  creatorId,
  onChangeCreatorId,
  fromUtc,
  onChangeFromUtc,
  toUtc,
  onChangeToUtc,
}: ApprovalsSlaAndFilterBarProps) {
  return (
    <>
      <UiCard title="Tóm tắt Cam kết Thời gian Phê duyệt (SLA)">
        <div className="ui-inline-wrap">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%', marginBottom: 8 }}>
            <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontSize: 12, color: '#475569' }}>Đúng hạn</div>
              <strong style={{ fontSize: 16, color: '#065f46' }}>{normalCount}</strong>
            </div>
            <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontSize: 12, color: '#475569' }}>Sắp quá hạn</div>
              <strong style={{ fontSize: 16, color: '#b45309' }}>{warningCount}</strong>
            </div>
            <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontSize: 12, color: '#475569' }}>Quá hạn</div>
              <strong style={{ fontSize: 16, color: '#b91c1c' }}>{overdueCount}</strong>
            </div>
          </div>

          <button type="button" onClick={onTriggerEscalateOverdue}>
            Leo thang Nhắc duyệt Hồ sơ Quá hạn SLA
          </button>
          {escalationResult && (
            <div style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%', fontWeight: 600 }}>
              {`Đã leo thang lô nhắc duyệt ${escalationResult.escalationBatchId} • Trạng thái: Đã chuyển cấp quản lý xử lý hồ sơ quá hạn SLA`}
            </div>
          )}
        </div>
      </UiCard>

      <UiToolbar>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 12, width: '100%' }}>
          <UiToolbarField label="Loại chứng từ">
            <select aria-label="Loại chứng từ" value={type} onChange={event => onChangeType(event.target.value)}>
              <option value="">Tất cả loại</option>
              <option value="ImportReceipt">Phiếu nhập kho</option>
              <option value="ExportReceipt">Phiếu xuất kho</option>
              <option value="StockTransfer">Phiếu điều chuyển</option>
              <option value="Stocktake">Phiếu kiểm kê</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Mức SLA">
            <select aria-label="Mức SLA" value={slaStatus} onChange={event => onChangeSlaStatus(event.target.value)}>
              <option value="">Tất cả mức SLA</option>
              <option value="Normal">Đúng hạn</option>
              <option value="Warning">Sắp quá hạn</option>
              <option value="Overdue">Quá hạn</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Mã chứng từ">
            <input aria-label="Tìm mã chứng từ" value={keyword} onChange={event => onChangeKeyword(event.target.value)} placeholder="Tìm theo mã" />
          </UiToolbarField>
          <UiToolbarField label="Kho">
            <select aria-label="Mã kho" value={warehouseId} onChange={event => onChangeWarehouseId(event.target.value)}>
              <option value="">Tất cả kho</option>
              <option value="1">{CANONICAL_WH01_LABEL}</option>
              <option value="2">Kho Phía Bắc (WH02)</option>
              <option value="3">Kho Phía Nam (WH03)</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Người tạo">
            <select aria-label="Mã người tạo" value={creatorId} onChange={event => onChangeCreatorId(event.target.value)}>
              <option value="">Tất cả người tạo</option>
              <option value="1">Quản trị viên Hệ thống (qa_admin)</option>
              <option value="2">{`Quản lý ${CANONICAL_WH01_LABEL} (qa_manager)`}</option>
              <option value="3">{`Giám sát ${CANONICAL_WH01_LABEL} (qa_supervisor)`}</option>
              <option value="4">{`Nhân viên ${CANONICAL_WH01_LABEL} (qa_staff)`}</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Từ ngày">
            <input aria-label="Từ ngày" type="datetime-local" value={fromUtc} onChange={event => onChangeFromUtc(event.target.value)} />
          </UiToolbarField>
          <UiToolbarField label="Đến ngày">
            <input aria-label="Đến ngày" type="datetime-local" value={toUtc} onChange={event => onChangeToUtc(event.target.value)} />
          </UiToolbarField>
        </div>
      </UiToolbar>
    </>
  );
}
