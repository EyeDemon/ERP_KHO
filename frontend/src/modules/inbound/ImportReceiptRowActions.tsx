import React from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { type ImportReceipt } from './ImportReceiptTypes';

export interface ImportReceiptRowActionsProps {
  receipt: ImportReceipt;
  isMobile?: boolean;
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

export const ImportReceiptRowActions: React.FC<ImportReceiptRowActionsProps> = ({
  receipt: r,
  isMobile = false,
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
  const canObserveDraft = r.status === 'Draft' && canObserve;
  const canCancelDraft = r.status === 'Draft' && canCancel;
  const canApproveReceipt =
    (r.status === 'Received' || (r.status === 'QcCompleted' && canApproveQc)) &&
    canApprove &&
    r.createdBy !== userId;
  const canPostReceipt = r.status === 'ReadyToPost' && canPost && r.createdBy !== userId;
  const canExpressPost =
    ['Draft', 'Received', 'ReadyToPost'].includes(r.status) &&
    !r.requiresQc &&
    (canPost || canApprove || canReceive);

  const triggerExpressPost = () => {
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
        setSuccessMsg(
          `Đã ghi sổ nhanh (1 bước) phiếu ${r.code} vào vị trí ${targetLocationCode} & cập nhật tồn kho.`,
        );
        return fetchData();
      })
      .catch((err) => setError(permissionError(err, 'Không thể thực hiện Ghi sổ nhanh (1 bước).')))
      .finally(() => setWorkflowId(null));
  };

  const primaryKind: 'observe' | 'approve' | 'post' | 'express' | null = canObserveDraft
    ? 'observe'
    : canApproveReceipt
      ? 'approve'
      : canPostReceipt
        ? 'post'
        : canExpressPost
          ? 'express'
          : null;

  const menuItemStyle: React.CSSProperties = {
    width: '100%',
    textAlign: 'left',
    justifyContent: 'flex-start',
    padding: '8px 10px',
    fontSize: 12.5,
    fontWeight: 600,
    borderRadius: 6,
    border: '1px solid transparent',
    background: '#ffffff',
    color: '#0f172a',
    whiteSpace: 'normal',
    lineHeight: 1.3,
  };

  const renderPrimaryButton = (fullWidth: boolean) => {
    const style: React.CSSProperties = {
      background: '#1d4ed8',
      color: '#ffffff',
      borderColor: '#1d4ed8',
      fontWeight: 600,
      ...(fullWidth ? { flex: '1 1 auto', minHeight: 44, whiteSpace: 'normal', textAlign: 'center', lineHeight: 1.25 } : {}),
    };
    if (primaryKind === 'observe') {
      return (
        <button type="button" className="ui-btn-primary" style={style} disabled={workflowId === r.id} onClick={() => void observeReceipt(r.id)}>
          {workflowId === r.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}
        </button>
      );
    }
    if (primaryKind === 'approve') {
      return (
        <button type="button" className="ui-btn-primary" style={style} disabled={approvingId === r.id} onClick={() => void handleApprove(r.id)}>
          {approvingId === r.id ? 'Đang duyệt...' : 'Duyệt để ghi nhận tồn kho'}
        </button>
      );
    }
    if (primaryKind === 'post') {
      return (
        <button type="button" className="ui-btn-primary" style={style} disabled={workflowId === r.id} onClick={() => void runWorkflow(r.id, 'post')}>
          {workflowId === r.id ? 'Đang ghi nhận tồn kho...' : 'Ghi nhận tồn kho'}
        </button>
      );
    }
    if (primaryKind === 'express') {
      return (
        <button type="button" className="ui-btn-primary" data-testid="btn-express-post-inbound" style={style} disabled={workflowId === r.id} onClick={triggerExpressPost}>
          {workflowId === r.id ? 'Đang ghi sổ nhanh...' : 'Ghi sổ nhanh (1 bước)'}
        </button>
      );
    }
    return null;
  };

  if (isMobile) {
    return (
      <div className="import-row-actions" style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        {renderPrimaryButton(true)}
        <button
          type="button"
          className="ui-btn-secondary"
          style={{ minHeight: 44, whiteSpace: 'nowrap' }}
          onClick={() => void handleViewDetails(r.id)}
        >
          Chi tiết
        </button>
        <details className="ui-row-more-menu" style={{ position: 'relative' }}>
          <summary className="ui-more-summary" aria-label={`Thao tác khác cho phiếu ${r.code}`}>
            ⋯
          </summary>
          <div className="ui-row-more-popover">
            <button type="button" style={menuItemStyle} disabled={printLoadingId !== null} onClick={(e) => void openPrintPreview(r.id, e.currentTarget)}>
              {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
            </button>
            {canExpressPost && primaryKind !== 'express' && (
              <button type="button" data-testid="btn-express-post-inbound" style={menuItemStyle} disabled={workflowId === r.id} onClick={triggerExpressPost}>
                {workflowId === r.id ? 'Đang ghi sổ nhanh...' : 'Ghi sổ nhanh (1 bước)'}
              </button>
            )}
            {canCancelDraft && (
              <button type="button" style={{ ...menuItemStyle, color: '#b91c1c' }} onClick={() => void handleCancel(r.id)}>
                Hủy
              </button>
            )}
          </div>
        </details>
      </div>
    );
  }

  return (
    <div className="import-row-actions" style={{ display: 'inline-flex', gap: 6, alignItems: 'center', flexWrap: 'nowrap' }}>
      <button
        type="button"
        className="ui-btn-secondary"
        style={{ padding: '4px 10px', fontSize: 12.5, whiteSpace: 'nowrap' }}
        onClick={() => void handleViewDetails(r.id)}
      >
        Chi tiết
      </button>
      <details className="ui-row-more-menu" style={{ position: 'relative' }}>
        <summary className="ui-more-summary" aria-label={`Thao tác khác cho phiếu ${r.code}`}>
          ⋯
        </summary>
        <div className="ui-row-more-popover">
          <button type="button" style={menuItemStyle} disabled={printLoadingId !== null} onClick={(e) => void openPrintPreview(r.id, e.currentTarget)}>
            {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
          </button>
          {canObserveDraft && (
            <button type="button" style={{ ...menuItemStyle, color: '#1d4ed8' }} disabled={workflowId === r.id} onClick={() => void observeReceipt(r.id)}>
              {workflowId === r.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}
            </button>
          )}
          {canApproveReceipt && (
            <button type="button" style={{ ...menuItemStyle, color: '#1d4ed8' }} disabled={approvingId === r.id} onClick={() => void handleApprove(r.id)}>
              {approvingId === r.id ? 'Đang duyệt...' : 'Duyệt để ghi nhận tồn kho'}
            </button>
          )}
          {canPostReceipt && (
            <button type="button" style={{ ...menuItemStyle, color: '#1d4ed8' }} disabled={workflowId === r.id} onClick={() => void runWorkflow(r.id, 'post')}>
              {workflowId === r.id ? 'Đang ghi nhận tồn kho...' : 'Ghi nhận tồn kho'}
            </button>
          )}
          {canExpressPost && (
            <button type="button" data-testid="btn-express-post-inbound" style={{ ...menuItemStyle, color: '#065f46' }} disabled={workflowId === r.id} onClick={triggerExpressPost}>
              {workflowId === r.id ? 'Đang ghi sổ nhanh...' : 'Ghi sổ nhanh (1 bước)'}
            </button>
          )}
          {canCancelDraft && (
            <button type="button" style={{ ...menuItemStyle, color: '#b91c1c' }} onClick={() => void handleCancel(r.id)}>
              Hủy
            </button>
          )}
        </div>
      </details>
    </div>
  );
};
