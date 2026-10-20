import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';

export interface PartnerDebtSummary {
  totalPayableBalance: number;
  totalReceivableBalance: number;
  openApInvoiceCount: number;
  openArInvoiceCount: number;
  overdueInvoiceCount: number;
}

export interface PartnerDebtInvoice {
  id: number;
  code: string;
  partnerId: number;
  partnerCode: string;
  partnerName: string;
  debtType: 'AP' | 'AR' | string;
  sourceType: string;
  sourceId: number;
  sourceCode: string;
  currency: string;
  subTotalAmount: number;
  taxAmount: number;
  totalAmount: number;
  paidAmount: number;
  remainingAmount: number;
  status: 'Open' | 'PartiallyPaid' | 'Paid' | 'Cancelled' | string;
  issuedAtUtc: string;
  dueDateUtc?: string | null;
  note?: string | null;
  rowVersion: string;
}

export interface PartnerDebtLedger {
  id: number;
  partnerId: number;
  partnerCode: string;
  partnerName: string;
  invoiceId: number;
  invoiceCode: string;
  debtType: string;
  transactionType: string;
  amountDelta: number;
  balanceAfter: number;
  referenceType: string;
  referenceId?: number | null;
  referenceCode: string;
  paymentMethod?: string | null;
  note?: string | null;
  createdBy: number;
  createdAtUtc: string;
}

const STATUS_LABELS: Record<string, string> = {
  Open: 'Chưa thanh toán',
  PartiallyPaid: 'Thanh toán một phần',
  Paid: 'Đã tất toán',
  Cancelled: 'Đã hủy',
};

const SOURCE_TYPE_LABELS: Record<string, string> = {
  ExportReceipt: 'Phiếu xuất kho',
  ImportReceipt: 'Phiếu nhập kho',
  Shipment: 'Lô giao hàng',
  PurchaseOrder: 'Đơn mua hàng',
  SalesOrder: 'Đơn bán hàng',
  Payment: 'Chứng từ thanh toán',
  Manual: 'Bút toán thủ công',
  InventoryReversal: 'Đảo bút toán kho',
};

const TX_LABELS: Record<string, string> = {
  InvoiceCreated: 'Phát hành hóa đơn',
  InvoiceIssued: 'Phát hành hóa đơn',
  Payment: 'Thanh toán',
  Adjustment: 'Điều chỉnh / Đảo bút toán',
};

const PAYMENT_METHOD_LABELS: Record<string, string> = {
  BANK_TRANSFER: 'Chuyển khoản ngân hàng',
  CASH: 'Tiền mặt tại quỹ',
  OFFSET: 'Bù trừ công nợ hai chiều',
};

const formatSourceType = (sourceType?: string | null) =>
  (sourceType && SOURCE_TYPE_LABELS[sourceType]) || sourceType || '—';

const formatPaymentMethod = (method?: string | null) =>
  (method && PAYMENT_METHOD_LABELS[method]) || method || '';

const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  status === 'Paid' ? 'success' : status === 'PartiallyPaid' ? 'warning' : status === 'Cancelled' ? 'danger' : 'neutral';

const formatCurrency = (val: number, currency = 'VND') =>
  `${Number(val || 0).toLocaleString('vi-VN', { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ${currency}`;

const formatNumberCompact = (val: number) =>
  Number(val || 0).toLocaleString('vi-VN', { minimumFractionDigits: 0, maximumFractionDigits: 2 });

const renderNoteWithNowrapCodes = (note?: string | null) => {
  if (!note) return '—';
  const parts = note.split(/([A-Z]{2,4}-[A-Z0-9-]+)/g);
  return parts.map((part, idx) =>
    /^[A-Z]{2,4}-[A-Z0-9-]+$/.test(part) ? (
      <span key={idx} style={{ whiteSpace: 'nowrap' }}>{part}</span>
    ) : (
      part
    ),
  );
};

export default function PartnerDebts() {
  const canManage = usePermission('partner.update');
  const [summary, setSummary] = useState<PartnerDebtSummary>({
    totalPayableBalance: 0,
    totalReceivableBalance: 0,
    openApInvoiceCount: 0,
    openArInvoiceCount: 0,
    overdueInvoiceCount: 0,
  });
  const [invoices, setInvoices] = useState<PartnerDebtInvoice[]>([]);
  const [ledger, setLedger] = useState<PartnerDebtLedger[]>([]);
  const [debtTypeFilter, setDebtTypeFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [selectedInvoice, setSelectedInvoice] = useState<PartnerDebtInvoice | null>(null);
  const [payAmount, setPayAmount] = useState('');
  const [paymentMethod, setPaymentMethod] = useState('BANK_TRANSFER');
  const [referenceCode, setReferenceCode] = useState('');
  const [payNote, setPayNote] = useState('');
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const lockRef = useRef(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [sumRes, invRes, ledRes] = await Promise.all([
        apiClient.get<PartnerDebtSummary>('/api/partner-debts/summary'),
        apiClient.get<PartnerDebtInvoice[]>('/api/partner-debts/invoices', {
          params: {
            debtType: debtTypeFilter || undefined,
            status: statusFilter || undefined,
          },
        }),
        apiClient.get<PartnerDebtLedger[]>('/api/partner-debts/ledger', {
          params: {
            debtType: debtTypeFilter || undefined,
            invoiceId: selectedInvoice?.id || undefined,
          },
        }),
      ]);
      const nextInvoices = Array.isArray(invRes.data) ? invRes.data : [];
      setSummary(sumRes.data);
      setInvoices(nextInvoices);
      setLedger(Array.isArray(ledRes.data) ? ledRes.data : []);
      setSelectedInvoice(prev =>
        prev ? nextInvoices.find(x => x.id === prev.id || x.code === prev.code) ?? prev : null,
      );
    } catch (e: any) {
      setError(e?.response?.data?.message || 'Không thể tải dữ liệu công nợ đối tác (AP/AR).');
    } finally {
      setLoading(false);
    }
  }, [debtTypeFilter, statusFilter, selectedInvoice?.id]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const openPaymentModal = (inv: PartnerDebtInvoice) => {
    setSelectedInvoice(inv);
    setPayAmount(String(inv.remainingAmount));
    setPaymentMethod('BANK_TRANSFER');
    setReferenceCode(`PAY-${inv.code}`);
    setPayNote('');
    setError('');
    setMessage('');
  };

  const submitPayment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedInvoice || lockRef.current || submitting) return;
    const amt = Number(payAmount);
    if (!Number.isFinite(amt) || amt <= 0) {
      setError('Số tiền thanh toán phải lớn hơn 0.');
      return;
    }
    lockRef.current = true;
    setSubmitting(true);
    setError('');
    setMessage('');
    const actionKey = `partner-debt-pay-${selectedInvoice.id}-${amt}-${referenceCode || 'AUTO'}`;
    try {
      const res = await apiClient.post<PartnerDebtInvoice>(
        `/api/partner-debts/invoices/${selectedInvoice.id}/pay`,
        {
          amount: amt,
          paymentMethod,
          referenceCode: referenceCode.trim() || undefined,
          note: payNote.trim() || undefined,
          rowVersion: selectedInvoice.rowVersion,
        },
        { headers: idempotencyHeaders(actionKey) },
      );
      completeIdempotentAction(actionKey);
      setSelectedInvoice(res.data);
      setMessage(`Đã ghi nhận thanh toán ${formatCurrency(amt, selectedInvoice.currency)} cho hóa đơn ${selectedInvoice.code}.`);
      await loadData();
    } catch (e: any) {
      setError(e?.response?.data?.message || 'Không thể thanh toán hóa đơn công nợ.');
    } finally {
      lockRef.current = false;
      setSubmitting(false);
    }
  };

  const displayedLedger = selectedInvoice
    ? ledger.filter(
        entry => entry.invoiceId === selectedInvoice.id || entry.invoiceCode === selectedInvoice.code,
      )
    : ledger;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Tài chính & Đối tác"
        title="Công nợ Phải trả (AP) & Phải thu (AR)"
        description="Quản lý hóa đơn công nợ nhà cung cấp (từ Phiếu nhập đã chốt), công nợ khách hàng (từ Vận đơn/Phiếu xuất đã giao) và sổ phụ công nợ bất biến."
      />

      {error && <p role="alert">{error}</p>}
      {message && <p role="status" className="ui-success-text">{message}</p>}

      <UiCard title="Tổng quan Dư nợ Đối tác (Công nợ Phải trả & Phải thu)">
        <UiMetricGrid>
          <UiMetric value={formatCurrency(summary.totalPayableBalance)} label="Tổng nợ phải trả NCC" />
          <UiMetric value={formatCurrency(summary.totalReceivableBalance)} label="Tổng nợ phải thu KH" />
          <UiMetric value={summary.openApInvoiceCount} label="Hóa đơn AP đang mở" />
          <UiMetric value={summary.openArInvoiceCount} label="Hóa đơn AR đang mở" />
          <UiMetric value={summary.overdueInvoiceCount} label="Hóa đơn quá hạn thanh toán" />
        </UiMetricGrid>
      </UiCard>

      <UiToolbar>
        <UiToolbarField label="Loại công nợ">
          <select
            aria-label="Lọc loại công nợ"
            value={debtTypeFilter}
            onChange={e => {
              setDebtTypeFilter(e.target.value);
              setSelectedInvoice(null);
            }}
          >
            <option value="">Tất cả (AP & AR)</option>
            <option value="AP">Phải trả Nhà cung cấp (AP)</option>
            <option value="AR">Phải thu Khách hàng (AR)</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Trạng thái hóa đơn">
          <select
            aria-label="Lọc trạng thái hóa đơn"
            value={statusFilter}
            onChange={e => setStatusFilter(e.target.value)}
          >
            <option value="">Mọi trạng thái</option>
            <option value="Open">Chưa thanh toán</option>
            <option value="PartiallyPaid">Thanh toán một phần</option>
            <option value="Paid">Đã tất toán</option>
          </select>
        </UiToolbarField>
        {selectedInvoice && (
          <div className="ui-auto-actions">
            <button type="button" onClick={() => setSelectedInvoice(null)}>
              Bỏ lọc theo hóa đơn {selectedInvoice.code}
            </button>
          </div>
        )}
      </UiToolbar>

      {selectedInvoice && canManage && ['Open', 'PartiallyPaid'].includes(selectedInvoice.status) && (
        <UiCard title={`Tất toán / Thanh toán Hóa đơn ${selectedInvoice.code} (${selectedInvoice.debtType === 'AP' ? 'Phải trả NCC' : 'Phải thu KH'})`}>
          <form onSubmit={submitPayment} className="ui-form-grid" style={{ width: '100%', maxWidth: '100%', minWidth: 0 }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '10px 16px' }}>
              <div>
                <strong>Đối tác:</strong> {selectedInvoice.partnerCode} — {selectedInvoice.partnerName}
                <br />
                <small>Chứng từ gốc: {formatSourceType(selectedInvoice.sourceType)} #{selectedInvoice.sourceCode}</small>
              </div>
              <div>
                <strong>Tổng hóa đơn:</strong> {formatCurrency(selectedInvoice.totalAmount, selectedInvoice.currency)}
                <br />
                <small>Còn lại cần thanh toán: <strong>{formatCurrency(selectedInvoice.remainingAmount, selectedInvoice.currency)}</strong></small>
              </div>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '14px 16px', marginTop: 14, marginBottom: 16 }}>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
                <span>Số tiền thanh toán *</span>
                <input
                  aria-label="Số tiền thanh toán công nợ"
                  type="number"
                  min="0.01"
                  max={selectedInvoice.remainingAmount}
                  step="0.01"
                  required
                  value={payAmount}
                  onChange={e => setPayAmount(e.target.value)}
                  style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
                />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
                <span>Phương thức thanh toán</span>
                <select
                  aria-label="Phương thức thanh toán"
                  value={paymentMethod}
                  onChange={e => setPaymentMethod(e.target.value)}
                  style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
                >
                  <option value="BANK_TRANSFER">Chuyển khoản ngân hàng</option>
                  <option value="CASH">Tiền mặt tại quỹ</option>
                  <option value="OFFSET">Bù trừ công nợ hai chiều</option>
                </select>
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
                <span>Mã tham chiếu / Ủy nhiệm chi</span>
                <input
                  aria-label="Mã tham chiếu thanh toán"
                  value={referenceCode}
                  onChange={e => setReferenceCode(e.target.value)}
                  maxLength={80}
                  style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
                />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
                <span>Ghi chú</span>
                <input
                  aria-label="Ghi chú thanh toán"
                  value={payNote}
                  onChange={e => setPayNote(e.target.value)}
                  maxLength={300}
                  style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
                />
              </label>
            </div>
            <div className="ui-inline-actions">
              <button type="submit" className="ui-primary-button" disabled={submitting}>
                {submitting ? 'Đang ghi sổ...' : 'Xác nhận Thanh toán'}
              </button>
              <button type="button" disabled={submitting} onClick={() => setSelectedInvoice(null)}>
                Đóng
              </button>
            </div>
          </form>
        </UiCard>
      )}

      <UiCard title="Danh sách Hóa đơn Công nợ">
        {loading && invoices.length === 0 ? (
          <p role="status">Đang tải danh sách hóa đơn công nợ...</p>
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Danh sách hóa đơn công nợ" style={{ width: '100%' }}>
              <thead>
                <tr>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Mã hóa đơn</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Loại</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, minWidth: 115, maxWidth: 155 }}>Đối tác</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Chứng từ kho</th>
                  <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, textAlign: 'right' }}>Tổng cộng</th>
                  <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, textAlign: 'right' }}>Đã trả</th>
                  <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, textAlign: 'right' }}>Còn nợ</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Hạn TT</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Trạng thái</th>
                  <th
                    className="ui-sticky-action"
                    style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, position: 'sticky', right: 0, zIndex: 2 }}
                  >
                    Thao tác
                  </th>
                </tr>
              </thead>
              <tbody>
                {invoices.length === 0 ? (
                  <tr>
                    <td colSpan={10} className="ui-empty-cell">
                      Chưa có hóa đơn công nợ phù hợp với bộ lọc.
                    </td>
                  </tr>
                ) : (
                  invoices.map(inv => (
                    <tr key={inv.id}>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                        <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{inv.code}</strong>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 11.5 }}>
                        <UiBadge tone={inv.debtType === 'AP' ? 'warning' : 'success'}>
                          {inv.debtType}
                        </UiBadge>
                        <small style={{ display: 'block', fontSize: 10.5, marginTop: 2 }}>
                          {inv.debtType === 'AP' ? 'Phải trả' : 'Phải thu'}
                        </small>
                      </td>
                      <td
                        className="ui-cell-partner"
                        style={{ minWidth: 110, maxWidth: 145, padding: '6px 5px', fontSize: 12, overflowWrap: 'break-word', wordBreak: 'normal' }}
                      >
                        <div style={{ whiteSpace: 'nowrap', fontWeight: 600 }}>{inv.partnerCode}</div>
                        <small style={{ overflowWrap: 'break-word', wordBreak: 'normal', fontSize: 11, lineHeight: 1.25, display: 'block' }}>{inv.partnerName}</small>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                        <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{inv.sourceCode}</strong>
                        <small style={{ whiteSpace: 'nowrap', fontSize: 11, display: 'block' }}>{formatSourceType(inv.sourceType)}</small>
                      </td>
                      <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        <div><strong>{formatCurrency(inv.totalAmount, inv.currency)}</strong></div>
                        <div style={{ fontSize: 10, color: '#64748b', fontWeight: 400, whiteSpace: 'pre-line', lineHeight: 1.25 }}>
                          {`Trước thuế: ${formatNumberCompact(inv.subTotalAmount)}\n• VAT: ${formatNumberCompact(inv.taxAmount)}`}
                        </div>
                      </td>
                      <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        {formatCurrency(inv.paidAmount, inv.currency)}
                      </td>
                      <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        <strong>{formatCurrency(inv.remainingAmount, inv.currency)}</strong>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                        {inv.dueDateUtc ? new Date(inv.dueDateUtc).toLocaleDateString('vi-VN') : '—'}
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 11.5 }}>
                        <UiBadge tone={statusTone(inv.status)}>
                          {STATUS_LABELS[inv.status] ?? inv.status}
                        </UiBadge>
                      </td>
                      <td
                        className="ui-cell-nowrap ui-sticky-action"
                        style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, position: 'sticky', right: 0, background: '#ffffff', zIndex: 1 }}
                      >
                        <div className="ui-inline-actions" style={{ flexWrap: 'nowrap', gap: 4 }}>
                          <button type="button" style={{ padding: '4px 7px', minHeight: 28, fontSize: 11.5 }} onClick={() => openPaymentModal(inv)}>
                            Sổ phụ
                          </button>
                          {canManage && ['Open', 'PartiallyPaid'].includes(inv.status) && (
                            <button type="button" className="ui-primary-button" style={{ padding: '4px 7px', minHeight: 28, fontSize: 11.5 }} onClick={() => openPaymentModal(inv)}>
                              Thanh toán
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard
        title={
          selectedInvoice
            ? `Sổ phụ & Lịch sử Thanh toán — Hóa đơn ${selectedInvoice.code}`
            : 'Sổ phụ & Lịch sử Thanh toán (Sổ cái Công nợ Bất biến)'
        }
      >
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Sổ cái biến động công nợ đối tác">
            <thead>
              <tr>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Thời gian</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Loại</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px', minWidth: 130, maxWidth: 180 }}>Đối tác</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Hóa đơn</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Nghiệp vụ</th>
                <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', textAlign: 'right' }}>Biến động</th>
                <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', textAlign: 'right' }}>Dư nợ sau GD</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Tham chiếu</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px', minWidth: 120, maxWidth: 170 }}>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {displayedLedger.length === 0 ? (
                <tr>
                  <td colSpan={9} className="ui-empty-cell">
                    Chưa có bút toán sổ cái công nợ.
                  </td>
                </tr>
              ) : (
                displayedLedger.map(entry => (
                  <tr key={entry.id}>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>
                      {new Date(entry.createdAtUtc).toLocaleString('vi-VN')}
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5 }}>
                      <UiBadge>{entry.debtType}</UiBadge>
                    </td>
                    <td
                      className="ui-cell-partner"
                      style={{ minWidth: 130, maxWidth: 180, padding: '6px 8px', fontSize: 12.5, overflowWrap: 'break-word', wordBreak: 'normal' }}
                    >
                      {entry.partnerCode} — {entry.partnerName}
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>
                      <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{entry.invoiceCode}</strong>
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5 }}>
                      {TX_LABELS[entry.transactionType] ?? entry.transactionType}
                    </td>
                    <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <strong style={{ color: entry.amountDelta < 0 ? '#15803d' : '#b45309' }}>
                        {entry.amountDelta > 0 ? `+${entry.amountDelta.toLocaleString('vi-VN')}` : entry.amountDelta.toLocaleString('vi-VN')}
                      </strong>
                    </td>
                    <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <strong>{entry.balanceAfter.toLocaleString('vi-VN')}</strong>
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>
                      <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{entry.referenceCode || `#${entry.referenceId ?? ''}`}</strong>
                      <small style={{ whiteSpace: 'nowrap', fontSize: 11.5, display: 'block' }}>
                        {entry.paymentMethod ? formatPaymentMethod(entry.paymentMethod) : formatSourceType(entry.referenceType)}
                      </small>
                    </td>
                    <td style={{ minWidth: 120, maxWidth: 170, padding: '6px 8px', fontSize: 12.5, overflowWrap: 'break-word', wordBreak: 'normal' }}>
                      {renderNoteWithNowrapCodes(entry.note)}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </UiPage>
  );
}
