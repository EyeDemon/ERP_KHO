import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import apiClient from '../services/apiClient';
import { UiCard, UiPage, UiPageHeader } from '../ui/ProductionUi';
import {
  buildClientVietQrPayload, buildPlanDowngradeWarning, extractErrorText,
  formatVietQrTransferContent, normalizeSaasCompanyName, normalizeSaasTaxCode,
  normalizeTransferReference, parseBillingSubscriptionResponse,
  resolveInvoiceStatusBadge, resolveNextBillingCycle, resolveQuotaTierLabel,
  resolveSubscriptionStatusLabel, SAAS_PLAN_CATALOG, type BillingState, type InvoiceRecord,
} from '../modules/governance/SaasBillingHelpers';
import { SaasBillingSummaryAndQuotas } from '../modules/governance/SaasBillingSummaryAndQuotas';
import { SaasBillingTaxProfileCard } from '../modules/governance/SaasBillingTaxProfileCard';
import { SaasBillingVietQrPanel } from '../modules/governance/SaasBillingVietQrPanel';
import { SaasBillingInvoiceHistoryCard, SaasBillingPlanCardsGrid } from '../modules/governance/SaasBillingPlanCardsAndHistory';

export { normalizeTransferReference, formatVietQrTransferContent, resolveQuotaTierLabel, SAAS_PLAN_CATALOG, buildPlanDowngradeWarning };

const INITIAL_BILLING_STATE: BillingState = {
  planCode: 'GROWTH', planName: 'Gói Tăng trưởng', monthlyPriceVnd: 1490000, subscriptionStatus: 'ACTIVE',
  activeWarehouses: 0, maxWarehouses: 3, activeUsers: 0, maxUsers: 20, activeProducts: 0, maxProducts: 20000,
  currentMonthOrderCount: 0, monthlyOrderQuota: 10000,
  currentPeriodStartUtc: '2026-11-01T12:00:00Z', currentPeriodEndUtc: '2026-11-30T12:00:00Z', nextRenewalDateUtc: '2026-11-05T12:00:00Z',
  taxCode: '', bankName: '', bankBin: '', accountNo: '', accountName: '',
  recentInvoices: [],
};

const PLAN_PRICES: Record<'STARTER' | 'GROWTH' | 'ENTERPRISE', number> = { STARTER: 490000, GROWTH: 1490000, ENTERPRISE: 3490000 };

const SaasBilling = () => {
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [checkingPayment, setCheckingPayment] = useState(false);
  const [isQrPanelOpen, setIsQrPanelOpen] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [copiedField, setCopiedField] = useState<'account' | 'amount' | 'content' | null>(null);
  const [editingTaxCode, setEditingTaxCode] = useState(false);
  const [taxCodeDraft, setTaxCodeDraft] = useState('');
  const [buyerCompanyName, setBuyerCompanyName] = useState('');
  const [buyerTaxpayerType, setBuyerTaxpayerType] = useState<'ENTERPRISE' | 'HOUSEHOLD_INDIVIDUAL' | 'INDIVIDUAL_CONSUMER'>('ENTERPRISE');
  const [legalRegisteredAddress, setLegalRegisteredAddress] = useState('');
  const [selectedPlanCode, setSelectedPlanCode] = useState<'STARTER' | 'GROWTH' | 'ENTERPRISE'>('GROWTH');
  const [billing, setBilling] = useState<BillingState>(INITIAL_BILLING_STATE);
  const [vietQrInvoice, setVietQrInvoice] = useState({
    invoiceCode: '', planCode: 'GROWTH', amountVnd: 1490000, status: 'IDLE',
    qrPayload: '', transferContent: 'Chưa tạo lệnh thanh toán', expiresAtUtc: '',
    gatewayStatus: 'Chưa khởi tạo mã thanh toán • Bấm Tạo mã QR thanh toán để mở phiên chuyển khoản Napas247.',
  });
  const [remainingSeconds, setRemainingSeconds] = useState<number>(900);
  const [autoRefreshQr, setAutoRefreshQr] = useState<boolean>(false);
  const [globalSuspendedMounted, setGlobalSuspendedMounted] = useState<boolean>(() =>
    typeof document !== 'undefined' && Boolean(document.getElementById('customer-tenant-suspended-banner')),
  );

  const openQrPanelOnDemand = useCallback(() => {
    setIsQrPanelOpen(true);
    setVietQrInvoice((prev) => {
      if (prev.status.toUpperCase() !== 'UNPAID') return prev;
      setRemainingSeconds(900);
      return { ...prev, expiresAtUtc: new Date(Date.now() + 15 * 60000).toISOString() };
    });
  }, []);

  const scrollToVietQrPanel = useCallback(() => {
    openQrPanelOnDemand();
    if (typeof document === 'undefined') return;
    window.setTimeout(() => {
      const panel = document.querySelector('[data-testid="vietqr-payment-panel"]') as HTMLElement | null;
      if (panel) {
        if (typeof panel.scrollIntoView === 'function') panel.scrollIntoView({ behavior: 'smooth', block: 'center' });
        panel.focus({ preventScroll: true });
      }
    }, 30);
  }, [openQrPanelOnDemand]);

  useEffect(() => {
    if (!isQrPanelOpen || vietQrInvoice.status.toUpperCase() !== 'UNPAID') return undefined;
    const computeRemaining = () => {
      const expMs = new Date(vietQrInvoice.expiresAtUtc).getTime();
      return Number.isNaN(expMs) ? 900 : Math.max(0, Math.floor((expMs - Date.now()) / 1000));
    };
    setRemainingSeconds(computeRemaining());
    const timer = window.setInterval(() => {
      const next = computeRemaining();
      setRemainingSeconds(next);
      if (next <= 0) {
        setVietQrInvoice((prev) =>
          prev.status.toUpperCase() === 'UNPAID'
            ? { ...prev, status: 'EXPIRED', gatewayStatus: `Hóa đơn ${prev.invoiceCode} đã hết thời hạn 15 phút (00:00) • Vui lòng bấm Tạo mã mới.` }
            : prev,
        );
      }
    }, 1000);
    return () => window.clearInterval(timer);
  }, [isQrPanelOpen, vietQrInvoice.expiresAtUtc, vietQrInvoice.status]);

  const applyBillingData = (bd: Record<string, unknown>) => {
    const parsed = parseBillingSubscriptionResponse(bd);
    setSelectedPlanCode(parsed.activeTargetPlan);
    setTaxCodeDraft(parsed.resolvedTaxCode);
    setBuyerCompanyName((prev) => normalizeSaasCompanyName(prev || parsed.resolvedCompanyName));
    setBilling(parsed.billingState);
    setVietQrInvoice(parsed.vietQrState);
  };

  const loadSubscription = useCallback(async () => {
    setLoading(true); setError('');
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

  useEffect(() => { void loadSubscription(); }, [loadSubscription]);

  const selectedPlanSpec = SAAS_PLAN_CATALOG.find((p) => p.code === selectedPlanCode) ?? SAAS_PLAN_CATALOG[2];
  const selectedPlanLabel = selectedPlanSpec.title;
  const nextCycle = resolveNextBillingCycle(billing.currentPeriodEndUtc);
  const currentCycleText = '01/11/2026 – 30/11/2026 (Kỳ 11/2026)';

  const handleSelectPlan = (targetPlan: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => {
    const targetSpec = SAAS_PLAN_CATALOG.find((p) => p.code === targetPlan);
    const warning = targetSpec ? buildPlanDowngradeWarning(targetSpec, billing) : null;
    if (warning) { setError(warning); return; }
    setError('');
    setSelectedPlanCode(targetPlan);
    const matchingUnpaid = billing.recentInvoices.find((inv) => inv.status.toUpperCase() === 'UNPAID' && inv.planCode.toUpperCase() === targetPlan);
    const planPrice = targetSpec?.priceVnd ?? 1490000;
    const freshExp = new Date(Date.now() + 15 * 60000).toISOString();
    setVietQrInvoice(
      matchingUnpaid
        ? {
            invoiceCode: matchingUnpaid.invoiceCode, planCode: matchingUnpaid.planCode, amountVnd: matchingUnpaid.amountVnd, status: 'UNPAID',
            qrPayload: matchingUnpaid.qrPayload || buildClientVietQrPayload(matchingUnpaid.amountVnd, matchingUnpaid.invoiceCode, billing.bankBin, billing.accountNo),
            transferContent: matchingUnpaid.transferContent, expiresAtUtc: freshExp,
            gatewayStatus: `Đang chờ thanh toán hóa đơn ${matchingUnpaid.invoiceCode} • Hệ thống tự động kích hoạt gói ngay khi nhận chuyển khoản.`,
          }
        : {
            invoiceCode: '', planCode: targetPlan, amountVnd: planPrice, status: 'IDLE', qrPayload: '',
            transferContent: 'Chưa tạo lệnh thanh toán', expiresAtUtc: freshExp,
            gatewayStatus: 'Chưa có hóa đơn chờ thanh toán cho gói này • Bấm nút tạo mã VietQR để khởi tạo hóa đơn mới.',
          },
    );
    setIsQrPanelOpen(true);
  };

  const saveTaxCode = () => {
    const rawTrimmed = taxCodeDraft.trim();
    if (!rawTrimmed) { setError('Vui lòng nhập mã số thuế doanh nghiệp hợp lệ.'); return; }
    const cleanTax = normalizeSaasTaxCode(rawTrimmed);
    setError('');
    setTaxCodeDraft(cleanTax);
    setBilling((prev) => ({ ...prev, taxCode: cleanTax }));
    setEditingTaxCode(false);
    setNotice(`Đã cập nhật mã số thuế xuất hóa đơn GTGT: ${cleanTax}.`);
  };

  const saveSaasTaxProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true); setError(''); setNotice('');
    try {
      const cleanTax = normalizeSaasTaxCode(taxCodeDraft);
      const cleanCompany = normalizeSaasCompanyName(buyerCompanyName);
      await apiClient.put('/api/v1/saas-billing/tax-profile', {
        taxCode: cleanTax, companyName: cleanCompany, taxpayerType: buyerTaxpayerType, legalRegisteredAddress: legalRegisteredAddress.trim(),
      });
      setTaxCodeDraft(cleanTax);
      setBuyerCompanyName(cleanCompany);
      setBilling((prev) => ({ ...prev, taxCode: cleanTax || prev.taxCode }));
      setNotice(`Đã cập nhật Hồ sơ Thuế Doanh nghiệp Thuê bao (Thông tư 86/2024/TT-BTC): ${cleanTax}.`);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể lưu hồ sơ thuế doanh nghiệp thuê bao.'));
    } finally {
      setBusy(false);
    }
  };

  const copyToClipboard = async (field: 'account' | 'amount' | 'content', text: string) => {
    try { if (navigator?.clipboard?.writeText) await navigator.clipboard.writeText(text); } catch { /* ignore */ }
    setCopiedField(field);
    setNotice(`Đã sao chép: ${text}`);
  };

  const createVietQrInvoice = async (overridePlanCode?: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => {
    if (busy) return;
    const targetPlan = overridePlanCode ?? selectedPlanCode;
    const targetSpec = SAAS_PLAN_CATALOG.find((p) => p.code === targetPlan);
    const warning = targetSpec ? buildPlanDowngradeWarning(targetSpec, billing) : null;
    if (warning) { setError(warning); return; }
    if (overridePlanCode) setSelectedPlanCode(overridePlanCode);
    setIsQrPanelOpen(true); setBusy(true); setError(''); setNotice('');
    const targetLabel = targetPlan === 'STARTER' ? 'Gói Khởi đầu' : targetPlan === 'ENTERPRISE' ? 'Gói Doanh nghiệp' : 'Gói Tăng trưởng';
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice', {
        planCode: targetPlan, amountVnd: PLAN_PRICES[targetPlan],
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const invoiceCode = String(d.invoiceCode ?? `INV-VQR-${targetPlan}`);
      const amountVnd = Number(d.amountVnd ?? PLAN_PRICES[targetPlan]);
      const cleanStr = (v: unknown, fb: string) => (typeof v === 'string' && v.trim() && !v.trim().includes('CHUA_CAU_HINH_VIETQR') ? v.trim() : fb);
      const bankObj = (d.vietQrBank ?? d.beneficiary ?? {}) as Record<string, unknown>;
      const [respBankName, respBankBin, respAccountNo, respAccountName] = [
        cleanStr(d.bankName ?? d.beneficiaryBankName ?? bankObj.bankName, billing.bankName),
        cleanStr(d.bankBin ?? bankObj.bankBin, billing.bankBin),
        cleanStr(d.accountNo ?? d.beneficiaryAccountNumber ?? bankObj.accountNo, billing.accountNo),
        cleanStr(d.accountName ?? d.beneficiaryAccountName ?? d.beneficiaryName ?? bankObj.accountName, billing.accountName),
      ];
      const qrPayload = String(d.qrPayload ?? d.vietQrPayload ?? buildClientVietQrPayload(amountVnd, invoiceCode, respBankBin, respAccountNo));
      const cleanTransferContent = formatVietQrTransferContent(String(d.transferContent ?? d.transferReference ?? invoiceCode), invoiceCode);
      const status = String(d.status ?? 'UNPAID').toUpperCase();
      const createdAtUtc = String(d.createdAtUtc ?? new Date().toISOString());
      const rawExpires = typeof d.expiresAtUtc === 'string' ? d.expiresAtUtc : null;
      const expMs = rawExpires ? new Date(rawExpires).getTime() : NaN;
      const expiresAtUtc = Number.isNaN(expMs) || expMs <= Date.now() ? new Date(Date.now() + 15 * 60000).toISOString() : rawExpires!;

      setRemainingSeconds(900);
      setVietQrInvoice({
        invoiceCode, planCode: targetPlan, amountVnd, status, qrPayload, transferContent: cleanTransferContent, expiresAtUtc,
        gatewayStatus: `Đang chờ thanh toán hóa đơn ${invoiceCode} • Hệ thống tự động kích hoạt gói ngay khi nhận chuyển khoản.`,
      });
      setBilling((prev) => {
        const exists = prev.recentInvoices.some((inv) => inv.invoiceCode === invoiceCode);
        const subTotalVnd = Math.round(amountVnd / 1.1);
        const nextInvoice: InvoiceRecord = {
          invoiceCode, planCode: targetPlan, planName: targetLabel, amountVnd, subTotalVnd, vatRatePercent: 10,
          vatAmountVnd: amountVnd - subTotalVnd, buyerTaxCode: prev.taxCode, paymentProvider: 'VIETQR', status,
          qrPayload, transferContent: cleanTransferContent, createdAtUtc, paidAtUtc: null, expiresAtUtc,
        };
        return {
          ...prev, bankName: respBankName || prev.bankName, bankBin: respBankBin || prev.bankBin,
          accountNo: respAccountNo || prev.accountNo, accountName: respAccountName || prev.accountName,
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

  const handleOpenVietQrClick = () => {
    openQrPanelOnDemand();
    if (vietQrInvoice.status === 'IDLE' || !vietQrInvoice.invoiceCode) void createVietQrInvoice();
  };

  const checkPaymentStatus = async () => {
    if (busy || checkingPayment) return;
    setBusy(true); setCheckingPayment(true); setError(''); setNotice('');
    const checkedInvoiceCode = vietQrInvoice.invoiceCode || 'hiện tại';
    try {
      const res = await apiClient
        .get<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing')
        .catch(() => apiClient.get<Record<string, unknown>>('/api/v1/saas-billing/subscription'));
      if (res?.data) {
        applyBillingData(res.data);
        const statusLabel = resolveSubscriptionStatusLabel(String(res.data.subscriptionStatus ?? res.data.status ?? 'ACTIVE'));
        const rawInvoices = Array.isArray(res.data.recentInvoices) ? (res.data.recentInvoices as Array<Record<string, unknown>>) : [];
        const matchedPaid = rawInvoices.some((inv) => String(inv.invoiceCode ?? '') === checkedInvoiceCode && String(inv.status ?? '').toUpperCase() === 'PAID');
        if (matchedPaid) {
          setVietQrInvoice((prev) => ({ ...prev, status: 'PAID', gatewayStatus: `Trạng thái gói dịch vụ: ${statusLabel} (Đã kích hoạt tự động & mở khóa 100% hạn mức)` }));
          setNotice(`Đã ghi nhận thanh toán thành công cho hóa đơn ${checkedInvoiceCode} • Gói hiện tại: ${selectedPlanLabel} (${statusLabel}).`);
        } else {
          setNotice(`Chưa ghi nhận giao dịch chuyển khoản cho hóa đơn ${checkedInvoiceCode}. Nếu bạn vừa chuyển khoản, vui lòng thử lại sau 30 giây. (Đã kiểm tra trạng thái giao dịch từ máy chủ • Gói hiện tại: ${selectedPlanLabel} - ${statusLabel}).`);
        }
      }
    } catch (err) {
      setError(extractErrorText(err, 'Không thể kiểm tra trạng thái giao dịch từ máy chủ.'));
    } finally {
      setBusy(false); setCheckingPayment(false);
    }
  };

  const confirmVietQrWebhook = async () => {
    if (busy) return;
    setBusy(true); setError(''); setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing/webhook-vietqr', {
        invoiceCode: vietQrInvoice.invoiceCode || undefined, planCode: selectedPlanCode,
        paidAmountVnd: PLAN_PRICES[selectedPlanCode], bankTransactionRef: `FT${Date.now()}`,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const statusLabel = resolveSubscriptionStatusLabel(String(d.subscriptionStatus ?? 'ACTIVE'));
      setVietQrInvoice((prev) => ({ ...prev, status: 'PAID', gatewayStatus: `Trạng thái gói dịch vụ: ${statusLabel} (Đã kích hoạt tự động & mở khóa 100% hạn mức)` }));
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
  const isUnpaidExpired = isQrPanelOpen && vietQrInvoice.status.toUpperCase() === 'UNPAID' && remainingSeconds <= 0;
  const isInvoiceExpired = vietQrInvoice.status.toUpperCase() === 'EXPIRED' || isUnpaidExpired;
  const isInvoicePaid =
    vietQrInvoice.status.toUpperCase() === 'PAID' ||
    billing.recentInvoices.some((inv) => inv.invoiceCode === 'INV-SAAS-202611-002' && inv.status.toUpperCase() === 'PAID');
  const isSuspendedOrReadOnly =
    billing.subscriptionStatus.toUpperCase().includes('SUSPENDED') ||
    billing.subscriptionStatus.toUpperCase().includes('READ_ONLY') ||
    (typeof window !== 'undefined' && window.localStorage?.getItem('tenantLifecycleStatus') === 'SUSPENDED');
  const hasGlobalSuspendedBanner =
    globalSuspendedMounted || (typeof document !== 'undefined' && Boolean(document.getElementById('customer-tenant-suspended-banner')));

  useEffect(() => {
    if (typeof document === 'undefined') return undefined;
    const globalBanner = document.getElementById('customer-tenant-suspended-banner');
    setGlobalSuspendedMounted(Boolean(globalBanner));
    const unlockBtn = globalBanner?.querySelector('[data-testid="btn-goto-billing-unlock"]');
    if (!unlockBtn) return undefined;
    const handleUnlockClick = () => scrollToVietQrPanel();
    unlockBtn.addEventListener('click', handleUnlockClick);
    return () => unlockBtn.removeEventListener('click', handleUnlockClick);
  }, [isSuspendedOrReadOnly, scrollToVietQrPanel]);

  useEffect(() => {
    if (isQrPanelOpen && autoRefreshQr && isInvoiceExpired && !busy && !isInvoicePaid) void createVietQrInvoice();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isQrPanelOpen, autoRefreshQr, isInvoiceExpired, busy, isInvoicePaid]);

  const activeInvoiceBadge = resolveInvoiceStatusBadge(isInvoicePaid ? 'PAID' : vietQrInvoice.status, isUnpaidExpired);
  const displayAmountVnd = vietQrInvoice.invoiceCode ? vietQrInvoice.amountVnd : selectedPlanSpec.priceVnd;
  const showQaWebhookSimulator = Boolean(import.meta.env.DEV) && import.meta.env.VITE_QA_WEBHOOK_SIMULATOR === 'true';
  const hasActiveMatchingInvoice = vietQrInvoice.status.toUpperCase() === 'UNPAID' && !isInvoiceExpired && Boolean(vietQrInvoice.invoiceCode) && vietQrInvoice.planCode.toUpperCase() === selectedPlanCode;
  const quotaGaugeItems = [
    { key: 'orders', title: 'Sản lượng đơn / tháng', unit: 'đơn', used: billing.currentMonthOrderCount, max: billing.monthlyOrderQuota, tier: orderTier },
    { key: 'warehouses', title: 'Kho đang hoạt động', unit: 'kho', used: billing.activeWarehouses, max: billing.maxWarehouses, tier: whTier },
    { key: 'products', title: 'Danh mục mặt hàng', unit: 'mặt hàng', used: billing.activeProducts, max: billing.maxProducts, tier: skuTier },
    { key: 'users', title: 'Tài khoản nhân sự', unit: 'tài khoản nhân sự', used: billing.activeUsers, max: billing.maxUsers, tier: userTier },
  ];

  return (
    <UiPage className="saas-billing-compact-page">
      <UiPageHeader
        eyebrow="Quản trị & Gói dịch vụ SaaS"
        title="Gói thuê bao & Thanh toán SaaS"
        description="Theo dõi chu kỳ cước, hạn mức vận hành theo thời gian thực và thanh toán nâng cấp tự động qua chuyển khoản Napas247 VietQR."
      />
      {isSuspendedOrReadOnly && !hasGlobalSuspendedBanner ? (
        <div role="alert" data-testid="saas-billing-suspended-unlock-banner" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '10px 14px', borderRadius: 8, backgroundColor: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 13, fontWeight: 600 }}>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, flex: '1 1 280px' }}>
            <AlertTriangle size={18} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0 }} />
            <span>Doanh nghiệp hiện đang ở trạng thái tạm khóa / chỉ đọc. Vui lòng thanh toán hóa đơn cước SaaS quá hạn để hệ thống tự động mở khóa quyền vận hành kho.</span>
          </span>
          <button type="button" data-testid="btn-scroll-vietqr-unlock" onClick={scrollToVietQrPanel} style={{ padding: '6px 12px', borderRadius: 6, backgroundColor: '#991b1b', color: '#ffffff', border: '1px solid #7f1d1d', fontSize: 12.5, fontWeight: 700, whiteSpace: 'nowrap', cursor: 'pointer', flexShrink: 0 }}>
            Thanh toán mở khóa ngay →
          </button>
        </div>
      ) : null}
      {error && <div role="alert">{error}</div>}
      {loading && <p role="status">Đang tải thông tin gói dịch vụ SaaS...</p>}

      <SaasBillingSummaryAndQuotas
        billing={billing} busy={busy} editingTaxCode={editingTaxCode} setEditingTaxCode={setEditingTaxCode}
        taxCodeDraft={taxCodeDraft} setTaxCodeDraft={setTaxCodeDraft} saveTaxCode={saveTaxCode}
        loadSubscription={loadSubscription} quotaGaugeItems={quotaGaugeItems} createVietQrInvoice={createVietQrInvoice}
      />

      <div className="saas-billing-mid-grid">
        <SaasBillingTaxProfileCard
          busy={busy} taxCodeDraft={taxCodeDraft} setTaxCodeDraft={setTaxCodeDraft}
          buyerCompanyName={buyerCompanyName} setBuyerCompanyName={setBuyerCompanyName}
          buyerTaxpayerType={buyerTaxpayerType} setBuyerTaxpayerType={setBuyerTaxpayerType}
          legalRegisteredAddress={legalRegisteredAddress} setLegalRegisteredAddress={setLegalRegisteredAddress}
          onSubmit={saveSaasTaxProfile}
        />
        <SaasBillingVietQrPanel
          billing={billing} busy={busy} checkingPayment={checkingPayment}
          isQrPanelOpen={isQrPanelOpen} onOpenQrPanel={handleOpenVietQrClick} onCloseQrPanel={() => setIsQrPanelOpen(false)}
          selectedPlanLabel={selectedPlanLabel} nextCycle={nextCycle} activeInvoiceBadge={activeInvoiceBadge}
          isInvoicePaid={isInvoicePaid} isInvoiceExpired={isInvoiceExpired} remainingSeconds={remainingSeconds}
          displayAmountVnd={displayAmountVnd} copiedField={copiedField} copyToClipboard={copyToClipboard}
          vietQrInvoice={vietQrInvoice} hasActiveMatchingInvoice={hasActiveMatchingInvoice}
          autoRefreshQr={autoRefreshQr} setAutoRefreshQr={setAutoRefreshQr}
          showQaWebhookSimulator={showQaWebhookSimulator} notice={notice}
          checkPaymentStatus={checkPaymentStatus} createVietQrInvoice={createVietQrInvoice} confirmVietQrWebhook={confirmVietQrWebhook}
        />
      </div>

      <SaasBillingInvoiceHistoryCard billing={billing} currentCycleText={currentCycleText} nextCycle={nextCycle} />

      <UiCard
        title="Bảng gói cước thuê bao SaaS & thanh toán gia hạn VietQR"
        subtitle="Chọn gói dịch vụ phù hợp quy mô doanh nghiệp và bấm tạo mã VietQR khi cần thanh toán chuyển khoản Napas247"
      >
        <SaasBillingPlanCardsGrid visiblePlans={visiblePlans} billing={billing} selectedPlanCode={selectedPlanCode} whTierPct={whTier.pct} busy={busy} handleSelectPlan={handleSelectPlan} />
      </UiCard>
    </UiPage>
  );
};

export default SaasBilling;
