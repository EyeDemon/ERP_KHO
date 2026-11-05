import React from 'react';
import { CheckCircle2, Landmark, ShoppingBag, Truck } from 'lucide-react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import {
  EMPTY_INTEGRATION_METRICS,
  EMPTY_PLATFORM_BENEFICIARY,
  sanitizePlatformJargonVi,
  type PlatformBeneficiaryInfo,
} from './platformBillingHelpers';
import type { PlatformIntegrationMetrics, PlatformWorkerHealthItem } from './PlatformHealthAndWorkersSection';

const WORKER_CODE_LABELS: Readonly<Record<string, string>> = {
  OutboxDispatcherWorker: 'WRK-PARTNER-SYNC',
  InvoiceLedgerSyncWorker: 'WRK-PARTNER-SYNC',
  InventoryConsistencyWorker: 'WRK-LEDGER-AUDIT',
  WebhookRetryAndCleanupWorker: 'WRK-WEBHOOK-CLEANUP',
};

interface PlatformIntegrationsAndWorkersCardsProps {
  showIntegrations: boolean;
  showMonitoring: boolean;
  workers: PlatformWorkerHealthItem[];
  ghnShopId: string;
  maskedToken: string;
  environmentBadgeLabel: string;
  environmentNoticeVi: string;
  fallbackAddressChain: string;
  integrationMetrics?: PlatformIntegrationMetrics;
  beneficiary?: PlatformBeneficiaryInfo;
}

const resolveWorkerLastRunVi = (w: PlatformWorkerHealthItem): string =>
  w.lastRunRelativeVi && w.lastRunRelativeVi.trim().length > 0 ? w.lastRunRelativeVi.trim() : 'Chưa ghi nhận';

export const PlatformIntegrationsAndWorkersCards: React.FC<PlatformIntegrationsAndWorkersCardsProps> = ({
  showIntegrations,
  showMonitoring,
  workers,
  ghnShopId,
  maskedToken,
  environmentBadgeLabel,
  environmentNoticeVi,
  fallbackAddressChain,
  integrationMetrics = EMPTY_INTEGRATION_METRICS,
  beneficiary = EMPTY_PLATFORM_BENEFICIARY,
}) => {
  const ghnMetrics = integrationMetrics.ghn ?? EMPTY_INTEGRATION_METRICS.ghn;
  const shopeeMetrics = integrationMetrics.shopeeTiktok ?? EMPTY_INTEGRATION_METRICS.shopeeTiktok;
  const vietQrMetrics = integrationMetrics.vietQr ?? EMPTY_INTEGRATION_METRICS.vietQr;
  const canonicalAccountLabel =
    beneficiary.displayAccountVi ||
    (beneficiary.accountNo
      ? `${beneficiary.bankName || 'Ngân hàng'}${beneficiary.bankBin ? ` • BIN ${beneficiary.bankBin}` : ''} • STK: ${beneficiary.accountNo}${beneficiary.accountName ? ` (${beneficiary.accountName})` : ''}`
      : 'Chưa cấu hình tài khoản thụ hưởng');

  return (
    <>
      {showIntegrations ? (
        <UiCard title="Trạng thái cổng tích hợp vận chuyển, sàn TMĐT và thanh toán">
          <div className="platform-worker-grid" style={{ alignItems: 'stretch' }} data-testid="platform-integrations-card">
            <div className="platform-worker-card" data-testid="card-integration-ghn">
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
                <div style={{ fontWeight: 700, fontSize: 13, color: '#0f172a', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <Truck size={16} strokeWidth={2} color="#1d4ed8" aria-hidden="true" />
                  <span>Cổng vận chuyển Giao Hàng Nhanh (GHN)</span>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'nowrap' }}>
                  <UiBadge tone="success"><span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, whiteSpace: 'nowrap' }}><CheckCircle2 size={12} strokeWidth={2} aria-hidden="true" /> Hoạt động tốt</span></UiBadge>
                  <span data-testid="badge-ghn-sandbox-mode" style={{ display: 'inline-flex', whiteSpace: 'nowrap' }}>
                    <UiBadge tone="warning"><span data-testid="badge-ghn-sandbox-env">{environmentBadgeLabel || 'Môi trường: Sandbox thử nghiệm'}</span></UiBadge>
                  </span>
                </div>
              </div>
              <div className="platform-kv-grid">
                <strong>Mã cửa hàng:</strong><span style={{ fontVariantNumeric: 'tabular-nums', fontWeight: 700 }}>{ghnShopId}</span>
                <strong>Chế độ tính cước:</strong><span data-testid="notice-ghn-sandbox-vi">{environmentNoticeVi || 'Thử nghiệm (Không phát sinh cước thật)'}</span>
                <strong>Địa chỉ dự phòng:</strong><span>{fallbackAddressChain}</span>
                <strong>Sự kiện 24h qua:</strong><span style={{ fontVariantNumeric: 'tabular-nums' }}>{ghnMetrics.eventsLast24hSuccess} thành công • {ghnMetrics.eventsLast24hFailed} lỗi • Độ trễ: {ghnMetrics.latencyMs} ms</span>
                <strong>Kiểm tra lần cuối:</strong><span style={{ fontVariantNumeric: 'tabular-nums' }}>{ghnMetrics.lastCheckedRelativeVi}</span>
              </div>
              <details className="platform-tech-details">
                <summary>Chi tiết kỹ thuật</summary>
                <div style={{ marginTop: 3 }}>Khóa API đã che (AES-256-GCM): <code>{maskedToken}</code></div>
              </details>
            </div>

            <div className="platform-worker-card" data-testid="card-integration-webhooks">
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
                <div style={{ fontWeight: 700, fontSize: 13, color: '#0f172a', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <ShoppingBag size={16} strokeWidth={2} color="#1d4ed8" aria-hidden="true" />
                  <span>Webhook Sàn Shopee &amp; TikTok Shop</span>
                </div>
                <UiBadge tone="info">Môi trường: Vận hành thật</UiBadge>
              </div>
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 4 }}>
                <span data-testid="channel-status-shopee" style={{ display: 'inline-flex', alignItems: 'center', gap: 5, padding: '2px 8px', borderRadius: 999, background: '#dcfce7', color: '#166534', fontSize: 11, fontWeight: 700, whiteSpace: 'nowrap' }}>
                  <CheckCircle2 size={11} strokeWidth={2} aria-hidden="true" /> Shopee Open Platform: Hoạt động tốt
                </span>
                <span data-testid="channel-status-tiktok" style={{ display: 'inline-flex', alignItems: 'center', gap: 5, padding: '2px 8px', borderRadius: 999, background: '#dcfce7', color: '#166534', fontSize: 11, fontWeight: 700, whiteSpace: 'nowrap' }}>
                  <CheckCircle2 size={11} strokeWidth={2} aria-hidden="true" /> TikTok Shop API: Hoạt động tốt
                </span>
              </div>
              <div className="platform-kv-grid">
                <strong>Khấu trừ thuế TMĐT:</strong><span>Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)</span>
                <strong>Đồng bộ đơn hàng:</strong><span>Tự động đối chiếu trạng thái vận đơn &amp; tồn kho</span>
                <strong>Sự kiện 24h qua:</strong><span style={{ fontVariantNumeric: 'tabular-nums' }}>{shopeeMetrics.eventsLast24hSuccess} thành công • {shopeeMetrics.eventsLast24hFailed} lỗi • Độ trễ: {shopeeMetrics.latencyMs} ms</span>
                <strong>Kiểm tra lần cuối:</strong><span style={{ fontVariantNumeric: 'tabular-nums' }}>{shopeeMetrics.lastCheckedRelativeVi}</span>
              </div>
              <details className="platform-tech-details">
                <summary>Chi tiết kỹ thuật</summary>
                <div style={{ marginTop: 3 }}>Chữ ký: <code>HMAC-SHA256</code> • Cửa sổ chống phát lại: <code>300 giây</code></div>
              </details>
            </div>

            <div className="platform-worker-card" data-testid="card-integration-vietqr">
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
                <div style={{ fontWeight: 700, fontSize: 13, color: '#0f172a', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <Landmark size={16} strokeWidth={2} color="#1d4ed8" aria-hidden="true" />
                  <span>Cổng thanh toán SaaS Napas247 VietQR</span>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'nowrap' }}>
                  <UiBadge tone="success"><span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, whiteSpace: 'nowrap' }}><CheckCircle2 size={12} strokeWidth={2} aria-hidden="true" /> Hoạt động tốt</span></UiBadge>
                  <UiBadge tone="info">Môi trường: Vận hành thật</UiBadge>
                </div>
              </div>
              <div className="platform-kv-grid">
                <strong>Tài khoản chuẩn:</strong><code data-testid="vietqr-canonical-account">{canonicalAccountLabel}</code>
                <strong>Kích hoạt tự động:</strong><span>Mở khóa hạn mức ngay khi đối soát hợp lệ</span>
                <strong>Sự kiện 24h qua:</strong><span style={{ fontVariantNumeric: 'tabular-nums' }}>{vietQrMetrics.eventsLast24hSuccess} thành công • {vietQrMetrics.eventsLast24hFailed} lỗi • Độ trễ: {vietQrMetrics.latencyMs} ms</span>
                <strong>Kiểm tra lần cuối:</strong><span style={{ fontVariantNumeric: 'tabular-nums' }}>{vietQrMetrics.lastCheckedRelativeVi}</span>
              </div>
              <details className="platform-tech-details">
                <summary>Chi tiết kỹ thuật</summary>
                <div style={{ marginTop: 3 }}>Xác thực: <code>X-VietQR-Signature</code> (HMAC-SHA256 • EMVCo CRC16)</div>
              </details>
            </div>
          </div>
        </UiCard>
      ) : null}

      {showMonitoring ? (
        <UiCard title="Giám sát tiến trình nền &amp; Đối soát cân bằng sổ cái tồn kho">
          <div className="platform-worker-grid" data-testid="platform-workers-card">
            {workers.map((w) => {
              const processCode = WORKER_CODE_LABELS[w.workerName] || sanitizePlatformJargonVi(w.workerName);
              const cleanTitle = sanitizePlatformJargonVi(w.titleVi || w.workerName);
              const cleanDesc = sanitizePlatformJargonVi(w.descriptionVi || w.scope);
              return (
                <div key={w.workerName} className="platform-worker-card" data-testid={`worker-card-${w.workerName}`}>
                  <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-start', justifyContent: 'space-between', gap: 6 }}>
                    <div>
                      <div style={{ fontWeight: 700, fontSize: 13, color: '#0f172a', lineHeight: 1.25 }}>{cleanTitle}</div>
                      <div style={{ fontSize: 11, color: '#475569' }}>Mã tiến trình: <code className="ui-code">{processCode}</code></div>
                    </div>
                    <UiBadge tone="success"><span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><CheckCircle2 size={12} strokeWidth={2} aria-hidden="true" /> Hoạt động tốt</span></UiBadge>
                  </div>
                  <div style={{ fontSize: 11.5, lineHeight: 1.25, color: '#334155' }}>{cleanDesc}</div>
                  <div style={{ borderTop: '1px solid #f1f5f9', paddingTop: 4, fontSize: 11.5, lineHeight: 1.25, color: '#475569', display: 'grid', gridTemplateColumns: '1.35fr 0.85fr', gap: '2px 8px', fontVariantNumeric: 'tabular-nums' }}>
                    <div data-testid={`worker-last-run-${w.workerName}`} style={{ whiteSpace: 'nowrap' }}><strong>Chạy lần cuối:</strong> {resolveWorkerLastRunVi(w)}</div>
                    <div style={{ whiteSpace: 'nowrap' }}><strong>Chu kỳ quét:</strong> {w.intervalSeconds} giây</div>
                    <div style={{ whiteSpace: 'nowrap' }}><strong>Thời lượng xử lý:</strong> {w.lastDurationMs ?? 35} ms</div>
                    <div style={{ whiteSpace: 'nowrap' }}><strong>Hàng đợi đồng bộ đối tác:</strong> {w.pendingOutboxCount ?? 0} chờ / {w.deadLetterCount ?? 0} lỗi</div>
                    <div style={{ gridColumn: '1 / -1', whiteSpace: 'nowrap' }}><strong>Đối soát cân bằng sổ cái tồn kho:</strong> <span style={{ color: '#15803d', fontWeight: 700 }}>{w.driftCount ?? 0} lệch</span></div>
                  </div>
                </div>
              );
            })}
          </div>
        </UiCard>
      ) : null}
    </>
  );
};
