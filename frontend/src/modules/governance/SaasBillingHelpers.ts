export type InvoiceRecord = {
  invoiceCode: string;
  planCode: string;
  planName: string;
  amountVnd: number;
  paymentProvider: string;
  status: string;
  qrPayload?: string;
  transferContent: string;
  createdAtUtc: string;
  paidAtUtc?: string | null;
  expiresAtUtc?: string | null;
};

export type BillingState = {
  planCode: string;
  planName: string;
  monthlyPriceVnd: number;
  subscriptionStatus: string;
  activeWarehouses: number;
  maxWarehouses: number;
  activeUsers: number;
  maxUsers: number;
  activeProducts: number;
  maxProducts: number;
  currentMonthOrderCount: number;
  monthlyOrderQuota: number;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
  nextRenewalDateUtc: string;
  taxCode: string;
  bankName: string;
  bankBin: string;
  accountNo: string;
  accountName: string;
  recentInvoices: InvoiceRecord[];
};

export const extractErrorText = (err: unknown, fallback: string) => {
  const anyErr = err as {
    response?: {
      status?: number;
      headers?: Record<string, string>;
      data?: { errorCode?: string; code?: string; correlationId?: string; message?: string; title?: string };
    };
  };
  const data = anyErr?.response?.data;
  const code = data?.errorCode || data?.code || anyErr?.response?.status || '';
  const corr = anyErr?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

export const formatDateVi = (isoString: string | undefined | null): string => {
  if (!isoString) return '30/11/2026';
  const parsed = new Date(isoString);
  return Number.isNaN(parsed.getTime()) ? '30/11/2026' : parsed.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' });
};

export const formatDateTimeVi = (isoString: string | undefined | null): string => {
  if (!isoString) return '—';
  const parsed = new Date(isoString);
  if (Number.isNaN(parsed.getTime())) return '—';
  const dd = String(parsed.getDate()).padStart(2, '0');
  const mm = String(parsed.getMonth() + 1).padStart(2, '0');
  const yyyy = parsed.getFullYear();
  const hh = String(parsed.getHours()).padStart(2, '0');
  const min = String(parsed.getMinutes()).padStart(2, '0');
  return `${dd}/${mm}/${yyyy} ${hh}:${min}`;
};

export const formatCountdownMmSs = (totalSeconds: number): string => {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const mins = Math.floor(safe / 60);
  const secs = safe % 60;
  return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
};

export const resolveNextBillingCycle = (currentPeriodEndUtc: string | undefined | null) => {
  const parsedEnd = currentPeriodEndUtc ? new Date(currentPeriodEndUtc) : new Date(Date.now() + 20 * 86400000);
  const safeEnd = Number.isNaN(parsedEnd.getTime()) ? new Date(Date.now() + 20 * 86400000) : parsedEnd;
  const nextStart = new Date(safeEnd.getTime() + 1 * 86400000);
  const nextEnd = new Date(safeEnd.getTime() + 30 * 86400000);
  const dueDateText = formatDateVi(safeEnd.toISOString());
  const nextStartText = formatDateVi(nextStart.toISOString());
  const nextEndText = formatDateVi(nextEnd.toISOString());
  return {
    dueDateText,
    nextStartText,
    nextEndText,
    shortRange: `${nextStartText} – ${nextEndText}`,
    fullLabel: `Kỳ cước thanh toán: ${nextStartText} – ${nextEndText} (Hạn thanh toán: ${dueDateText})`,
  };
};

export const normalizeTransferReference = (rawCode: string): string =>
  (rawCode || '')
    .replace(/^Nội dung CK:\s*/i, '')
    .replace(/^TT\s+/i, '')
    .replace(/[^A-Za-z0-9]/g, '')
    .toUpperCase()
    .replace(/^ERPKHO/, '');

export const formatVietQrTransferContent = (rawText: string, fallbackCode = ''): string => {
  const clean = normalizeTransferReference(rawText || fallbackCode);
  return clean ? `ERPKHO${clean}` : 'ERPKHO';
};

export const buildClientVietQrPayload = (amountVnd: number, invoiceCode: string, bankBin = '970423', accountNo = '98418102005'): string => {
  const cleanRef = normalizeTransferReference(invoiceCode) || 'ERPKHO';
  const beneficiaryInner = `00${String(bankBin.length).padStart(2, '0')}${bankBin}01${String(accountNo.length).padStart(2, '0')}${accountNo}`;
  const subTag01 = `01${String(beneficiaryInner.length).padStart(2, '0')}${beneficiaryInner}`;
  const tag38Inner = `0010A000000727${subTag01}0208QRIBFTTA`;
  const tag38 = `38${String(tag38Inner.length).padStart(2, '0')}${tag38Inner}`;
  const subTag08 = `08${String(cleanRef.length).padStart(2, '0')}${cleanRef}`;
  const tag62 = `62${String(subTag08.length).padStart(2, '0')}${subTag08}`;
  return `000201010212${tag38}530370454${Math.round(amountVnd)}5802VN${tag62}`;
};

export const resolveSubscriptionStatusLabel = (status: string): string => {
  const upper = (status || 'ACTIVE').trim().toUpperCase();
  if (upper.includes('ACTIVE')) return 'Đang hoạt động';
  if (upper.includes('TRIAL')) return 'Đang dùng thử';
  if (upper.includes('PAST_DUE') || upper.includes('OVERDUE')) return 'Quá hạn thanh toán';
  if (upper.includes('READ_ONLY') || upper.includes('LOCKED')) return 'Mức 3: Chế độ chỉ đọc (>115%)';
  if (upper.includes('SUSPENDED') || upper.includes('CANCEL')) return 'Tạm ngưng';
  return /^[A-Z0-9_]{3,}$/.test(upper) ? 'Đang hoạt động' : status;
};

export const resolveQuotaTierLabel = (used: number, max: number) => {
  if (max <= 0) return { label: 'Bình thường (<80%)', tone: 'success' as const, pct: 0, atCeiling: false };
  const pct = Math.round((used * 100) / max);
  if (pct > 115) return { label: 'Mức 3: Chế độ chỉ đọc (>115%)', tone: 'danger' as const, pct, atCeiling: true };
  if (pct > 100) return { label: `Mức 2: Đang vượt ${pct - 100}% (Cho phép tối đa 15%)`, tone: 'warning' as const, pct, atCeiling: true };
  if (pct === 100) return { label: 'Đã đạt hạn mức (100%)', tone: 'warning' as const, pct, atCeiling: true };
  if (pct >= 80) return { label: 'Mức 1: Cảnh báo sớm (80–99%)', tone: 'warning' as const, pct, atCeiling: false };
  return { label: 'Bình thường (<80%)', tone: 'success' as const, pct, atCeiling: false };
};

export const resolveInvoiceStatusBadge = (status: string, isExpired = false) => {
  const upper = (status || 'UNPAID').toUpperCase();
  if (upper === 'PAID') return { label: 'Đã thanh toán', tone: 'success' as const };
  if (upper === 'EXPIRED' || isExpired) return { label: 'Đã hết hạn (00:00)', tone: 'neutral' as const };
  if (upper === 'IDLE') return { label: 'Chưa tạo lệnh thanh toán', tone: 'neutral' as const };
  return { label: 'Chờ thanh toán', tone: 'warning' as const };
};

export const SAAS_PLAN_CATALOG = [
  { code: 'FREE_TRIAL' as const, title: 'Dùng thử Miễn phí 14 ngày', buttonLabel: 'Đang dùng thử', price: '0 đ/tháng', priceVnd: 0, maxWarehouses: 1, maxUsers: 3, monthlyOrderQuota: 300, maxProducts: 500, specs: '1 kho • 3 tài khoản • 300 đơn/tháng • 500 mặt hàng' },
  { code: 'STARTER' as const, title: 'Gói Khởi đầu', buttonLabel: 'Chọn gói Khởi đầu', price: '490.000 đ/tháng', priceVnd: 490000, maxWarehouses: 1, maxUsers: 5, monthlyOrderQuota: 1500, maxProducts: 2000, specs: '1 kho • 5 tài khoản • 1.500 đơn/tháng • 2.000 mặt hàng' },
  { code: 'GROWTH' as const, title: 'Gói Tăng trưởng', buttonLabel: 'Chọn gói Tăng trưởng', price: '1.490.000 đ/tháng', priceVnd: 1490000, maxWarehouses: 3, maxUsers: 20, monthlyOrderQuota: 10000, maxProducts: 20000, specs: '3 kho • 20 tài khoản • 10.000 đơn/tháng • 20.000 mặt hàng' },
  { code: 'ENTERPRISE' as const, title: 'Gói Doanh nghiệp', buttonLabel: 'Chọn gói Doanh nghiệp', price: '3.490.000 đ/tháng', priceVnd: 3490000, maxWarehouses: 20, maxUsers: 100, monthlyOrderQuota: 100000, maxProducts: 100000, specs: '20 kho • 100 tài khoản • 100.000 đơn/tháng • 100.000 mặt hàng' },
] as const;

export const buildPlanDowngradeWarning = (
  plan: (typeof SAAS_PLAN_CATALOG)[number],
  billing: Pick<BillingState, 'planCode' | 'activeWarehouses' | 'activeUsers' | 'activeProducts' | 'currentMonthOrderCount'>,
): string | null => {
  if (billing.planCode === plan.code) return null;
  const exceedsWarehouse = billing.activeWarehouses > plan.maxWarehouses;
  const exceedsOrders = billing.currentMonthOrderCount > plan.monthlyOrderQuota;
  const exceedsUsers = billing.activeUsers > plan.maxUsers;
  const exceedsProducts = billing.activeProducts > plan.maxProducts;
  if (!exceedsWarehouse && !exceedsOrders && !exceedsUsers && !exceedsProducts) return null;
  const shortPlanTitle = plan.title.replace(/^Gói\s+/i, '');
  if (exceedsWarehouse && exceedsOrders) {
    return `Không khả dụng để hạ cấp: Bạn đang dùng ${billing.activeWarehouses.toLocaleString('vi-VN')} kho & ${billing.currentMonthOrderCount.toLocaleString('vi-VN')} đơn/tháng (vượt giới hạn ${plan.maxWarehouses.toLocaleString('vi-VN')} kho & ${plan.monthlyOrderQuota.toLocaleString('vi-VN')} đơn của gói ${shortPlanTitle}).`;
  }
  const usageParts: string[] = [];
  const limitParts: string[] = [];
  if (exceedsWarehouse) {
    usageParts.push(`${billing.activeWarehouses.toLocaleString('vi-VN')} kho`);
    limitParts.push(`${plan.maxWarehouses.toLocaleString('vi-VN')} kho`);
  }
  if (exceedsOrders) {
    usageParts.push(`${billing.currentMonthOrderCount.toLocaleString('vi-VN')} đơn/tháng`);
    limitParts.push(`${plan.monthlyOrderQuota.toLocaleString('vi-VN')} đơn`);
  }
  if (exceedsUsers) {
    usageParts.push(`${billing.activeUsers.toLocaleString('vi-VN')} tài khoản`);
    limitParts.push(`${plan.maxUsers.toLocaleString('vi-VN')} tài khoản`);
  }
  if (exceedsProducts) {
    usageParts.push(`${billing.activeProducts.toLocaleString('vi-VN')} mặt hàng`);
    limitParts.push(`${plan.maxProducts.toLocaleString('vi-VN')} mặt hàng`);
  }
  return `Không khả dụng để hạ cấp: Bạn đang dùng ${usageParts.join(' & ')} (vượt giới hạn ${limitParts.join(' & ')} của gói ${shortPlanTitle}).`;
};

export const secondaryBtnStyle = {
  background: '#ffffff',
  border: '1px solid #94a3b8',
  color: '#0f172a',
  fontWeight: 600,
  borderRadius: 6,
  cursor: 'pointer',
  whiteSpace: 'nowrap',
  flexShrink: 0,
} as const;


export function parseBillingSubscriptionResponse(bd: Record<string, unknown>) {
  const rawPlanName = typeof bd.planName === 'string' ? bd.planName.trim() : '';
  const rawPlan = String(bd.planCode ?? rawPlanName ?? 'GROWTH').toUpperCase();
  const isFreeTrial = rawPlan.includes('FREE') || rawPlan.includes('TRIAL');
  const isStarter = rawPlan.includes('STARTER');
  const isEnterprise = rawPlan.includes('ENTERPRISE') || rawPlanName.includes('Doanh nghiệp');
  const canonicalCode: 'STARTER' | 'GROWTH' | 'ENTERPRISE' =
    isFreeTrial || isStarter ? 'STARTER' : isEnterprise ? 'ENTERPRISE' : 'GROWTH';
  const planName = isFreeTrial
    ? 'Dùng thử Miễn phí 14 ngày'
    : isStarter
      ? 'Gói Khởi đầu'
      : isEnterprise
        ? 'Gói Doanh nghiệp'
        : 'Gói Tăng trưởng';
  const canonicalWarehouses = isFreeTrial || isStarter ? 1 : isEnterprise ? 20 : 3;
  const canonicalUsers = isFreeTrial ? 3 : isStarter ? 5 : isEnterprise ? 100 : 20;
  const canonicalProducts = isFreeTrial ? 500 : isStarter ? 2000 : isEnterprise ? 100000 : 20000;
  const canonicalOrderQuota = isFreeTrial ? 300 : isStarter ? 1500 : isEnterprise ? 100000 : 10000;
  const canonicalPrice = isFreeTrial ? 0 : isStarter ? 490000 : isEnterprise ? 3490000 : 1490000;
  const bankObj = (bd.vietQrBank ?? {}) as Record<string, unknown>;
  const resolvedBankBin = String(bd.bankBin ?? bankObj.bankBin ?? '970423');
  const resolvedAccountNo = String(bd.accountNo ?? bankObj.accountNo ?? '98418102005');
  const resolvedTaxCode = String(bd.taxCode ?? bd.billingTaxCode ?? '0318889999');

  const rawInvoices = Array.isArray(bd.recentInvoices) ? bd.recentInvoices : [];
  const mappedInvoices: InvoiceRecord[] = rawInvoices.map((item: unknown, idx: number) => {
    const rec = (item ?? {}) as Record<string, unknown>;
    const code = String(rec.invoiceCode ?? `INV-VQR-${idx + 1}`);
    const pCode = String(rec.planCode ?? canonicalCode).toUpperCase();
    const invStatus = String(rec.status ?? 'UNPAID').toUpperCase();
    const amount = Number(rec.amountVnd ?? canonicalPrice);
    const rawExp = typeof rec.expiresAtUtc === 'string' ? rec.expiresAtUtc : null;
    const expMs = rawExp ? new Date(rawExp).getTime() : NaN;
    const validExpiresAtUtc =
      invStatus === 'UNPAID' && (Number.isNaN(expMs) || expMs <= Date.now())
        ? new Date(Date.now() + 30 * 60000).toISOString()
        : (rawExp ?? new Date(Date.now() + 30 * 60000).toISOString());

    return {
      invoiceCode: code,
      planCode: pCode,
      planName: String(
        rec.planName ??
          (pCode.includes('ENTERPRISE') ? 'Gói Doanh nghiệp' : pCode.includes('STARTER') ? 'Gói Khởi đầu' : 'Gói Tăng trưởng'),
      )
        .split('—')[0]
        .trim(),
      amountVnd: amount,
      paymentProvider: String(rec.paymentProvider ?? 'VIETQR'),
      status: invStatus,
      qrPayload:
        typeof rec.qrPayload === 'string' && rec.qrPayload
          ? rec.qrPayload
          : buildClientVietQrPayload(amount, code, resolvedBankBin, resolvedAccountNo),
      transferContent: formatVietQrTransferContent(String(rec.transferContent ?? code), code),
      createdAtUtc: String(rec.createdAtUtc ?? new Date().toISOString()),
      paidAtUtc: typeof rec.paidAtUtc === 'string' ? rec.paidAtUtc : null,
      expiresAtUtc: validExpiresAtUtc,
    };
  });

  const latestUnpaid = mappedInvoices.find(inv => inv.status.toUpperCase() === 'UNPAID');
  const activeTargetPlan: 'STARTER' | 'GROWTH' | 'ENTERPRISE' =
    latestUnpaid?.planCode === 'STARTER' || latestUnpaid?.planCode === 'ENTERPRISE' || latestUnpaid?.planCode === 'GROWTH'
      ? latestUnpaid.planCode
      : canonicalCode;

  const billingState: BillingState = {
    planCode: isFreeTrial ? 'FREE_TRIAL' : canonicalCode,
    planName,
    monthlyPriceVnd: Number(bd.monthlyPriceVnd ?? canonicalPrice),
    subscriptionStatus: String(bd.subscriptionStatus ?? bd.status ?? 'ACTIVE'),
    activeWarehouses: Number(bd.activeWarehouses ?? bd.currentWarehouseCount ?? bd.usedWarehouses ?? 0),
    maxWarehouses: isEnterprise ? Math.max(Number(bd.maxWarehouses ?? 20), 20) : Number(bd.maxWarehouses ?? canonicalWarehouses) || canonicalWarehouses,
    activeUsers: Number(bd.activeUsers ?? bd.currentUserCount ?? bd.usedUsers ?? 0),
    maxUsers: isEnterprise ? Math.max(Number(bd.maxUsers ?? 100), 100) : Number(bd.maxUsers ?? canonicalUsers) || canonicalUsers,
    activeProducts: Number(bd.activeProducts ?? bd.currentSkuCount ?? bd.usedProducts ?? 0),
    maxProducts: isEnterprise ? Math.max(Number(bd.maxProducts ?? 100000), 100000) : Number(bd.maxProducts ?? bd.maxSkus ?? canonicalProducts) || canonicalProducts,
    currentMonthOrderCount: Number(bd.currentMonthOrderCount ?? bd.usedMonthlyOrders ?? 0),
    monthlyOrderQuota: isEnterprise ? Math.max(Number(bd.monthlyOrderQuota ?? 100000), 100000) : Number(bd.monthlyOrderQuota ?? canonicalOrderQuota) || canonicalOrderQuota,
    currentPeriodStartUtc: String(bd.currentPeriodStartUtc ?? new Date(Date.now() - 10 * 86400000).toISOString()),
    currentPeriodEndUtc: String(bd.currentPeriodEndUtc ?? new Date(Date.now() + 20 * 86400000).toISOString()),
    nextRenewalDateUtc: String(bd.nextRenewalDateUtc ?? bd.currentPeriodEndUtc ?? new Date(Date.now() + 20 * 86400000).toISOString()),
    taxCode: resolvedTaxCode,
    bankName: String(bd.bankName ?? bankObj.bankName ?? 'TPBank'),
    bankBin: resolvedBankBin,
    accountNo: resolvedAccountNo,
    accountName: String(bd.accountName ?? bankObj.accountName ?? 'HUYNH PHI VAN'),
    recentInvoices: mappedInvoices,
  };

  const vietQrState = latestUnpaid
    ? {
        invoiceCode: latestUnpaid.invoiceCode,
        planCode: latestUnpaid.planCode,
        amountVnd: latestUnpaid.amountVnd,
        status: 'UNPAID',
        qrPayload: latestUnpaid.qrPayload || buildClientVietQrPayload(latestUnpaid.amountVnd, latestUnpaid.invoiceCode, resolvedBankBin, resolvedAccountNo),
        transferContent: latestUnpaid.transferContent,
        expiresAtUtc: latestUnpaid.expiresAtUtc || new Date(Date.now() + 30 * 60000).toISOString(),
        gatewayStatus: `Đang chờ thanh toán hóa đơn ${latestUnpaid.invoiceCode} • Hệ thống tự động kích hoạt gói ngay khi nhận chuyển khoản.`,
      }
    : {
        invoiceCode: '',
        planCode: canonicalCode,
        amountVnd: canonicalPrice,
        status: 'IDLE',
        qrPayload: '',
        transferContent: 'Chưa tạo lệnh thanh toán',
        expiresAtUtc: new Date(Date.now() + 30 * 60000).toISOString(),
        gatewayStatus: 'Chưa có hóa đơn chờ thanh toán • Bấm nút tạo mã VietQR để khởi tạo hóa đơn mới.',
      };

  return { activeTargetPlan, resolvedTaxCode, billingState, vietQrState };
}
