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

const TX_LABELS: Record<string, string> = {
  InvoiceCreated: 'Ghi nhận công nợ',
  InvoiceIssued: 'Ghi nhận công nợ',
  Payment: 'Thanh toán / Thu tiền',
  Adjustment: 'Điều chỉnh',
};

const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  status === 'Paid' ? 'success' : status === 'PartiallyPaid' ? 'warning' : status === 'Cancelled' ? 'danger' : 'neutral';

const formatCurrency = (val: number, currency = 'VND') =>
  `${Number(val || 0).toLocaleString('vi-VN', { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ${currency}`;

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
      setSummary(sumRes.data);
      setInvoices(Array.isArray(invRes.data) ? invRes.data : []);
      setLedger(Array.isArray(ledRes.data) ? ledRes.data : []);
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

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Tài chính & Đối tác"
        title="Công nợ Phải trả (AP) & Phải thu (AR)"
        description="Quản lý hóa đơn công nợ nhà cung cấp (AP từ Phiếu nhập đã chốt), công nợ khách hàng (AR từ Shipment/Phiếu xuất đã giao) và sổ phụ bất biến (PartnerDebtLedger)."
      />

      {error && <p role="alert">{error}</p>}
      {message && <p role="status" className="ui-success-text">{message}</p>}

      <UiCard title="Tổng quan Dư nợ Đối tác (AP / AR Financial Summary)">
        <UiMetricGrid>
          <UiMetric value={formatCurrency(summary.totalPayableBalance)} label="Tổng nợ phải trả NCC (AP Balance)" />
          <UiMetric value={formatCurrency(summary.totalReceivableBalance)} label="Tổng nợ phải thu KH (AR Balance)" />
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
        <UiCard title={`Tất toán / Thanh toán Hóa đơn ${selectedInvoice.code} (${selectedInvoice.debtType})`}>
          <form onSubmit={submitPayment} className="ui-form-grid">
            <div>
              <strong>Đối tác:</strong> {selectedInvoice.partnerCode} — {selectedInvoice.partnerName}
              <br />
              <small>Chứng từ gốc: {selectedInvoice.sourceType} #{selectedInvoice.sourceCode}</small>
            </div>
            <div>
              <strong>Tổng hóa đơn:</strong> {formatCurrency(selectedInvoice.totalAmount, selectedInvoice.currency)}
              <br />
              <small>Còn lại cần thanh toán: <strong>{formatCurrency(selectedInvoice.remainingAmount, selectedInvoice.currency)}</strong></small>
            </div>
            <label>
              Số tiền thanh toán *
              <input
                aria-label="Số tiền thanh toán công nợ"
                type="number"
                min="0.01"
                max={selectedInvoice.remainingAmount}
                step="0.01"
                required
                value={payAmount}
                onChange={e => setPayAmount(e.target.value)}
              />
            </label>
            <label>
              Phương thức thanh toán
              <select
                aria-label="Phương thức thanh toán"
                value={paymentMethod}
                onChange={e => setPaymentMethod(e.target.value)}
              >
                <option value="BANK_TRANSFER">Chuyển khoản ngân hàng (BANK_TRANSFER)</option>
                <option value="CASH">Tiền mặt (CASH)</option>
                <option value="OFFSET">Bù trừ công nợ (OFFSET)</option>
              </select>
            </label>
            <label>
              Mã tham chiếu / Ủy nhiệm chi
              <input
                aria-label="Mã tham chiếu thanh toán"
                value={referenceCode}
                onChange={e => setReferenceCode(e.target.value)}
                maxLength={80}
              />
            </label>
            <label>
              Ghi chú
              <input
                aria-label="Ghi chú thanh toán"
                value={payNote}
                onChange={e => setPayNote(e.target.value)}
                maxLength={300}
              />
            </label>
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

      <UiCard title="Danh sách Hóa đơn Công nợ (Partner Debt Invoices)">
        {loading ? (
          <p role="status">Đang tải danh sách hóa đơn công nợ...</p>
        ) : (
          <UiTableScroll>
            <table aria-label="Danh sách hóa đơn công nợ">
              <thead>
                <tr>
                  <th>Mã hóa đơn</th>
                  <th>Loại</th>
                  <th>Đối tác</th>
                  <th>Chứng từ kho</th>
                  <th>Trước thuế</th>
                  <th>Thuế</th>
                  <th>Tổng cộng</th>
                  <th>Đã trả</th>
                  <th>Còn nợ</th>
                  <th>Hạn TT</th>
                  <th>Trạng thái</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {invoices.length === 0 ? (
                  <tr>
                    <td colSpan={12} className="ui-empty-cell">
                      Chưa có hóa đơn công nợ phù hợp với bộ lọc.
                    </td>
                  </tr>
                ) : (
                  invoices.map(inv => (
                    <tr key={inv.id}>
                      <td><strong>{inv.code}</strong></td>
                      <td>
                        <UiBadge tone={inv.debtType === 'AP' ? 'warning' : 'success'}>
                          {inv.debtType === 'AP' ? 'AP (Phải trả)' : 'AR (Phải thu)'}
                        </UiBadge>
                      </td>
                      <td>
                        <div>{inv.partnerCode}</div>
                        <small>{inv.partnerName}</small>
                      </td>
                      <td>
                        <div>{inv.sourceCode}</div>
                        <small>{inv.sourceType}</small>
                      </td>
                      <td>{formatCurrency(inv.subTotalAmount, inv.currency)}</td>
                      <td>{formatCurrency(inv.taxAmount, inv.currency)}</td>
                      <td><strong>{formatCurrency(inv.totalAmount, inv.currency)}</strong></td>
                      <td>{formatCurrency(inv.paidAmount, inv.currency)}</td>
                      <td><strong>{formatCurrency(inv.remainingAmount, inv.currency)}</strong></td>
                      <td>{inv.dueDateUtc ? new Date(inv.dueDateUtc).toLocaleDateString('vi-VN') : '—'}</td>
                      <td>
                        <UiBadge tone={statusTone(inv.status)}>
                          {STATUS_LABELS[inv.status] ?? inv.status}
                        </UiBadge>
                      </td>
                      <td>
                        <div className="ui-inline-actions">
                          <button type="button" onClick={() => setSelectedInvoice(inv)}>
                            Sổ phụ
                          </button>
                          {canManage && ['Open', 'PartiallyPaid'].includes(inv.status) && (
                            <button type="button" className="ui-primary-button" onClick={() => openPaymentModal(inv)}>
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

      <UiCard title="Sổ cái Công nợ Bất biến (Append-Only PartnerDebtLedger)">
        <UiTableScroll>
          <table aria-label="Sổ cái biến động công nợ đối tác">
            <thead>
              <tr>
                <th>Thời gian</th>
                <th>Loại</th>
                <th>Đối tác</th>
                <th>Hóa đơn</th>
                <th>Nghiệp vụ</th>
                <th>Biến động</th>
                <th>Dư nợ sau GD</th>
                <th>Tham chiếu</th>
                <th>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {ledger.length === 0 ? (
                <tr>
                  <td colSpan={9} className="ui-empty-cell">
                    Chưa có bút toán sổ cái công nợ.
                  </td>
                </tr>
              ) : (
                ledger.map(entry => (
                  <tr key={entry.id}>
                    <td>{new Date(entry.createdAtUtc).toLocaleString('vi-VN')}</td>
                    <td><UiBadge>{entry.debtType}</UiBadge></td>
                    <td>{entry.partnerCode} — {entry.partnerName}</td>
                    <td><strong>{entry.invoiceCode}</strong></td>
                    <td>{TX_LABELS[entry.transactionType] ?? entry.transactionType}</td>
                    <td>
                      <strong style={{ color: entry.amountDelta < 0 ? '#15803d' : '#b45309' }}>
                        {entry.amountDelta > 0 ? `+${entry.amountDelta.toLocaleString('vi-VN')}` : entry.amountDelta.toLocaleString('vi-VN')}
                      </strong>
                    </td>
                    <td><strong>{entry.balanceAfter.toLocaleString('vi-VN')}</strong></td>
                    <td>
                      <div>{entry.referenceCode}</div>
                      {entry.paymentMethod && <small>{entry.paymentMethod}</small>}
                    </td>
                    <td>{entry.note || '—'}</td>
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
