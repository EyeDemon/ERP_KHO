import React from 'react';
import { roundQty4 } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiPageHeader, UiTableScroll } from '../../ui/ProductionUi';
import type { LocationOption, StocktakeDetailRow, StocktakeResponse, Warehouse } from './stocktakesShared';
import { getStatusText, normalizeStatusCode, statusTone } from './stocktakesShared';

interface StocktakesCreateSectionProps {
  warehouses: Warehouse[]; warehousesLoading: boolean; warehousesError: string; onRetryWarehouses: () => void;
  createWarehouseId: number | ''; setCreateWarehouseId: (val: number | '') => void;
  createLocationId: number | ''; setCreateLocationId: (val: number | '') => void;
  createLocations: LocationOption[]; createIsBlindCount: boolean; setCreateIsBlindCount: (val: boolean) => void;
  createNote: string; setCreateNote: (val: string) => void;
  formLoading: boolean; formError: string; onSubmit: (e: React.FormEvent) => void; onCancel: () => void;
}

export const StocktakesCreateSection: React.FC<StocktakesCreateSectionProps> = ({
  warehouses, warehousesLoading, warehousesError, onRetryWarehouses,
  createWarehouseId, setCreateWarehouseId, createLocationId, setCreateLocationId,
  createLocations, createIsBlindCount, setCreateIsBlindCount,
  createNote, setCreateNote, formLoading, formError, onSubmit, onCancel,
}) => (
  <>
    <UiPageHeader
      eyebrow="Kiểm soát tồn kho"
      title="Tạo phiếu kiểm kê mới"
      description="Chọn kho cần kiểm kê; hệ thống sẽ tạo danh sách sản phẩm theo tồn hiện tại của kho."
    />

    <UiCard title="Thông tin kiểm kê">
      <form className="stocktake-form" onSubmit={onSubmit}>
        {formError && <p role="alert">{formError}</p>}

        <label className="ui-stack">
          <span>Kho hàng *</span>
          {warehousesLoading ? (
            <span role="status" className="ui-muted-text">Đang tải danh sách kho...</span>
          ) : warehousesError ? (
            <span role="alert">{warehousesError} <button type="button" onClick={onRetryWarehouses}>Thử lại</button></span>
          ) : warehouses.length === 0 ? (
            <span className="stocktake-empty-warning">Không có kho đang hoạt động.</span>
          ) : (
            <select
              aria-label="Kho hàng kiểm kê"
              value={createWarehouseId}
              onChange={(e) => setCreateWarehouseId(e.target.value ? parseInt(e.target.value, 10) : '')}
              disabled={formLoading}
              required
            >
              <option value="">-- Chọn kho hàng --</option>
              {warehouses.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          )}
        </label>

        <label className="ui-stack">
          <span>Vị trí kệ kiểm kê (Kiểm kê chu kỳ - tùy chọn)</span>
          <select
            aria-label="Vị trí kiểm kê"
            value={createLocationId}
            onChange={(e) => setCreateLocationId(e.target.value ? parseInt(e.target.value, 10) : '')}
            disabled={formLoading || createWarehouseId === ''}
          >
            <option value="">-- Toàn bộ kho --</option>
            {createLocations.map((loc) => <option key={loc.id} value={loc.id}>{loc.code} — {loc.name}</option>)}
          </select>
        </label>

        <label className="ui-inline-actions">
          <input
            aria-label="Kiểm kê mù (Ẩn tồn hệ thống)"
            type="checkbox"
            checked={createIsBlindCount}
            onChange={(e) => setCreateIsBlindCount(e.target.checked)}
            disabled={formLoading}
          />
          <span>Kiểm kê mù (Ẩn số lượng tồn hệ thống khi nhân viên kiểm đếm)</span>
        </label>

        <label className="ui-stack">
          <span>Ghi chú</span>
          <textarea aria-label="Ghi chú kiểm kê" value={createNote} onChange={(e) => setCreateNote(e.target.value)} disabled={formLoading} rows={3} />
        </label>

        <div className="stocktake-page-actions">
          <button type="submit" disabled={formLoading || warehousesLoading || !!warehousesError || warehouses.length === 0}>
            {formLoading ? 'Đang tạo...' : 'Tạo mới'}
          </button>
          <button type="button" onClick={onCancel} disabled={formLoading}>Hủy</button>
        </div>
      </form>
    </UiCard>
  </>
);

interface StocktakesDetailSectionProps {
  currentDetail: StocktakeResponse; canApprove: boolean; userId: number | null; loading: boolean; freezeLoading: boolean;
  onBackToList: () => void; onCountFreeze: () => void; onApprove: () => void;
  editingRowId: number | null; editActualQuantity: number | string; setEditActualQuantity: (val: number | string) => void;
  editNote: string; setEditNote: React.Dispatch<React.SetStateAction<string>>;
  rowLoading: boolean; rowError: string; rowSuccessId: number | null;
  onRowEdit: (row: StocktakeDetailRow) => void; onRowSave: (row: StocktakeDetailRow) => void;
  onRowCancel: () => void; onRecount: (row: StocktakeDetailRow) => void;
}

const rightNumStyle: React.CSSProperties = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' };

const renderVariance = (systemQty: number, actualQty?: number | string | null) => {
  if (actualQty === null || actualQty === undefined || actualQty === '')
    return <span className="stocktake-variance neutral">Chưa nhập</span>;
  const actual = Number(actualQty);
  if (isNaN(actual)) return <span className="stocktake-variance neutral">Lỗi</span>;
  const diff = roundQty4(actual - systemQty);
  if (diff > 0) return <span className="stocktake-variance success">+{diff} (Thừa)</span>;
  if (diff < 0) return <span className="stocktake-variance danger">{diff} (Thiếu)</span>;
  return <span className="stocktake-variance neutral">0 (Khớp)</span>;
};

export const StocktakesDetailSection: React.FC<StocktakesDetailSectionProps> = ({
  currentDetail, canApprove, userId, loading, freezeLoading, onBackToList, onCountFreeze, onApprove,
  editingRowId, editActualQuantity, setEditActualQuantity, editNote, setEditNote,
  rowLoading, rowError, rowSuccessId, onRowEdit, onRowSave, onRowCancel, onRecount,
}) => {
  const statusCode = normalizeStatusCode(currentDetail.status);
  const isBlindOrder = Boolean(currentDetail.isBlindCount || currentDetail.note?.toUpperCase().startsWith('[BLIND]'));

  return (
    <>
      <UiCard title={'Thông tin phiếu • ' + currentDetail.code}>
        <div className="stocktake-page-actions" style={{ marginBottom: 12 }}>
          <button type="button" onClick={onBackToList}>Quay lại danh sách</button>
          {statusCode === 0 && canApprove && (
            <button type="button" onClick={onCountFreeze} disabled={freezeLoading || loading}>
              {freezeLoading ? 'Đang khóa...' : 'Đóng băng kiểm kê'}
            </button>
          )}
          {[0, 3, 4, 5].includes(statusCode) && canApprove && currentDetail.createdBy !== userId && (
            <button type="button" className="ui-primary-button" onClick={onApprove} disabled={loading}>
              {loading ? 'Đang xử lý...' : 'Duyệt phiếu'}
            </button>
          )}
        </div>
        <div className="stocktake-detail-meta" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}>
          <div><strong>Kho:</strong> {currentDetail.warehouseName}</div>
          <div>
            <strong>Trạng thái:</strong>{' '}
            <UiBadge tone={statusTone(currentDetail.status)}>{getStatusText(currentDetail.status)}</UiBadge>
            {isBlindOrder && <> <UiBadge tone="warning">Kiểm kê mù (Ẩn tồn hệ thống)</UiBadge></>}
          </div>
          <div><strong>Ngày tạo:</strong> {new Date(currentDetail.createdAt).toLocaleString('vi-VN')}</div>
          <div><strong>Người tạo:</strong> {currentDetail.createdByName}</div>
          {currentDetail.approvedByName && (
            <>
              <div><strong>Người duyệt:</strong> {currentDetail.approvedByName}</div>
              <div><strong>Ngày duyệt:</strong> {currentDetail.approvedAt ? new Date(currentDetail.approvedAt).toLocaleString('vi-VN') : '—'}</div>
            </>
          )}
          <div className="wide"><strong>Ghi chú:</strong> {currentDetail.note || '—'}</div>
        </div>
      </UiCard>

      <UiCard title="Chi tiết kiểm đếm">
        <UiTableScroll>
          <table className="ui-table-compact stocktake-detail-table" aria-label={'Chi tiết kiểm kê ' + currentDetail.code}>
            <thead>
              <tr>
                <th>Mã SP</th><th>Tên sản phẩm</th><th>Đơn vị</th>
                <th className="ui-cell-numeric" style={rightNumStyle}>Tồn hệ thống</th>
                <th className="ui-cell-numeric" style={rightNumStyle}>Thực đếm</th>
                <th className="ui-cell-numeric" style={rightNumStyle}>Chênh lệch</th>
                <th>Ghi chú</th>
                {statusCode === 0 && <th>Hành động</th>}
              </tr>
            </thead>
            <tbody>
              {currentDetail.details.map((row) => {
                const isEditing = editingRowId === row.id;
                const isBlindDraft = statusCode === 0 && isBlindOrder;
                return (
                  <tr key={row.id}>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap' }}>
                      <strong style={{ whiteSpace: 'nowrap' }}>{row.productCode}</strong>
                      {row.locationCode && <><br /><small style={{ whiteSpace: 'nowrap' }}>{row.locationCode}</small></>}
                    </td>
                    <td>{row.productName}</td>
                    <td>{row.unitName === 'QA_UNIT' || row.unitName === 'QA-PCS' ? 'THÙNG' : row.unitName}</td>
                    <td className="ui-cell-numeric" style={rightNumStyle}>
                      {isBlindDraft ? <span className="ui-muted-text">Ẩn (Kiểm kê mù)</span> : row.systemQuantity}
                    </td>
                    <td className="ui-cell-numeric" style={rightNumStyle}>
                      {isEditing ? (
                        <input
                          className="stocktake-editor"
                          aria-label={'Số lượng thực tế ' + row.productCode}
                          type="number" step="0.0001" min="0"
                          value={editActualQuantity}
                          onChange={(e) => setEditActualQuantity(e.target.value)}
                          disabled={rowLoading}
                        />
                      ) : row.actualQuantity !== undefined && row.actualQuantity !== null ? row.actualQuantity : '—'}
                    </td>
                    <td className="ui-cell-numeric" style={rightNumStyle}>
                      {isBlindDraft
                        ? <span className="stocktake-variance neutral">Ẩn khi đếm mù</span>
                        : isEditing
                          ? renderVariance(row.systemQuantity, editActualQuantity)
                          : renderVariance(row.systemQuantity, row.actualQuantity)}
                    </td>
                    <td>
                      {isEditing ? (
                        <div className="ui-stack-xs">
                          <select
                            aria-label={'Mã lý do chênh lệch ' + row.productCode}
                            value=""
                            onChange={(e) => {
                              if (!e.target.value) return;
                              const prefix = `[REASON: ${e.target.value}] `;
                              setEditNote((prev) => prev.startsWith('[REASON:') ? prev.replace(/^\[REASON:\s*[^\]]+\]\s*/, prefix) : prefix + prev);
                            }}
                            disabled={rowLoading}
                          >
                            <option value="">-- Chọn nhanh mã lý do chênh lệch --</option>
                            <option value="COUNT_VARIANCE">Chênh lệch kiểm đếm định kỳ</option>
                            <option value="DAMAGED_FOUND">Phát hiện hàng hư hỏng vật lý</option>
                            <option value="EXPIRY_WRITE_OFF">Hủy hàng hết hạn sử dụng</option>
                            <option value="SUPPLIER_SHORTAGE">Thiếu hụt nguồn giao từ nhà cung cấp</option>
                            <option value="MISPLACED_STOCK">Sai lệch vị trí lưu kho nội bộ</option>
                            <option value="RECALL_QUARANTINE">Cô lập thu hồi lô hàng khẩn cấp</option>
                            <option value="DAMAGED">Hư hỏng</option>
                            <option value="MISPLACED">Thất lạc / Sai vị trí</option>
                            <option value="SUPPLIER_SHORT">Thiếu từ nhà cung cấp</option>
                            <option value="COUNT_ERROR">Sai sót kiểm đếm</option>
                          </select>
                          <input
                            className="stocktake-editor-note"
                            aria-label={'Ghi chú kiểm kê ' + row.productCode}
                            type="text"
                            value={editNote}
                            onChange={(e) => setEditNote(e.target.value)}
                            disabled={rowLoading}
                          />
                        </div>
                      ) : row.note || '—'}
                    </td>
                    {statusCode === 0 && (
                      <td>
                        {isEditing ? (
                          <div>
                            <div className="stocktake-row-actions">
                              <button type="button" onClick={() => onRowSave(row)} disabled={rowLoading}>{rowLoading ? '...' : 'Lưu'}</button>
                              <button type="button" onClick={onRowCancel} disabled={rowLoading}>Hủy</button>
                            </div>
                            {rowError && <div role="alert" className="stocktake-row-error">{rowError}</div>}
                          </div>
                        ) : (
                          <div className="stocktake-row-actions">
                            <button type="button" onClick={() => onRowEdit(row)}>Nhập SL</button>
                            {canApprove && row.actualQuantity !== undefined && row.actualQuantity !== null && (
                              <button type="button" className="ui-btn-secondary" onClick={() => onRecount(row)} disabled={rowLoading}>
                                Yêu cầu đếm lại
                              </button>
                            )}
                            {rowSuccessId === row.id && <span role="status" className="stocktake-row-success">✓ Đã lưu</span>}
                          </div>
                        )}
                      </td>
                    )}
                  </tr>
                );
              })}
              {currentDetail.details.length === 0 && (
                <tr><td colSpan={statusCode === 0 ? 8 : 7} className="ui-empty-cell">Không có sản phẩm nào trong kho này</td></tr>
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </>
  );
};
