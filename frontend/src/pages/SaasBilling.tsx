import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { BarcodeSvg } from '../components/BarcodeSvg';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';

type BillingState = {
  planCode: string;
  planName: string;
  subscriptionStatus: string;
  activeWarehouses: number;
  maxWarehouses: number;
  activeUsers: number;
  maxUsers: number;
  activeProducts: number;
  maxProducts: number;
  currentMonthOrderCount: number;
  monthlyOrderQuota: number;
};

const extractErrorText = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const code = data?.errorCode || data?.code || err?.response?.status || '';
  const corr = err?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

const resolveSubscriptionStatusLabel = (status: string): string => {
  const upper = (status || 'ACTIVE').trim().toUpperCase();
  if (upper.includes('ACTIVE')) return 'Đang hoạt động';
  if (upper.includes('TRIAL')) return 'Đang dùng thử';
  if (upper.includes('PAST_DUE') || upper.includes('OVERDUE')) return 'Quá hạn thanh toán';
  if (upper.includes('READ_ONLY') || upper.includes('LOCKED')) return 'Khóa chỉ đọc (>115%)';
  if (upper.includes('SUSPENDED') || upper.includes('CANCEL')) return 'Tạm ngưng';
  if (/^[A-Z0-9_]{3,}$/.test(upper)) return 'Đang hoạt động';
  return status;
};

const resolveQuotaTierLabel = (used: number, max: number) => {
  if (max <= 0) return { label: 'Bình thường', tone: 'success' as const, pct: 0 };
  const pct = Math.round((used * 100) / max);
  if (pct > 115) return { label: 'Khóa chỉ đọc (>115%)', tone: 'danger' as const, pct };
  if (pct > 100) return { label: 'Cho phép vượt 115%', tone: 'warning' as const, pct };
  if (pct >= 90) return { label: 'Cảnh báo 90%', tone: 'warning' as const, pct };
  return { label: 'Bình thường', tone: 'success' as const, pct };
};

const SaasBilling = () => {
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [selectedPlanCode, setSelectedPlanCode] = useState<'STARTER' | 'GROWTH' | 'ENTERPRISE'>('ENTERPRISE');
  const [billing, setBilling] = useState<BillingState>({
    planCode: 'ENTERPRISE',
    planName: 'Gói Doanh nghiệp',
    subscriptionStatus: 'ACTIVE',
    activeWarehouses: 0,
    maxWarehouses: 20,
    activeUsers: 0,
    maxUsers: 100,
    activeProducts: 0,
    maxProducts: 100000,
    currentMonthOrderCount: 0,
    monthlyOrderQuota: 100000,
  });
  const [vietQrInvoice, setVietQrInvoice] = useState({
    qrPayload: '',
    transferReference: '',
    gatewayStatus: 'Sẵn sàng khởi tạo mã thanh toán VietQR tự động',
  });

  const applyBillingData = (bd: Record<string, unknown>) => {
    const rawPlanName = typeof bd.planName === 'string' ? bd.planName.trim() : '';
    const rawPlan = String(bd.planCode ?? rawPlanName ?? 'ENTERPRISE').toUpperCase();
    const isStarter = rawPlan.includes('STARTER');
    const isEnterprise = rawPlan.includes('ENTERPRISE') || rawPlanName.includes('Doanh nghiệp');
    const canonicalCode: 'STARTER' | 'GROWTH' | 'ENTERPRISE' = isStarter
      ? 'STARTER'
      : isEnterprise
        ? 'ENTERPRISE'
        : 'GROWTH';
    const planName = isStarter
      ? 'Gói Khởi đầu'
      : isEnterprise
        ? 'Gói Doanh nghiệp'
        : 'Gói Tăng trưởng';
    const canonicalWarehouses = isStarter ? 1 : isEnterprise ? 20 : 3;
    const canonicalUsers = isStarter ? 5 : isEnterprise ? 100 : 20;
    const canonicalProducts = isStarter ? 2000 : isEnterprise ? 100000 : 20000;
    const canonicalOrderQuota = isStarter ? 1500 : isEnterprise ? 100000 : 10000;
    setSelectedPlanCode(canonicalCode);
    setBilling({
      planCode: canonicalCode,
      planName,
      subscriptionStatus: String(bd.subscriptionStatus ?? bd.status ?? 'ACTIVE'),
      activeWarehouses: Number(bd.activeWarehouses ?? bd.currentWarehouseCount ?? bd.usedWarehouses ?? 0),
      maxWarehouses: isEnterprise ? Math.max(Number(bd.maxWarehouses ?? 20), 20) : (Number(bd.maxWarehouses ?? canonicalWarehouses) || canonicalWarehouses),
      activeUsers: Number(bd.activeUsers ?? bd.currentUserCount ?? bd.usedUsers ?? 0),
      maxUsers: isEnterprise ? Math.max(Number(bd.maxUsers ?? 100), 100) : (Number(bd.maxUsers ?? canonicalUsers) || canonicalUsers),
      activeProducts: Number(bd.activeProducts ?? bd.currentSkuCount ?? bd.usedProducts ?? 0),
      maxProducts: isEnterprise ? Math.max(Number(bd.maxProducts ?? 100000), 100000) : (Number(bd.maxProducts ?? canonicalProducts) || canonicalProducts),
      currentMonthOrderCount: Number(bd.currentMonthOrderCount ?? bd.usedMonthlyOrders ?? 0),
      monthlyOrderQuota: isEnterprise ? Math.max(Number(bd.monthlyOrderQuota ?? 100000), 100000) : (Number(bd.monthlyOrderQuota ?? canonicalOrderQuota) || canonicalOrderQuota),
    });
  };

  const loadSubscription = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient
        .get<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing')
        .catch(() => apiClient.get<Record<string, unknown>>('/api/v1/saas-billing/subscription'));
      if (res?.data) {
        applyBillingData(res.data);
      }
    } catch (err) {
      setError(extractErrorText(err, 'Không thể tải thông tin gói cước thuê bao SaaS.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadSubscription();
  }, [loadSubscription]);

  const selectedPlanLabel =
    selectedPlanCode === 'STARTER'
      ? 'Gói Khởi đầu'
      : selectedPlanCode === 'ENTERPRISE'
        ? 'Gói Doanh nghiệp'
        : 'Gói Tăng trưởng';

  const createVietQrInvoice = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    const planPriceMap: Record<'STARTER' | 'GROWTH' | 'ENTERPRISE', number> = {
      STARTER: 490000,
      GROWTH: 1490000,
      ENTERPRISE: 3490000,
    };
    try {
      const res = await apiClient.post<Record<string, unknown>>(
        '/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice',
        {
          planCode: selectedPlanCode,
          amountVnd: planPriceMap[selectedPlanCode],
        },
      );
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const qrPayload = String(d.qrPayload ?? d.vietQrPayload ?? `VIETQR-PAYOS-${selectedPlanCode}`);
      const transferContent = String(d.transferContent ?? d.transferReference ?? `ERPKHO2026 ${selectedPlanCode}`);
      setVietQrInvoice({
        qrPayload,
        transferReference: `Nội dung CK: ${transferContent}`,
        gatewayStatus: 'Đối soát tự động PayOS / SePay: Đã tạo lệnh thanh toán & sẵn sàng kích hoạt tự động',
      });
      setNotice(`Đã khởi tạo mã thanh toán VietQR tự động cho ${selectedPlanLabel} (${transferContent}).`);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể khởi tạo hóa đơn thanh toán VietQR.'));
    } finally {
      setBusy(false);
    }
  };

  const checkPaymentStatus = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient
        .get<Record<string, unknown>>('/api/v1/permissions/saas/subscription-billing')
        .catch(() => apiClient.get<Record<string, unknown>>('/api/v1/saas-billing/subscription'));
      if (res?.data) {
        applyBillingData(res.data);
        const rawStatus = String(res.data.subscriptionStatus ?? res.data.status ?? 'ACTIVE');
        const statusLabel = resolveSubscriptionStatusLabel(rawStatus);
        setVietQrInvoice((prev) => ({
          ...prev,
          gatewayStatus: `Trạng thái gói cước từ máy chủ: ${statusLabel} (Tự động đối soát qua Webhook ngân hàng)`,
        }));
        setNotice(`Đã kiểm tra trạng thái giao dịch từ máy chủ • Gói hiện tại: ${selectedPlanLabel} (${statusLabel}).`);
      }
    } catch (err) {
      setError(extractErrorText(err, 'Không thể kiểm tra trạng thái giao dịch từ máy chủ.'));
    } finally {
      setBusy(false);
    }
  };

  const confirmVietQrWebhook = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>(
        '/api/v1/permissions/saas/subscription-billing/webhook-vietqr',
        {
          planCode: selectedPlanCode,
          paidAmountVnd: selectedPlanCode === 'STARTER' ? 490000 : selectedPlanCode === 'ENTERPRISE' ? 3490000 : 1490000,
          bankTransactionRef: `FT-${Date.now()}`,
        },
      );
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const rawStatus = String(d.subscriptionStatus ?? 'ACTIVE');
      const statusLabel = resolveSubscriptionStatusLabel(rawStatus);
      setVietQrInvoice((prev) => ({
        ...prev,
        gatewayStatus: `Trạng thái gói cước: ${statusLabel} (Đã kích hoạt tự động & mở khóa 100% hạn mức)`,
      }));
      setNotice(
        typeof d.message === 'string' && d.message
          ? d.message
          : `Đã xác nhận thanh toán tự động Webhook PayOS / SePay cho ${selectedPlanLabel} • Trạng thái: ${statusLabel}.`,
      );
      await loadSubscription();
    } catch (err) {
      setError(extractErrorText(err, 'Không thể xác nhận thanh toán tự động qua Webhook.'));
    } finally {
      setBusy(false);
    }
  };

  const whTier = resolveQuotaTierLabel(billing.activeWarehouses, billing.maxWarehouses);
  const userTier = resolveQuotaTierLabel(billing.activeUsers, billing.maxUsers);
  const skuTier = resolveQuotaTierLabel(billing.activeProducts, billing.maxProducts);
  const orderTier = resolveQuotaTierLabel(billing.currentMonthOrderCount, billing.monthlyOrderQuota);

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Quản trị & Thuê bao SaaS"
        title="Gói cước Thuê bao SaaS, Hạn mức 3 Nấc & Thanh toán Tự động VietQR"
        description="Theo dõi hạn mức kho, tài khoản, danh mục mặt hàng, sản lượng đơn hàng tháng và thanh toán gia hạn tự động qua VietQR (PayOS / SePay)."
        actions={
          <Link to="/permissions" className="ui-btn-secondary" style={{ textDecoration: 'none', padding: '6px 12px', borderRadius: 6, border: '1px solid #cbd5e1' }}>
            ← Quay lại Quản trị Quyền RBAC
          </Link>
        }
      />

      {error && <div role="alert">{error}</div>}
      {notice && <p role="status" className="ui-success-text">{notice}</p>}
      {loading && <p role="status">Đang tải thông tin gói cước thuê bao SaaS...</p>}

      <UiMetricGrid>
        <UiMetric value={billing.planName} label={`Trạng thái: ${resolveSubscriptionStatusLabel(billing.subscriptionStatus)}`} />
        <UiMetric value={`${billing.activeWarehouses} / ${billing.maxWarehouses} kho`} label={`Hạn mức kho: ${whTier.label} (${whTier.pct}%)`} />
        <UiMetric value={`${billing.activeUsers} / ${billing.maxUsers} người dùng`} label={`Hạn mức tài khoản: ${userTier.label} (${userTier.pct}%)`} />
        <UiMetric value={`${billing.activeProducts} / ${billing.maxProducts.toLocaleString('vi-VN')} mặt hàng`} label={`Hạn mức mặt hàng: ${skuTier.label} (${skuTier.pct}%)`} />
      </UiMetricGrid>

      <UiCard
        title="Thanh đo Hạn mức Thuê bao 3 Nấc (Bình thường / Cảnh báo 90% / Cho phép vượt 115% / Khóa chỉ đọc >115%)"
        subtitle="Bảo vệ liên tục vận hành kho giờ cao điểm: cho phép vượt 115% hạn mức trước khi chuyển sang chế độ chỉ đọc (vẫn bảo toàn 100% tra cứu & xuất Excel)"
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          <div className="ui-inline-wrap" style={{ gap: 8 }}>
            <UiBadge tone="success">Bình thường (&lt;90%)</UiBadge>
            <UiBadge tone="warning">Cảnh báo 90% (90–100%)</UiBadge>
            <UiBadge tone="warning">Cho phép vượt 115% (100–115%)</UiBadge>
            <UiBadge tone="danger">Khóa chỉ đọc (&gt;115%)</UiBadge>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }} data-testid="saas-quota-gauge-grid">
            {[
              { title: 'Kho đang hoạt động', used: billing.activeWarehouses, max: billing.maxWarehouses, tier: whTier },
              { title: 'Tài khoản người dùng', used: billing.activeUsers, max: billing.maxUsers, tier: userTier },
              { title: 'Danh mục mặt hàng', used: billing.activeProducts, max: billing.maxProducts, tier: skuTier },
              { title: 'Sản lượng đơn hàng tháng', used: billing.currentMonthOrderCount, max: billing.monthlyOrderQuota, tier: orderTier },
            ].map((item) => (
              <div key={item.title} style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6 }}>
                  <span style={{ fontSize: 12, fontWeight: 600, color: '#475569' }}>{item.title}</span>
                  <UiBadge tone={item.tier.tone}>{item.tier.label}</UiBadge>
                </div>
                <strong style={{ display: 'block', fontSize: 16, color: '#0f172a', marginTop: 4, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                  {item.used.toLocaleString('vi-VN')} / {item.max.toLocaleString('vi-VN')}
                </strong>
                <div style={{ marginTop: 6, height: 8, borderRadius: 4, background: '#e2e8f0', overflow: 'hidden' }}>
                  <div
                    style={{
                      width: `${Math.min(100, item.tier.pct)}%`,
                      height: '100%',
                      background: item.tier.pct > 115 ? '#dc2626' : item.tier.pct >= 90 ? '#d97706' : '#059669',
                    }}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      </UiCard>

      <UiCard
        title="Bảng Gói cước Thuê bao SaaS & Thanh toán Gia hạn VietQR"
        subtitle="Chọn gói cước phù hợp quy mô doanh nghiệp và quét mã VietQR để tự động mở khóa hạn mức tức thì"
      >
        <div className="ui-stack" style={{ gap: 14 }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12 }}>
            {[
              { code: 'STARTER' as const, title: 'Gói Khởi đầu', price: '490.000 đ/tháng', specs: '1 kho • 5 tài khoản • 1.500 đơn/tháng' },
              { code: 'GROWTH' as const, title: 'Gói Tăng trưởng', price: '1.490.000 đ/tháng', specs: '3 kho • 20 tài khoản • 10.000 đơn/tháng' },
              { code: 'ENTERPRISE' as const, title: 'Gói Doanh nghiệp', price: '3.490.000 đ/tháng', specs: '20 kho • 100 tài khoản • 100.000 đơn/tháng' },
            ].map((plan) => {
              const active = selectedPlanCode === plan.code;
              return (
                <div
                  key={plan.code}
                  onClick={() => setSelectedPlanCode(plan.code)}
                  style={{
                    cursor: 'pointer',
                    padding: '12px 14px',
                    borderRadius: 8,
                    border: active ? '2px solid #0284c7' : '1px solid #cbd5e1',
                    background: active ? '#f0f9ff' : '#ffffff',
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <strong style={{ fontSize: 14, color: '#0f172a' }}>{plan.title}</strong>
                    {active && <UiBadge tone="info">Đang chọn</UiBadge>}
                  </div>
                  <div style={{ fontSize: 15, fontWeight: 700, color: '#0369a1', marginTop: 4, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{plan.price}</div>
                  <div style={{ fontSize: 12, color: '#475569', marginTop: 4 }}>{plan.specs}</div>
                </div>
              );
            })}
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 14, padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
            <div className="ui-stack" style={{ gap: 8, flex: '1 1 320px' }}>
              <div className="ui-inline-wrap" style={{ gap: 8 }}>
                <button
                  type="button"
                  data-testid="btn-create-vietqr-invoice"
                  disabled={busy}
                  onClick={() => void createVietQrInvoice()}
                >
                  Tạo Hóa đơn Thanh toán VietQR
                </button>
                <button
                  type="button"
                  className="ui-btn-secondary"
                  data-testid="btn-check-vietqr-payment-status"
                  disabled={busy}
                  onClick={() => void checkPaymentStatus()}
                >
                  Tôi đã chuyển khoản — Kiểm tra trạng thái giao dịch
                </button>
                {import.meta.env.VITE_QA_WEBHOOK_SIMULATOR === 'true' && (
                  <button
                    type="button"
                    className="ui-btn-secondary"
                    data-testid="btn-confirm-vietqr-webhook"
                    disabled={busy}
                    onClick={() => void confirmVietQrWebhook()}
                  >
                    Xác nhận Thanh toán Tự động (Webhook PayOS / SePay)
                  </button>
                )}
                <button type="button" className="ui-btn-secondary" disabled={busy} onClick={() => void loadSubscription()}>
                  Làm mới Hạn mức
                </button>
              </div>
              <div style={{ fontSize: 13, color: '#0f172a' }}>
                <strong>{vietQrInvoice.transferReference || 'Chưa tạo mã chuyển khoản'}</strong> • <span>{vietQrInvoice.gatewayStatus}</span>
              </div>
            </div>
            <div>
              <BarcodeSvg
                value={vietQrInvoice.qrPayload || `VIETQR-${selectedPlanCode}`}
                format="QR"
                captionLabel="Mã VietQR Tự động"
              />
            </div>
          </div>
        </div>
      </UiCard>
    </UiPage>
  );
};

export default SaasBilling;
