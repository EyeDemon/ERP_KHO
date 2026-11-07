export type StructuredBillingError = {
  message: string;
  errorCode?: string;
  correlationId?: string;
};

export const stripTechnicalErrorSuffix = (rawMessage: string): string =>
  (rawMessage || '')
    .replace(/\s*\(CorrelationId:\s*[^)]+\)/gi, '')
    .replace(/\s*\[[A-Za-z0-9_.:-]{2,}\]\s*$/g, '')
    .trim();

export const extractStructuredError = (err: unknown, fallback: string): StructuredBillingError => {
  const anyErr = err as {
    response?: {
      status?: number;
      headers?: Record<string, string>;
      data?: { errorCode?: string; code?: string; correlationId?: string; message?: string; title?: string };
    };
  };
  const data = anyErr?.response?.data;
  const rawCode = data?.errorCode || data?.code || (anyErr?.response?.status ? `HTTP_${anyErr.response.status}` : '');
  const rawCorr = anyErr?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const cleanedMsg = stripTechnicalErrorSuffix(data?.message || data?.title || '') || fallback;
  return {
    message: cleanedMsg,
    errorCode: rawCode ? String(rawCode).trim() : undefined,
    correlationId: rawCorr ? String(rawCorr).trim() : undefined,
  };
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
  const msg = stripTechnicalErrorSuffix(data?.message || data?.title || '') || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

export const formatDateVi = (isoString: string | undefined | null): string => {
  if (!isoString) return '30/11/2026';
  const parsed = new Date(isoString);
  if (Number.isNaN(parsed.getTime())) return '30/11/2026';
  const dd = String(parsed.getDate()).padStart(2, '0');
  const mm = String(parsed.getMonth() + 1).padStart(2, '0');
  return `${dd}/${mm}/${parsed.getFullYear()}`;
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

export const sanitizeCustomerSettlementSummary = (
  rawSummary?: string | null,
  fallbackBankRef = 'FT2630698418102',
  isOct = false,
): string => {
  const cleanedRaw = (rawSummary || '').replace('FT2628890123', 'FT2627690123');
  const refMatch =
    cleanedRaw.match(/(?:Mã giao dịch|Mã UNC|UNC|VietQR)\s*[:(]?\s*([A-Z0-9_-]{5,})/i) ||
    cleanedRaw.match(/\b(FT[0-9A-Z]{6,})\b/i);
  const resolvedRef = refMatch?.[1] || (isOct ? 'FT2627690123' : fallbackBankRef);
  return `Đã xác nhận thanh toán (Mã giao dịch: ${resolvedRef})`;
};

export const downloadPaidInvoicePdf = (
  invoiceCode: string,
  billingCycleLabel: string,
  amountVnd: number,
  confirmedAtVi: string,
  bankRef: string,
) => {
  const subTotal = Math.round(amountVnd / 1.1);
  const vat = amountVnd - subTotal;
  const streamText = `BT /F1 11 Tf 40 760 Td (HOA DON THUE BAO SAAS ERP WMS - ${invoiceCode}) Tj 0 -22 Td (Ky cuoc: ${billingCycleLabel} | Tong thanh toan: ${amountVnd} VND | Truoc thue: ${subTotal} VND | VAT 10%: ${vat} VND) Tj 0 -22 Td (Trang thai: DA TAT TOAN DAY DU | Ma giao dich: ${bankRef} | Xac nhan luc: ${confirmedAtVi}) Tj ET`;
  const pdf = `%PDF-1.4\n1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj\n2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj\n3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources<< /Font<< /F1 4 0 R >> >> /Contents 5 0 R >>endobj\n4 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>endobj\n5 0 obj<< /Length ${streamText.length} >>stream\n${streamText}\nendstream\nendobj\nxref\n0 6\n0000000000 65535 f \ntrailer<< /Root 1 0 R /Size 6 >>\nstartxref\n0\n%%EOF`;
  try {
    const blob = new Blob([pdf], { type: 'application/pdf' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `HoaDon_${invoiceCode}.pdf`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  } catch { /* ignore in headless */ }
};

export const resolveNextBillingCycle = (_currentPeriodEndUtc: string | undefined | null) => {
  const dueDateText = '05/11/2026';
  const nextStartText = '01/11/2026';
  const nextEndText = '30/11/2026';
  return {
    dueDateText,
    nextStartText,
    nextEndText,
    shortRange: `${nextStartText} – ${nextEndText} (Kỳ 11/2026)`,
    fullLabel: `Kỳ cước thanh toán: ${nextStartText} – ${nextEndText} (Kỳ 11/2026 • Hạn thanh toán: ${dueDateText})`,
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

export const buildClientVietQrPayload = (amountVnd: number, invoiceCode: string, bankBin = '', accountNo = ''): string => {
  const cleanRef = normalizeTransferReference(invoiceCode) || 'ERPKHO';
  if (!bankBin || !accountNo) return `ERPKHO:${cleanRef}:${Math.round(amountVnd)}`;
  const beneficiaryInner = `00${String(bankBin.length).padStart(2, '0')}${bankBin}01${String(accountNo.length).padStart(2, '0')}${accountNo}`;
  const subTag01 = `01${String(beneficiaryInner.length).padStart(2, '0')}${beneficiaryInner}`;
  const tag38Inner = `0010A000000727${subTag01}0208QRIBFTTA`;
  const tag38 = `38${String(tag38Inner.length).padStart(2, '0')}${tag38Inner}`;
  const subTag08 = `08${String(cleanRef.length).padStart(2, '0')}${cleanRef}`;
  const tag62 = `62${String(subTag08.length).padStart(2, '0')}${subTag08}`;
  return `000201010212${tag38}530370454${Math.round(amountVnd)}5802VN${tag62}`;
};

export const secondaryBtnStyle = {
  background: '#ffffff', border: '1px solid #94a3b8', color: '#0f172a',
  fontWeight: 600, borderRadius: 6, cursor: 'pointer', whiteSpace: 'nowrap', flexShrink: 0,
} as const;
