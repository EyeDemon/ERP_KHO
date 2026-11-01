import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { UiCard, UiPage, UiPageHeader } from '../ui/ProductionUi';
import {
  buildClientVietQrPayload,
  buildPlanDowngradeWarning,
  extractErrorText,
  formatDateVi,
  formatVietQrTransferContent,
  normalizeTransferReference,
  parseBillingSubscriptionResponse,
  resolveInvoiceStatusBadge,
  resolveNextBillingCycle,
  resolveQuotaTierLabel,
  resolveSubscriptionStatusLabel,
  SAAS_PLAN_CATALOG,
  type BillingState,
  type InvoiceRecord,
} from '../modules/governance/SaasBillingHelpers';
import { SaasBillingSummaryAndQuotas } from '../modules/governance/SaasBillingSummaryAndQuotas';
import { SaasBillingVietQrPanel } from '../modules/governance/SaasBillingVietQrPanel';
import {
  SaasBillingInvoiceHistoryCard,
  SaasBillingPlanCardsGrid,
} from '../modules/governance/SaasBillingPlanCardsAndHistory';

export {
  normalizeTransferReference,
  formatVietQrTransferContent,
  resolveQuotaTierLabel,
  SAAS_PLAN_CATALOG,
  buildPlanDowngradeWarning,
};

const SaasBilling = () => {
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [checkingPayment, setCheckingPayment] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [copiedField, setCopiedField] = useState<'account' | 'amount' | 'content' | null>(null);
  const [editingTaxCode, setEditingTaxCode] = useState(false);
  const [taxCodeDraft, setTaxCodeDraft] = useState('0318889999');
  const [selectedPlanCode, setSelectedPlanCode] = useState<'STARTER' | 'GROWTH' | 'ENTERPRISE'>('GROWTH');
  const [billing, setBilling] = useState<BillingState>({
    planCode: 'GROWTH',
    planName: 'Gói Tăng trưởng',
    monthlyPriceVnd: 1490000,
    subscriptionStatus: 'ACTIVE',
    activeWarehouses: 0,
    maxWarehouses: 3,
    activeUsers: 0,
    maxUsers: 20,
    activeProducts: 0,
    maxProducts: 20000,
    currentMonthOrderCount: 0,
    monthlyOrderQuota: 10000,
    currentPeriodStartUtc: new Date(Date.now() - 10 * 86400000).toISOString(),
    currentPeriodEndUtc: new Date(Date.now() + 20 * 86400000).toISOString(),
    nextRenewalDateUtc: new Date(Date.now() + 20 * 86400000).toISOString(),
    taxCode: '0318889999',
    bankName: 'TPBank',
    bankBin: '970423',
    accountNo: '98418102005',
    accountName: 'HUYNH PHI VAN',
    recentInvoices: [],
  });

  const [vietQrInvoice, setVietQrInvoice] = useState({
    invoiceCode: 'INV-SAAS-202611-002',
    planCode: 'GROWTH',
    amountVnd: 1490000,
    status: 'UNPAID',
    qrPayload: buildClientVietQrPayload(1490000, 'INV-SAAS-202611-002'),
    transferContent: 'ERPKHOINVSAAS202611002',
    expiresAtUtc: new Date(Date.now() + 30 * 60000).toISOString(),
    gatewayStatus: 'Đang chờ thanh toán hóa đơn INV-SAAS-202611-002 • Hệ thống tự động kích hoạt gói ngay khi nhận chuyển khoản.',
  });

  const [remainingSeconds, setRemainingSeconds] = useState<number>(1800);
  const [autoRefreshQr, setAutoRefreshQr] = useState<boolean>(false);

  useEffect(() => {
    const computeRemaining = () => {
      if (vietQrInvoice.status.toUpperCase() !== 'UNPAID') return 0;
      const expMs = new Date(vietQrInvoice.expiresAtUtc).getTime();
      if (Number.isNaN(expMs)) return 1800;
      return Math.max(0, Math.floor((expMs - Date.now()) / 1000));
    };

    const initial = computeRemaining();
    setRemainingSeconds(initial);
    if (vietQrInvoice.status.toUpperCase() !== 'UNPAID') return undefined;

    const timer = window.setInterval(() => {
      const next = computeRemaining();
      setRemainingSeconds(next);
      if (next <= 0) {
        setVietQrInvoice((prev) =>
          prev.status.toUpperCase() === 'UNPAID'
            ? {
                ...prev,
                status: 'EXPIRED',
                gatewayStatus: `Hóa đơn ${prev.invoiceCode} đã hết thời hạn 30 phút (00:00) • Vui lòng bấm Tạo mã VietQR mới.`,
              }
            : prev,
        );
      }
    }, 1000);

    return () => window.clearInterval(timer);
  }, [vietQrInvoice.expiresAtUtc, vietQrInvoice.status]);

  const applyBillingData = (bd: Record<string, unknown>) => {
    const parsed = parseBillingSubscriptionResponse(bd);
    setSelectedPlanCode(parsed.activeTargetPlan);
    setTaxCodeDraft(parsed.resolvedTaxCode);
    setBilling(parsed.billingState);
    setVietQrInvoice(parsed.vietQrState);
  };

  const loadSubscription = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient
        .get<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing')
        .catch(() => apiClient.get<Record<string, unknown>>('/api/v1/saas-billing/subscription'));
      if (res?.data) applyBillingData(res.data);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể tải thông tin gói dịch vụ SaaS.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadSubscription();
  }, [loadSubscription]);

  const selectedPlanSpec = SAAS_PLAN_CATALOG.find((p) => p.code === selectedPlanCode) ?? SAAS_PLAN_CATALOG[2];
  const selectedPlanLabel = selectedPlanSpec.title;
  const nextCycle = resolveNextBillingCycle(billing.currentPeriodEndUtc);
  const currentCycleText = `${formatDateVi(billing.currentPeriodStartUtc)} – ${formatDateVi(billing.currentPeriodEndUtc)}`;

  const handleSelectPlan = (targetPlan: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => {
    const targetSpec = SAAS_PLAN_CATALOG.find((p) => p.code === targetPlan);
    const warning = targetSpec ? buildPlanDowngradeWarning(targetSpec, billing) : null;
    if (warning) {
      setError(warning);
      return;
    }
    setError('');
    setSelectedPlanCode(targetPlan);
    const matchingUnpaid = billing.recentInvoices.find(
      (inv) => inv.status.toUpperCase() === 'UNPAID' && inv.planCode.toUpperCase() === targetPlan,
    );
    const planPrice = targetSpec?.priceVnd ?? 1490000;
    if (matchingUnpaid) {
      setVietQrInvoice({
        invoiceCode: matchingUnpaid.invoiceCode,
        planCode: matchingUnpaid.planCode,
        amountVnd: matchingUnpaid.amountVnd,
        status: 'UNPAID',
        qrPayload: matchingUnpaid.qrPayload || buildClientVietQrPayload(matchingUnpaid.amountVnd, matchingUnpaid.invoiceCode, billing.bankBin, billing.accountNo),
        transferContent: matchingUnpaid.transferContent,
        expiresAtUtc: matchingUnpaid.expiresAtUtc || new Date(Date.now() + 30 * 60000).toISOString(),
        gatewayStatus: `Đang chờ thanh toán hóa đơn ${matchingUnpaid.invoiceCode} • Hệ thống tự động kích hoạt gói ngay khi nhận chuyển khoản.`,
      });
    } else {
      setVietQrInvoice({
        invoiceCode: '',
        planCode: targetPlan,
        amountVnd: planPrice,
        status: 'IDLE',
        qrPayload: '',
        transferContent: 'Chưa tạo lệnh thanh toán',
        expiresAtUtc: new Date(Date.now() + 30 * 60000).toISOString(),
        gatewayStatus: 'Chưa có hóa đơn chờ thanh toán cho gói này • Bấm nút tạo mã VietQR để khởi tạo hóa đơn mới.',
      });
    }
  };

  const saveTaxCode = () => {
    const cleanTax = taxCodeDraft.trim();
    if (!cleanTax) {
      setError('Vui lòng nhập mã số thuế doanh nghiệp hợp lệ.');
      return;
    }
    setError('');
    setBilling((prev) => ({ ...prev, taxCode: cleanTax }));
    setEditingTaxCode(false);
    setNotice(`Đã cập nhật mã số thuế xuất hóa đơn GTGT: ${cleanTax}.`);
  };

  const copyToClipboard = async (field: 'account' | 'amount' | 'content', text: string) => {
    try {
      if (navigator?.clipboard?.writeText) await navigator.clipboard.writeText(text);
    } catch {
      // Clipboard API may be unavailable in headless test runner
    }
    setCopiedField(field);
    setNotice(`Đã sao chép: ${text}`);
  };

  const createVietQrInvoice = async (overridePlanCode?: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => {
    if (busy) return;
    const targetPlan = overridePlanCode ?? selectedPlanCode;
    const targetSpec = SAAS_PLAN_CATALOG.find((p) => p.code === targetPlan);
    const warning = targetSpec ? buildPlanDowngradeWarning(targetSpec, billing) : null;
    if (warning) {
      setError(warning);
      return;
    }
    if (overridePlanCode) setSelectedPlanCode(overridePlanCode);
    setBusy(true);
    setError('');
    setNotice('');
    const planPriceMap: Record<'STARTER' | 'GROWTH' | 'ENTERPRISE', number> = { STARTER: 490000, GROWTH: 1490000, ENTERPRISE: 3490000 };
    const targetLabel = targetPlan === 'STARTER' ? 'Gói Khởi đầu' : targetPlan === 'ENTERPRISE' ? 'Gói Doanh nghiệp' : 'Gói Tăng trưởng';
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice', {
        planCode: targetPlan,
        amountVnd: planPriceMap[targetPlan],
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const invoiceCode = String(d.invoiceCode ?? `INV-VQR-${targetPlan}`);
      const amountVnd = Number(d.amountVnd ?? planPriceMap[targetPlan]);
      const qrPayload = String(d.qrPayload ?? d.vietQrPayload ?? buildClientVietQrPayload(amountVnd, invoiceCode, billing.bankBin, billing.accountNo));
      const rawTransfer = String(d.transferContent ?? d.transferReference ?? invoiceCode);
      const cleanTransferContent = formatVietQrTransferContent(rawTransfer, invoiceCode);
      const status = String(d.status ?? 'UNPAID').toUpperCase();
      const createdAtUtc = String(d.createdAtUtc ?? new Date().toISOString());
      const rawExpires = typeof d.expiresAtUtc === 'string' ? d.expiresAtUtc : null;
      const expMs = rawExpires ? new Date(rawExpires).getTime() : NaN;
      const expiresAtUtc =
        Number.isNaN(expMs) || expMs <= Date.now()
          ? new Date(Date.now() + 30 * 60000).toISOString()
          : rawExpires!;

      setVietQrInvoice({
        invoiceCode,
        planCode: targetPlan,
        amountVnd,
        status,
        qrPayload,
        transferContent: cleanTransferContent,
        expiresAtUtc,
        gatewayStatus: `Đang chờ thanh toán hóa đơn ${invoiceCode} • Hệ thống tự động kích hoạt gói ngay khi nhận chuyển khoản.`,
      });

      setBilling((prev) => {
        const exists = prev.recentInvoices.some((inv) => inv.invoiceCode === invoiceCode);
        const nextInvoice: InvoiceRecord = {
          invoiceCode,
          planCode: targetPlan,
          planName: targetLabel,
          amountVnd,
          paymentProvider: 'VIETQR',
          status,
          qrPayload,
          transferContent: cleanTransferContent,
          createdAtUtc,
          paidAtUtc: null,
          expiresAtUtc,
        };
        return {
          ...prev,
          recentInvoices: exists ? prev.recentInvoices.map((inv) => (inv.invoiceCode === invoiceCode ? nextInvoice : inv)) : [nextInvoice, ...prev.recentInvoices],
        };
      });

      setNotice(`Đã khởi tạo mã thanh toán VietQR tự động cho ${targetLabel} (Nội dung CK: ${cleanTransferContent}).`);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể khởi tạo hóa đơn thanh toán VietQR.'));
    } finally {
      setBusy(false);
    }
  };

  const checkPaymentStatus = async () => {
    if (busy || checkingPayment) return;
    setBusy(true);
    setCheckingPayment(true);
    setError('');
    setNotice('');
    const checkedInvoiceCode = vietQrInvoice.invoiceCode || 'hiện tại';
    try {
      const res = await apiClient
        .get<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing')
        .catch(() => apiClient.get<Record<string, unknown>>('/api/v1/saas-billing/subscription'));
      if (res?.data) {
        applyBillingData(res.data);
        const rawStatus = String(res.data.subscriptionStatus ?? res.data.status ?? 'ACTIVE');
        const statusLabel = resolveSubscriptionStatusLabel(rawStatus);
        const rawInvoices = Array.isArray(res.data.recentInvoices) ? (res.data.recentInvoices as Array<Record<string, unknown>>) : [];
        const matchedPaid = rawInvoices.some(
          (inv) =>
            String(inv.invoiceCode ?? '') === checkedInvoiceCode &&
            String(inv.status ?? '').toUpperCase() === 'PAID',
        );
        if (matchedPaid) {
          setVietQrInvoice((prev) => ({
            ...prev,
            status: 'PAID',
            gatewayStatus: `Trạng thái gói dịch vụ: ${statusLabel} (Đã kích hoạt tự động & mở khóa 100% hạn mức)`,
          }));
          setNotice(`Đã ghi nhận thanh toán thành công cho hóa đơn ${checkedInvoiceCode} • Gói hiện tại: ${selectedPlanLabel} (${statusLabel}).`);
        } else {
          setNotice(
            `Chưa ghi nhận giao dịch chuyển khoản cho hóa đơn ${checkedInvoiceCode}. Nếu bạn vừa chuyển khoản, vui lòng thử lại sau 30 giây. (Đã kiểm tra trạng thái giao dịch từ máy chủ • Gói hiện tại: ${selectedPlanLabel} - ${statusLabel}).`,
          );
        }
      }
    } catch (err) {
      setError(extractErrorText(err, 'Không thể kiểm tra trạng thái giao dịch từ máy chủ.'));
    } finally {
      setBusy(false);
      setCheckingPayment(false);
    }
  };

  const confirmVietQrWebhook = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing/webhook-vietqr', {
        invoiceCode: vietQrInvoice.invoiceCode || undefined,
        planCode: selectedPlanCode,
        paidAmountVnd: selectedPlanCode === 'STARTER' ? 490000 : selectedPlanCode === 'ENTERPRISE' ? 3490000 : 1490000,
        bankTransactionRef: `FT${Date.now()}`,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const statusLabel = resolveSubscriptionStatusLabel(String(d.subscriptionStatus ?? 'ACTIVE'));
      setVietQrInvoice((prev) => ({
        ...prev,
        status: 'PAID',
        gatewayStatus: `Trạng thái gói dịch vụ: ${statusLabel} (Đã kích hoạt tự động & mở khóa 100% hạn mức)`,
      }));
      setNotice(typeof d.message === 'string' && d.message ? d.message : `Đã xác nhận thanh toán tự động VietQR cho ${selectedPlanLabel} • Trạng thái: ${statusLabel}.`);
      await loadSubscription();
      setVietQrInvoice((prev) => ({ ...prev, status: 'PAID' }));
    } catch (err) {
      setError(extractErrorText(err, 'Không thể xác nhận thanh toán tự động qua Webhook.'));
    } finally {
      setBusy(false);
    }
  };

  const orderTier = resolveQuotaTierLabel(billing.currentMonthOrderCount, billing.monthlyOrderQuota);
  const whTier = resolveQuotaTierLabel(billing.activeWarehouses, billing.maxWarehouses);
  const userTier = resolveQuotaTierLabel(billing.activeUsers, billing.maxUsers);
  const skuTier = resolveQuotaTierLabel(billing.activeProducts, billing.maxProducts);
  const visiblePlans = SAAS_PLAN_CATALOG.filter((plan) => plan.code !== 'FREE_TRIAL' || billing.planCode === 'FREE_TRIAL');
  const isUnpaidExpired = vietQrInvoice.status.toUpperCase() === 'UNPAID' && remainingSeconds <= 0;
  const isInvoiceExpired = vietQrInvoice.status.toUpperCase() === 'EXPIRED' || isUnpaidExpired;
  const isInvoicePaid = vietQrInvoice.status.toUpperCase() === 'PAID';

  useEffect(() => {
    if (autoRefreshQr && isInvoiceExpired && !busy && !isInvoicePaid) {
      void createVietQrInvoice();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefreshQr, isInvoiceExpired, busy, isInvoicePaid]);
  const activeInvoiceBadge = resolveInvoiceStatusBadge(vietQrInvoice.status, isUnpaidExpired);
  const displayAmountVnd = vietQrInvoice.invoiceCode ? vietQrInvoice.amountVnd : selectedPlanSpec.priceVnd;
  const showQaWebhookSimulator = Boolean(import.meta.env.DEV) && import.meta.env.VITE_QA_WEBHOOK_SIMULATOR === 'true';
  const hasActiveMatchingInvoice =
    vietQrInvoice.status.toUpperCase() === 'UNPAID' &&
    !isInvoiceExpired &&
    Boolean(vietQrInvoice.invoiceCode) &&
    vietQrInvoice.planCode.toUpperCase() === selectedPlanCode;

  const quotaGaugeItems = [
    { key: 'orders', title: 'Sản lượng đơn hàng tháng', unit: 'đơn', used: billing.currentMonthOrderCount, max: billing.monthlyOrderQuota, tier: orderTier },
    { key: 'warehouses', title: 'Kho đang hoạt động', unit: 'kho', used: billing.activeWarehouses, max: billing.maxWarehouses, tier: whTier },
    { key: 'users', title: 'Tài khoản người dùng', unit: 'người dùng', used: billing.activeUsers, max: billing.maxUsers, tier: userTier },
    { key: 'products', title: 'Danh mục mặt hàng', unit: 'mặt hàng', used: billing.activeProducts, max: billing.maxProducts, tier: skuTier },
  ];

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Quản trị & Gói dịch vụ SaaS"
        title="Quản trị gói dịch vụ SaaS, hạn mức 3 mức & thanh toán tự động VietQR"
        description="Theo dõi chu kỳ cước, hạn mức vận hành theo thời gian thực và thanh toán nâng cấp tự động qua chuyển khoản Napas247 VietQR."
      />
      {error && <div role="alert">{error}</div>}
      {loading && <p role="status">Đang tải thông tin gói dịch vụ SaaS...</p>}

      <SaasBillingSummaryAndQuotas
        billing={billing}
        busy={busy}
        editingTaxCode={editingTaxCode}
        setEditingTaxCode={setEditingTaxCode}
        taxCodeDraft={taxCodeDraft}
        setTaxCodeDraft={setTaxCodeDraft}
        saveTaxCode={saveTaxCode}
        loadSubscription={loadSubscription}
        quotaGaugeItems={quotaGaugeItems}
        createVietQrInvoice={createVietQrInvoice}
      />

      <UiCard
        title="Bảng gói cước thuê bao SaaS & thanh toán gia hạn VietQR"
        subtitle="Chọn gói dịch vụ phù hợp quy mô doanh nghiệp và quét mã VietQR để hệ thống tự động đối soát, mở khóa hạn mức tức thì"
      >
        <div className="ui-stack" style={{ gap: 16 }}>
          <SaasBillingPlanCardsGrid
            visiblePlans={visiblePlans}
            billing={billing}
            selectedPlanCode={selectedPlanCode}
            whTierPct={whTier.pct}
            busy={busy}
            handleSelectPlan={handleSelectPlan}
          />
          <SaasBillingVietQrPanel
            billing={billing}
            busy={busy}
            checkingPayment={checkingPayment}
            selectedPlanLabel={selectedPlanLabel}
            nextCycle={nextCycle}
            activeInvoiceBadge={activeInvoiceBadge}
            isInvoicePaid={isInvoicePaid}
            isInvoiceExpired={isInvoiceExpired}
            remainingSeconds={remainingSeconds}
            displayAmountVnd={displayAmountVnd}
            copiedField={copiedField}
            copyToClipboard={copyToClipboard}
            vietQrInvoice={vietQrInvoice}
            hasActiveMatchingInvoice={hasActiveMatchingInvoice}
            autoRefreshQr={autoRefreshQr}
            setAutoRefreshQr={setAutoRefreshQr}
            showQaWebhookSimulator={showQaWebhookSimulator}
            notice={notice}
            checkPaymentStatus={checkPaymentStatus}
            createVietQrInvoice={createVietQrInvoice}
            confirmVietQrWebhook={confirmVietQrWebhook}
          />
        </div>
      </UiCard>

      <SaasBillingInvoiceHistoryCard billing={billing} currentCycleText={currentCycleText} nextCycle={nextCycle} />
    </UiPage>
  );
};

export default SaasBilling;
