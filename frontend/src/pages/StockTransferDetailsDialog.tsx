import { useEffect, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
import { ArrowRight, Check, PackageCheck, Send, X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { UiBadge, UiTableScroll } from '../ui/ProductionUi';
import './StockTransferReturnForm.css';

type TransferLine = {
  productId: number;
  productCode: string;
  productName: string;
  requestedQuantity: number;
  dispatchedQuantity: number;
  receivedQuantity: number;
  missingQuantity: number;
  damagedQuantity: number;
  inTransitQuantity: number;
  note?: string;
};
type Transfer = {
  id: number;
  code: string;
  sourceWarehouseId: number;
  sourceWarehouseName: string;
  destinationWarehouseId: number;
  destinationWarehouseName: string;
  status: string | number;
  note?: string;
  createdBy: number;
  createdAt: string;
  approvedAt?: string;
  dispatchedAt?: string;
  receivedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  returnReasonCode?: string;
  returnReason?: string;
  reverseOfTransferId?: number;
  reverseTransferId?: number;
  reverseReasonCode?: string;
  reverseReason?: string;
  details: TransferLine[];
};


type Props = {
  selected: Transfer;
  status: string;
  statusNames: string[];
  statusLabel: Record<string, string>;
  statusTone: (status: string) => 'neutral' | 'success' | 'warning' | 'danger';
  canWrite: boolean;
  canReturn: boolean;
  canApprove: boolean;
  userId: number | null;
  actionInFlight: boolean;
  receive: Record<number, { receivedQuantity: number; missingQuantity: number; damagedQuantity: number }>;
  setReceive: (value: Record<number, { receivedQuantity: number; missingQuantity: number; damagedQuantity: number }>) => void;
  onClose: () => void;
  onAction: (action: string, body?: unknown) => Promise<void>;
  onReceive: () => Promise<void>;
  onOpenTransfer: (id: number) => Promise<void>;
};

export default function StockTransferDetailsDialog({
  selected, status, statusNames, statusLabel, statusTone, canWrite, canReturn, canApprove, userId,
  actionInFlight, receive, setReceive, onClose, onAction, onReceive, onOpenTransfer,
}: Props) {
  const canTrace = usePermission('inventory_traceability.read');
  const inRouter = useInRouterContext();
  const traceHref = '/inventory-traceability?referenceType=StockTransfer&referenceId=' + selected.id;
  const [returnReasons, setReturnReasons] = useState<Array<{ code: string; name: string }>>([]);
  const [returnCode, setReturnCode] = useState('');
  const [returnReason, setReturnReason] = useState('');
  const [returnReasonError, setReturnReasonError] = useState('');

  useEffect(() => {
    setReturnReasons([]);
    setReturnCode('');
    setReturnReason('');
    setReturnReasonError('');
    if (!canWrite || !canReturn || !['InTransit', 'Received', 'Completed'].includes(status) || !!selected.reverseTransferId) return;
    let active = true;
    void apiClient.get('/api/stock-transfers/return-reasons').then(response => {
      if (active) setReturnReasons(Array.isArray(response.data) ? response.data : []);
    }).catch(() => {
      if (active) setReturnReasonError('Không thể tải mã lý do; chức năng hoàn trả đã bị khóa an toàn.');
    });
    return () => { active = false; };
  }, [canWrite, canReturn, selected.id, selected.reverseTransferId, status]);

  return (
          <div className="transfer-modal" role="presentation">
            <div
              className="transfer-dialog detail"
              role="dialog"
              aria-modal="true"
              aria-labelledby="transfer-detail-title"
            >
              <div className="dialog-title">
                <div>
                  <h2 id="transfer-detail-title">{selected.code}</h2>
                  <UiBadge tone={statusTone(status)}>{statusLabel[status]}</UiBadge>
                </div>
                <button type="button" aria-label="Đóng chi tiết điều chuyển" onClick={onClose}>
                  <X aria-hidden="true" />
                </button>
              </div>

              <div className="timeline" aria-label="Tiến trình điều chuyển">
                {['Draft', 'Approved', 'InTransit', 'Received', 'Completed'].map((name, index) => (
                  <div
                    className={statusNames.indexOf(status) >= index && status !== 'Cancelled' && status !== 'Returned' ? 'done' : ''}
                    key={name}
                  >
                    <span>{index + 1}</span>
                    <small>{statusLabel[name]}</small>
                  </div>
                ))}
              </div>

              <p className="route">
                <strong>{selected.sourceWarehouseName}</strong>
                <ArrowRight aria-hidden="true" />
                <strong>{selected.destinationWarehouseName}</strong>
              </p>

              <UiTableScroll>
                <table aria-label={`Chi tiết điều chuyển ${selected.code}`}>
                  <thead>
                    <tr>
                      <th>Sản phẩm</th>
                      <th>Yêu cầu</th>
                      <th>Đã xuất</th>
                      <th>Thực nhận</th>
                      <th>Thiếu</th>
                      <th>Hỏng</th>
                    </tr>
                  </thead>
                  <tbody>
                    {selected.details.map(line => (
                      <tr key={line.productId}>
                        <td>{line.productCode} - {line.productName}</td>
                        <td>{line.requestedQuantity}</td>
                        <td>{line.dispatchedQuantity}</td>
                        {status === 'InTransit' ? (
                          <>
                            <td>
                              <input
                                aria-label={`Thực nhận ${line.productCode}`}
                                type="number"
                                min="0"
                                step="0.0001"
                                value={receive[line.productId]?.receivedQuantity ?? 0}
                                onChange={event =>
                                  setReceive({
                                    ...receive,
                                    [line.productId]: {
                                      ...receive[line.productId],
                                      receivedQuantity: Number(event.target.value),
                                    },
                                  })
                                }
                              />
                            </td>
                            <td>
                              <input
                                aria-label={`Thiếu ${line.productCode}`}
                                type="number"
                                min="0"
                                step="0.0001"
                                value={receive[line.productId]?.missingQuantity ?? 0}
                                onChange={event =>
                                  setReceive({
                                    ...receive,
                                    [line.productId]: {
                                      ...receive[line.productId],
                                      missingQuantity: Number(event.target.value),
                                    },
                                  })
                                }
                              />
                            </td>
                            <td>
                              <input
                                aria-label={`Hỏng ${line.productCode}`}
                                type="number"
                                min="0"
                                step="0.0001"
                                value={receive[line.productId]?.damagedQuantity ?? 0}
                                onChange={event =>
                                  setReceive({
                                    ...receive,
                                    [line.productId]: {
                                      ...receive[line.productId],
                                      damagedQuantity: Number(event.target.value),
                                    },
                                  })
                                }
                              />
                            </td>
                          </>
                        ) : (
                          <>
                            <td>{line.receivedQuantity}</td>
                            <td>{line.missingQuantity}</td>
                            <td>{line.damagedQuantity}</td>
                          </>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>

              {canWrite && canReturn && status === 'InTransit' && (
                <section className="transfer-return-form" aria-label="Hoàn trả điều chuyển">
                  <h3>Hoàn trả về kho nguồn</h3>
                  <p>Chỉ dùng khi hàng chưa được kho đích nhận. Giao dịch xuất kho gốc được giữ nguyên để truy vết.</p>
                  {returnReasonError && <p role="alert">{returnReasonError}</p>}
                  <label htmlFor="transfer-return-code">Mã lý do hoàn trả
                    <select id="transfer-return-code" value={returnCode} onChange={event => setReturnCode(event.target.value)}>
                      <option value="">Chọn mã lý do</option>
                      {returnReasons.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}
                    </select>
                  </label>
                  <label htmlFor="transfer-return-note">Diễn giải hoàn trả
                    <textarea id="transfer-return-note" maxLength={400} value={returnReason} onChange={event => setReturnReason(event.target.value)} />
                  </label>
                  <button type="button" className="danger" disabled={actionInFlight || !returnReasons.some(item => item.code === returnCode) || !returnReason.trim()}
                    onClick={() => void onAction('return', { reasonCode: returnCode, reason: returnReason.trim() })}>
                    Hoàn trả kho nguồn
                  </button>
                </section>
              )}
              {selected.reverseOfTransferId && (
                <p role="status" className="transfer-return-result">
                  Phiếu điều chuyển ngược từ chứng từ #{selected.reverseOfTransferId}.
                  {selected.reverseReasonCode && <strong> Mã lý do: {selected.reverseReasonCode}.</strong>}
                  {selected.reverseReason && <> {selected.reverseReason}</>}
                </p>
              )}
              {selected.reverseTransferId && (
                <p role="status" className="transfer-return-result">
                  Đã lập phiếu điều chuyển ngược #{selected.reverseTransferId}.
                  <button type="button" disabled={actionInFlight}
                    onClick={() => void onOpenTransfer(selected.reverseTransferId!)}>
                    Xem phiếu điều chuyển ngược
                  </button>
                </p>
              )}
              {canWrite && canReturn && ['Received', 'Completed'].includes(status) &&
                !selected.reverseOfTransferId && !selected.reverseTransferId && (
                <section className="transfer-return-form" aria-label="Lập điều chuyển ngược">
                  <h3>Lập phiếu điều chuyển ngược</h3>
                  <p>Hàng đã nhận phải trả qua phiếu mới, duyệt và xuất/nhận kho như bình thường. Sổ cái cũ được giữ nguyên.</p>
                  {returnReasonError && <p role="alert">{returnReasonError}</p>}
                  <label htmlFor="transfer-reverse-code">Mã lý do điều chuyển ngược
                    <select id="transfer-reverse-code" value={returnCode} onChange={event => setReturnCode(event.target.value)}>
                      <option value="">Chọn mã lý do</option>
                      {returnReasons.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}
                    </select>
                  </label>
                  <label htmlFor="transfer-reverse-note">Diễn giải điều chuyển ngược
                    <textarea id="transfer-reverse-note" maxLength={400} value={returnReason}
                      onChange={event => setReturnReason(event.target.value)} />
                  </label>
                  <button type="button" disabled={actionInFlight ||
                    !returnReasons.some(item => item.code === returnCode) || !returnReason.trim()}
                    onClick={() => void onAction('reverse-draft', { reasonCode: returnCode, reason: returnReason.trim() })}>
                    Tạo phiếu điều chuyển ngược
                  </button>
                </section>
              )}
              {status === 'Returned' && (
                <p role="status" className="transfer-return-result">
                  Đã hoàn trả về kho nguồn. {selected.returnReasonCode && <strong>Mã lý do: {selected.returnReasonCode}. </strong>}
                  {selected.returnReason}
                </p>
              )}
              {status === 'Returned' && canTrace && (
                <p className="transfer-return-result">
                  {inRouter ? <Link to={traceHref} onClick={onClose}>Truy vết sổ cái hoàn trả</Link>
                    : <a href={traceHref}>Truy vết sổ cái hoàn trả</a>}
                </p>
              )}
              <div className="dialog-actions">
                {canWrite && ['Draft', 'Approved'].includes(status) && (
                  <button type="button" disabled={actionInFlight} className="danger" onClick={() => void onAction('cancel')}>
                    Hủy phiếu
                  </button>
                )}
                {canApprove && selected.createdBy !== userId && status === 'Draft' && (
                  <button type="button" disabled={actionInFlight} onClick={() => void onAction('approve')}>
                    <Check size={17} aria-hidden="true" /> Duyệt
                  </button>
                )}
                {canWrite && status === 'Approved' && (
                  <button type="button" disabled={actionInFlight} onClick={() => void onAction('dispatch')}>
                    <Send size={17} aria-hidden="true" /> Xuất kho
                  </button>
                )}
                {canWrite && status === 'InTransit' && (
                  <button type="button" disabled={actionInFlight} onClick={() => void onReceive()}>
                    <PackageCheck size={17} aria-hidden="true" /> Xác nhận nhận
                  </button>
                )}
                {canWrite && status === 'Received' && (
                  <button type="button" disabled={actionInFlight} className="ui-primary-button" onClick={() => void onAction('complete')}>
                    Hoàn tất
                  </button>
                )}
              </div>
            </div>
          </div>
  );
}
