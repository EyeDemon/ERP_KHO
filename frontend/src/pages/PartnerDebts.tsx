import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import {
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import {
  formatCurrency,
  mapRawStorageSnapshots,
  type PartnerDebtInvoice,
  type PartnerDebtLedger,
  PartnerDebtPaymentCard,
  PartnerDebtStorageSnapshotCard,
  type PartnerDebtSummary,
  type StorageOccupancySnapshotRow,
} from '../modules/catalog/PartnerDebtComponents';
import { PartnerDebtTablesSection } from '../modules/catalog/PartnerDebtTablesSection';

export type { PartnerDebtSummary, PartnerDebtInvoice, PartnerDebtLedger, StorageOccupancySnapshotRow };

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
  const [storageSnapshots, setStorageSnapshots] = useState<StorageOccupancySnapshotRow[]>([]);
  const [snapshotBusy, setSnapshotBusy] = useState(false);
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

  const loadStorageSnapshots = useCallback(async () => {
    try {
      const res = await apiClient.get('/api/v1/reports/3pl-storage-snapshot').catch(() => ({ data: [] }));
      const d = res?.data;
      const rawList = Array.isArray(d)
        ? d
        : Array.isArray(d?.items)
          ? d.items
          : Array.isArray(d?.snapshots)
            ? d.snapshots
            : Array.isArray(d?.owners)
              ? d.owners
              : [];
      setStorageSnapshots(mapRawStorageSnapshots(rawList));
    } catch {
      setStorageSnapshots([]);
    }
  }, []);

  const handleCapture3plSnapshot = async () => {
    if (snapshotBusy) return;
    setSnapshotBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/v1/reports/3pl-storage-snapshot', {
        billingUnitMode: 'PALLET_CBM_BIN_DAY',
        captureDateUtc: new Date().toISOString(),
      });
      const d = res?.data;
      const rawList = Array.isArray(d)
        ? d
        : Array.isArray(d?.items)
          ? d.items
          : Array.isArray(d?.snapshots)
            ? d.snapshots
            : Array.isArray(d?.owners)
              ? d.owners
              : d && typeof d === 'object' && ('ownerName' in d || 'equivalentPallets' in d)
                ? [d]
                : [];
      if (rawList.length > 0) {
        setStorageSnapshots(mapRawStorageSnapshots(rawList));
      } else {
        await loadStorageSnapshots();
      }
      setMessage('Đã chốt Số Dư Lưu Kho 3PL hàng ngày (Theo Pallet • Thể tích m³ • Vị trí kệ) và cập nhật phí lũy kế tháng.');
    } catch (e: any) {
      setError(e?.response?.data?.message || 'Không thể chốt Số Dư Lưu Kho 3PL hôm nay.');
    } finally {
      setSnapshotBusy(false);
    }
  };

  const loadData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [sumRes, invRes, ledRes] = await Promise.all([
        apiClient.get<PartnerDebtSummary>('/api/v1/partner-debts/summary'),
        apiClient.get<PartnerDebtInvoice[]>('/api/v1/partner-debts/invoices', {
          params: {
            debtType: debtTypeFilter || undefined,
            status: statusFilter || undefined,
          },
        }),
        apiClient.get<PartnerDebtLedger[]>('/api/v1/partner-debts/ledger', {
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
    void loadStorageSnapshots();
  }, [loadData, loadStorageSnapshots]);

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
        `/api/v1/partner-debts/invoices/${selectedInvoice.id}/pay`,
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
        description="Quản lý hóa đơn công nợ nhà cung cấp (từ Phiếu nhập đã chốt), công nợ khách hàng (từ Vận đơn/Phiếu xuất đã giao), đối soát phí lưu kho 3PL hàng ngày và sổ phụ công nợ bất biến."
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

      <PartnerDebtStorageSnapshotCard
        storageSnapshots={storageSnapshots}
        snapshotBusy={snapshotBusy}
        onCaptureSnapshot={handleCapture3plSnapshot}
        onReloadSnapshots={loadStorageSnapshots}
      />

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
        <PartnerDebtPaymentCard
          selectedInvoice={selectedInvoice}
          payAmount={payAmount}
          setPayAmount={setPayAmount}
          paymentMethod={paymentMethod}
          setPaymentMethod={setPaymentMethod}
          referenceCode={referenceCode}
          setReferenceCode={setReferenceCode}
          payNote={payNote}
          setPayNote={setPayNote}
          submitting={submitting}
          onSubmitPayment={submitPayment}
          onClose={() => setSelectedInvoice(null)}
        />
      )}

      <PartnerDebtTablesSection
        loading={loading}
        invoices={invoices}
        selectedInvoice={selectedInvoice}
        displayedLedger={displayedLedger}
        canManage={canManage}
        openPaymentModal={openPaymentModal}
      />
    </UiPage>
  );
}
