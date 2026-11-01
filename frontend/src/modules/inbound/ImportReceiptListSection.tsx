import React from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  type ImportReceipt,
  type Warehouse,
  formatVnDateTime,
  mapCreatedByDisplayName,
  statusLabel,
  statusTone,
} from './ImportReceiptTypes';

export interface ImportReceiptKpiCardProps {
  receipts: ImportReceipt[];
}

export const ImportReceiptKpiCard: React.FC<ImportReceiptKpiCardProps> = ({ receipts }) => (
  <div style={{ order: 1 }}>
    <UiCard title="Tổng quan luồng tiếp nhận hàng đến">
      <div className="ui-inline-wrap">
        <div className="ui-kpi-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(5, minmax(0, 1fr))', gap: 10, width: '100%' }}>
          <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Tổng số phiếu nhập</div>
            <strong style={{ fontSize: 15, color: '#0f172a' }}>{receipts.length}</strong>
          </div>
          <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Đang soạn / Nhận tại cửa</div>
            <strong style={{ fontSize: 15, color: '#b45309' }}>
              {receipts.filter(r => r.status === 'Draft' || r.status === 'Received' || r.status.startsWith('Discrepancy')).length}
            </strong>
          </div>
          <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Đang chờ Kiểm định / Chờ duyệt</div>
            <strong style={{ fontSize: 15, color: '#b45309' }}>
              {receipts.filter(r => r.status === 'QcPending' || r.status === 'QcCompleted' || r.status === 'ReadyToPost').length}
            </strong>
          </div>
          <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Đã ghi sổ tồn kho</div>
            <strong style={{ fontSize: 15, color: '#065f46' }}>
              {receipts.filter(r => r.status === 'Posted' || r.status === 'Approved').length}
            </strong>
          </div>
          <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Phiếu đã hủy</div>
            <strong style={{ fontSize: 15, color: '#b91c1c' }}>
              {receipts.filter(r => r.status === 'Cancelled').length}
            </strong>
          </div>
        </div>
      </div>
    </UiCard>
  </div>
);

export interface ImportReceiptListCardProps {
  receipts: ImportReceipt[];
  warehouses: Warehouse[];
  selectedReceipt: ImportReceipt | null;
  listWarehouseFilter: string;
  setListWarehouseFilter: (val: string) => void;
  listStatusFilter: string;
  setListStatusFilter: (val: string) => void;
  listSearchFilter: string;
  setListSearchFilter: (val: string) => void;
  printLoadingId: number | null;
  workflowId: number | null;
  setWorkflowId: (val: number | null) => void;
  approvingId: number | null;
  userId: number | null;
  canObserve: boolean;
  canCancel: boolean;
  canApprove: boolean;
  canApproveQc: boolean;
  canPost: boolean;
  canReceive: boolean;
  receivingDestLocation: string;
  handleViewDetails: (id: number) => Promise<void>;
  openPrintPreview: (id: number, trigger: HTMLButtonElement) => Promise<void>;
  observeReceipt: (id: number, receipt?: ImportReceipt) => Promise<void>;
  handleCancel: (id: number) => Promise<void>;
  handleApprove: (id: number) => Promise<void>;
  runWorkflow: (id: number, command: 'receive' | 'qc-disposition' | 'post', receipt?: ImportReceipt) => Promise<void>;
  fetchData: () => Promise<void>;
  setError: (val: string) => void;
  setSuccessMsg: (val: string) => void;
}

export const ImportReceiptListCard: React.FC<ImportReceiptListCardProps> = ({
  receipts,
  warehouses,
  selectedReceipt,
  listWarehouseFilter,
  setListWarehouseFilter,
  listStatusFilter,
  setListStatusFilter,
  listSearchFilter,
  setListSearchFilter,
  printLoadingId,
  workflowId,
  setWorkflowId,
  approvingId,
  userId,
  canObserve,
  canCancel,
  canApprove,
  canApproveQc,
  canPost,
  canReceive,
  receivingDestLocation,
  handleViewDetails,
  openPrintPreview,
  observeReceipt,
  handleCancel,
  handleApprove,
  runWorkflow,
  fetchData,
  setError,
  setSuccessMsg,
}) => {
  const filteredReceipts = receipts.filter(r => {
    if (listWarehouseFilter && r.warehouseName !== listWarehouseFilter && String(r.warehouseId) !== listWarehouseFilter) return false;
    if (listStatusFilter && r.status !== listStatusFilter) return false;
    if (listSearchFilter.trim()) {
      const q = listSearchFilter.trim().toLowerCase();
      const hay = `${r.code} ${r.supplierCode ?? ''} ${r.supplierName ?? ''} ${r.warehouseName ?? ''}`.toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });
  const warehouseNames = Array.from(
    new Set([...receipts.map(r => r.warehouseName).filter(Boolean), ...warehouses.map(w => w.name).filter(Boolean)]),
  );

  return (
    <div style={{ order: 2 }}>
      <UiCard
        title="Danh sách phiếu nhập"
        subtitle="Theo dõi trạng thái phiếu nhập, ghi nhận số lượng thực tế và duyệt ghi sổ tồn kho"
      >
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end', marginBottom: 12, padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 200, maxWidth: 280 }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Kho nhận</span>
            <select aria-label="Lọc phiếu nhập theo kho" value={listWarehouseFilter} onChange={e => setListWarehouseFilter(e.target.value)} style={{ maxWidth: 280 }}>
              <option value="">Tất cả kho</option>
              {warehouseNames.map(wh => <option key={wh} value={wh}>Kho: {wh}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 200, maxWidth: 280 }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Trạng thái phiếu</span>
            <select aria-label="Lọc phiếu nhập theo trạng thái" value={listStatusFilter} onChange={e => setListStatusFilter(e.target.value)} style={{ maxWidth: 280 }}>
              <option value="">Tất cả trạng thái</option>
              <option value="Draft">Trạng thái: Nháp</option>
              <option value="Received">Trạng thái: Đã nhận hàng</option>
              <option value="QcPending">Trạng thái: Chờ kiểm định QC</option>
              <option value="ReadyToPost">Trạng thái: Sẵn sàng ghi sổ</option>
              <option value="Approved">Trạng thái: Đã duyệt</option>
              <option value="Posted">Trạng thái: Đã ghi nhận tồn kho</option>
              <option value="Cancelled">Trạng thái: Đã hủy</option>
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 220, maxWidth: 320, flex: '1 1 220px' }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Tìm mã phiếu / Nhà cung cấp</span>
            <input type="search" aria-label="Tìm mã phiếu hoặc nhà cung cấp" placeholder="Nhập mã PN, mã/tên nhà cung cấp..." value={listSearchFilter} onChange={e => setListSearchFilter(e.target.value)} style={{ maxWidth: 320 }} />
          </label>
        </div>
        <UiTableScroll>
          <table aria-label="Danh sách phiếu nhập">
            <thead>
              <tr>
                <th>Mã phiếu</th>
                <th>Ngày tạo</th>
                <th>Kho</th>
                <th>Nhà cung cấp</th>
                <th>Người tạo</th>
                <th>Trạng thái</th>
                <th>Hành động</th>
              </tr>
            </thead>
            <tbody>
              {filteredReceipts.map(r => (
                <tr key={r.id}>
                  <td style={{ whiteSpace: 'nowrap' }}><strong>{r.code}</strong></td>
                  <td style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{formatVnDateTime(r.createdAt)}</td>
                  <td>{r.warehouseName}</td>
                  <td>{r.supplierCode ? r.supplierCode + ' - ' + r.supplierName : '—'}</td>
                  <td>{mapCreatedByDisplayName(r.createdByName || (selectedReceipt?.id === r.id ? selectedReceipt.createdByName : ''), Boolean(r.createdBy))}</td>
                  <td className="import-status-cell"><UiBadge tone={statusTone(r.status)}><span style={{ whiteSpace: 'nowrap' }}>{statusLabel(r.status)}</span></UiBadge></td>
                  <td>
                    <div className="import-row-actions" style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', alignItems: 'center' }}>
                      <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => void handleViewDetails(r.id)}>Chi tiết</button>
                      <button type="button" style={{ whiteSpace: 'nowrap' }} disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)}>
                        {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
                      </button>
                      {r.status === 'Draft' && (
                        <>
                          {canObserve && (
                            <button
                              type="button"
                              style={{ whiteSpace: 'nowrap', background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }}
                              disabled={workflowId === r.id}
                              onClick={() => void observeReceipt(r.id)}
                            >
                              {workflowId === r.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}
                            </button>
                          )}
                          {canCancel && (
                            <button
                              type="button"
                              style={{ whiteSpace: 'nowrap', borderColor: '#dc2626', color: '#dc2626', background: '#fff1f2', fontWeight: 600 }}
                              onClick={() => void handleCancel(r.id)}
                            >
                              Hủy
                            </button>
                          )}
                        </>
                      )}
                      {(r.status === 'Received' || (r.status === 'QcCompleted' && canApproveQc)) && canApprove && r.createdBy !== userId && (
                        <button
                          type="button"
                          style={{ whiteSpace: 'nowrap', background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }}
                          onClick={() => void handleApprove(r.id)}
                          disabled={approvingId === r.id}
                        >
                          {approvingId === r.id ? 'Đang duyệt...' : 'Duyệt để ghi nhận tồn kho'}
                        </button>
                      )}
                      {['Draft', 'Received', 'ReadyToPost'].includes(r.status) && !r.requiresQc && (canPost || canApprove || canReceive) && (
                        <button
                          type="button"
                          data-testid="btn-express-post-inbound"
                          style={{ whiteSpace: 'nowrap', background: '#059669', color: '#ffffff', borderColor: '#059669', fontWeight: 600 }}
                          disabled={workflowId === r.id}
                          onClick={() => {
                            setWorkflowId(r.id);
                            setError('');
                            setSuccessMsg('');
                            const targetLocationCode = receivingDestLocation || 'A-01-01';
                            const idemKey = `exp-inb-${r.id}-${Date.now()}`;
                            void apiClient
                              .post(
                                `/api/v1/import-receipts/${r.id}/express-post`,
                                { targetLocationCode },
                                { headers: { 'Idempotency-Key': idemKey } },
                              )
                              .then(() => {
                                setSuccessMsg(`Đã nhập nhanh 1 bước (Express Post) phiếu ${r.code} vào vị trí ${targetLocationCode} & ghi sổ tồn kho.`);
                                return fetchData();
                              })
                              .catch((err) => {
                                setError(permissionError(err, 'Không thể thực hiện Nhập nhanh 1 bước (Express Post).'));
                              })
                              .finally(() => setWorkflowId(null));
                          }}
                        >
                          {workflowId === r.id ? 'Đang nhập nhanh...' : 'Nhập nhanh 1 bước (Express Post)'}
                        </button>
                      )}
                      {r.status === 'ReadyToPost' && canPost && r.createdBy !== userId && (
                        <button
                          type="button"
                          style={{ whiteSpace: 'nowrap', background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }}
                          disabled={workflowId === r.id}
                          onClick={() => void runWorkflow(r.id, 'post')}
                        >
                          {workflowId === r.id ? 'Đang ghi nhận tồn kho...' : 'Ghi nhận tồn kho'}
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {filteredReceipts.length === 0 && <tr><td colSpan={7} className="ui-empty-cell">Chưa có phiếu nhập</td></tr>}
            </tbody>
          </table>
        </UiTableScroll>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0', fontSize: 12.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
          <span>Hiển thị {filteredReceipts.length === 0 ? 0 : 1}–{filteredReceipts.length} / Tổng {filteredReceipts.length} phiếu nhập</span>
          <span>Trang 1 / 1 (Cỡ trang: 20)</span>
        </div>
      </UiCard>
    </div>
  );
};
