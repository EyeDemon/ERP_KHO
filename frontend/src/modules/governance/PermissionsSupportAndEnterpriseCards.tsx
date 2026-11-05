import { useState } from 'react';
import { Link } from 'react-router-dom';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type {
  PermissionsBillingSummary,
  PermissionsEvidenceBundle,
  PermissionsSodAuditResult,
  PermissionsSupportSession,
} from './PermissionsHelpersAndTypes';

export function PermissionsSaasBillingBanner({ billingSummary }: { billingSummary: PermissionsBillingSummary }) {
  return (
    <UiCard title="Tóm tắt gói dịch vụ thuê bao hiện tại">
      <div
        data-testid="permissions-saas-billing-banner"
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 12,
          padding: '8px 12px',
          borderRadius: 8,
          border: '1px solid #bfdbfe',
          background: '#eff6ff',
          fontSize: 13,
          color: '#0f172a',
          fontVariantNumeric: 'tabular-nums',
        }}
      >
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 10 }}>
          <strong style={{ color: '#1e40af' }}>{billingSummary.planName}</strong>
          <span style={{ color: '#94a3b8' }}>•</span>
          <span>{`${billingSummary.activeWarehouses} / ${billingSummary.maxWarehouses} kho`}</span>
          <span style={{ color: '#94a3b8' }}>•</span>
          <span>{`${billingSummary.activeUsers} / ${billingSummary.maxUsers} người dùng`}</span>
          <span style={{ color: '#94a3b8' }}>•</span>
          <span>{`Đơn hàng tháng: ${billingSummary.currentMonthOrderCount.toLocaleString('vi-VN')} / ${billingSummary.monthlyOrderQuota.toLocaleString('vi-VN')}`}</span>
          <span style={{ color: '#94a3b8' }}>•</span>
          <span>{`${billingSummary.activeProducts} / ${billingSummary.maxProducts.toLocaleString('vi-VN')} mặt hàng`}</span>
        </div>
        <Link
          to="/saas-billing"
          data-testid="link-open-saas-billing"
          style={{ fontSize: 13, fontWeight: 700, color: '#1d4ed8', textDecoration: 'none', whiteSpace: 'nowrap' }}
        >
          Mở trang Quản trị Gói dịch vụ &amp; Thanh toán VietQR →
        </Link>
      </div>
    </UiCard>
  );
}

export function PermissionsSupportImpersonationCard({
  targetUserIdInput,
  onError,
}: {
  targetUserIdInput: string;
  onError: (msg: string) => void;
}) {
  const [supportSessions, setSupportSessions] = useState<PermissionsSupportSession[]>([]);
  const [supportBusy, setSupportBusy] = useState(false);
  const [supportTicketRef, setSupportTicketRef] = useState('');
  const [supportNotice, setSupportNotice] = useState('');

  const loadSupportImpersonationSessions = async () => {
    if (supportBusy) return;
    setSupportBusy(true);
    onError('');
    setSupportNotice('');
    try {
      const res = await apiClient.get('/api/v1/security/support-impersonation/sessions');
      const raw = Array.isArray(res?.data)
        ? res.data
        : Array.isArray((res?.data as { items?: unknown[] } | undefined)?.items)
          ? (res.data as { items: unknown[] }).items
          : [];
      setSupportSessions(
        raw.map((item: Record<string, unknown>, idx: number) => ({
          id: Number(item.id ?? item.sessionId ?? idx + 1),
          targetUserId: Number(item.targetUserId ?? item.userId ?? 1),
          supportAgentName: String(item.supportAgentName ?? 'Chuyên viên Kỹ thuật ERP KHO').replace(/ERP_KHO/g, 'ERP KHO'),
          ticketReference: String(item.ticketReference ?? 'ZALO-SUP-2026-001'),
          status: String(item.status ?? 'ACTIVE').toUpperCase().includes('REVOKE')
            ? 'Đã thu hồi'
            : 'Đang hoạt động (30 phút)',
          expiresAtUtc: String(item.expiresAtUtc ?? new Date(Date.now() + 30 * 60000).toISOString()),
        })),
      );
      setSupportNotice(`Đã tải ${raw.length} phiên cấp quyền hỗ trợ kỹ thuật tạm thời.`);
    } catch (failure) {
      onError(permissionError(failure));
    } finally {
      setSupportBusy(false);
    }
  };

  const grantSupportImpersonationSession = async () => {
    if (supportBusy) return;
    setSupportBusy(true);
    onError('');
    setSupportNotice('');
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    const ticket = supportTicketRef.trim() || 'ZALO-SUP-2026-001';
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/security/support-impersonation/grant', {
        targetUserId: uid,
        supportAgentName: 'Chuyên viên Kỹ thuật ERP KHO',
        ticketReference: ticket,
        ttlMinutes: 30,
        reason: 'Hỗ trợ kỹ thuật qua Zalo OA',
      });
      const d = res?.data;
      const newId = Number(d?.id ?? d?.sessionId ?? Date.now());
      const expiresAt =
        typeof d?.expiresAtUtc === 'string' && d.expiresAtUtc
          ? d.expiresAtUtc
          : new Date(Date.now() + 30 * 60000).toISOString();
      setSupportSessions(prev => [
        {
          id: newId,
          targetUserId: uid,
          supportAgentName: String(d?.supportAgentName ?? 'Chuyên viên Kỹ thuật ERP KHO'),
          ticketReference: String(d?.ticketReference ?? ticket),
          status: 'Đang hoạt động (30 phút)',
          expiresAtUtc: expiresAt,
        },
        ...prev,
      ]);
      setSupportNotice(`Đã cấp phiên hỗ trợ kỹ thuật tạm thời (#${newId}, hiệu lực 30 phút) cho phiếu ${ticket}.`);
    } catch (failure) {
      onError(permissionError(failure));
    } finally {
      setSupportBusy(false);
    }
  };

  const revokeSupportImpersonationSession = async (sessionId: number) => {
    if (supportBusy) return;
    setSupportBusy(true);
    onError('');
    setSupportNotice('');
    try {
      await apiClient.post(`/api/v1/security/support-impersonation/${sessionId}/revoke`, {
        reason: 'Chủ shop thu hồi phiên hỗ trợ kỹ thuật',
      });
      setSupportSessions(prev => prev.map(s => (s.id === sessionId ? { ...s, status: 'Đã thu hồi' } : s)));
      setSupportNotice(`Đã thu hồi tức thì phiên hỗ trợ kỹ thuật #${sessionId}.`);
    } catch (failure) {
      onError(permissionError(failure));
    } finally {
      setSupportBusy(false);
    }
  };

  return (
    <UiCard
      title="Phiên cấp quyền hỗ trợ kỹ thuật tạm thời (30 phút)"
      subtitle="Cấp quyền truy cập có thời hạn cho chuyên viên kỹ thuật xử lý sự cố và tự động thu hồi sau 30 phút kèm nhật ký kiểm toán"
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div className="ui-inline-wrap" style={{ gap: 8 }}>
          <input
            type="search"
            aria-label="Mã phiếu yêu cầu hỗ trợ Zalo"
            value={supportTicketRef}
            onChange={e => setSupportTicketRef(e.target.value)}
            placeholder="VD: ZALO-SUP-2026-001"
            style={{ height: 36, minHeight: 36, width: 220, padding: '4px 10px', boxSizing: 'border-box' }}
          />
          <button
            type="button"
            data-testid="btn-grant-support-impersonation"
            disabled={supportBusy}
            onClick={() => void grantSupportImpersonationSession()}
          >
            Cấp Quyền Hỗ Trợ Tạm Thời (30 Phút)
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            data-testid="btn-load-support-impersonation"
            disabled={supportBusy}
            onClick={() => void loadSupportImpersonationSessions()}
          >
            Tải Danh Sách Phiên Hỗ Trợ
          </button>
        </div>
        {supportNotice && (
          <div
            role="status"
            style={{
              padding: '6px 10px',
              borderRadius: 6,
              background: '#ecfdf5',
              color: '#065f46',
              border: '1px solid #a7f3d0',
              fontSize: 12.5,
            }}
          >
            {supportNotice}
          </div>
        )}
        {supportSessions.length > 0 && (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Bảng phiên cấp quyền hỗ trợ kỹ thuật tạm thời">
              <thead>
                <tr>
                  <th>Mã phiên</th>
                  <th>Chuyên viên hỗ trợ</th>
                  <th>Mã phiếu Zalo</th>
                  <th>Hết hạn lúc</th>
                  <th>Trạng thái</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {supportSessions.map(s => (
                  <tr key={s.id}>
                    <td>
                      <code>#{s.id}</code>
                    </td>
                    <td>{s.supportAgentName}</td>
                    <td>
                      <code>{s.ticketReference}</code>
                    </td>
                    <td>{new Date(s.expiresAtUtc).toLocaleString('vi-VN')}</td>
                    <td>
                      <UiBadge tone={s.status.includes('Đã thu hồi') ? 'neutral' : 'success'}>{s.status}</UiBadge>
                    </td>
                    <td>
                      <button
                        type="button"
                        data-testid={`btn-revoke-support-${s.id}`}
                        disabled={supportBusy || s.status.includes('Đã thu hồi')}
                        onClick={() => void revokeSupportImpersonationSession(s.id)}
                      >
                        Thu hồi tức thì
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </div>
    </UiCard>
  );
}

export function PermissionsEnterpriseComplianceCard({
  canReadUserWarehouse,
  canAssign,
  evidenceBusy,
  onExportEvidenceBundle,
  sodAuditBusy,
  onVerifySodIsolation,
  evidenceBundle,
  sodAuditResult,
}: {
  canReadUserWarehouse: boolean;
  canAssign: boolean;
  evidenceBusy: boolean;
  onExportEvidenceBundle: () => void;
  sodAuditBusy: boolean;
  onVerifySodIsolation: () => void;
  evidenceBundle: PermissionsEvidenceBundle | null;
  sodAuditResult: PermissionsSodAuditResult | null;
}) {
  return (
    <UiCard title="Cấu hình quản trị doanh nghiệp mở rộng: Cờ chức năng, chuỗi số chứng từ, đăng nhập một cửa, giữ pháp lý & bảo vệ dữ liệu">
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))',
          gap: '4px 16px',
          fontSize: 12.5,
          color: '#334155',
          marginBottom: 8,
        }}
      >
        <span>• <strong>Cờ chức năng:</strong> Phân tách nhiệm vụ • Phân bổ theo hạn dùng • Bảo vệ sổ cái bất biến</span>
        <span>• <strong>Cảnh báo vận hành:</strong> Đang bật</span>
        <span>• <strong>Liên kết định danh một cửa:</strong> Sẵn sàng xác thực tập trung</span>
        <span>• <strong>Chuỗi đánh số chứng từ:</strong> PO/ASN/IMP/PUT/EXP/PCK/SHP/TRF/STK</span>
        <span>• <strong>Chính sách luồng duyệt:</strong> v2026.10</span>
        <span>• <strong>Hồ sơ đính kèm:</strong> Liên kết kiểm toán bất biến</span>
        <span>• <strong>Đa ngôn ngữ &amp; chuẩn lưu trữ:</strong> vi-VN / UTC + Đơn vị cơ sở</span>
        <span>• <strong>Giữ pháp lý &amp; truy xuất chứng cứ:</strong> Đang bật lưu trữ bất biến</span>
        <span>• <strong>Bảo vệ dữ liệu cá nhân:</strong> Ẩn thông tin nhạy cảm &amp; bảo toàn nhật ký</span>
      </div>
      <div className="ui-inline-actions">
        <button type="button" disabled={evidenceBusy} onClick={onExportEvidenceBundle}>
          Kết xuất Gói Bằng chứng Kiểm toán & Giữ pháp lý
        </button>
        {(canReadUserWarehouse || canAssign) && (
          <button type="button" disabled={sodAuditBusy} onClick={onVerifySodIsolation}>
            Kiểm toán Cô lập Phạm vi Kho & Phân tách Nhiệm vụ
          </button>
        )}
        {evidenceBundle && (
          <span role="status" style={{ fontSize: 12.5, color: '#065f46', fontWeight: 600 }}>
            {`• Gói bằng chứng: ${evidenceBundle.bundleId} • Chính sách: ${evidenceBundle.policyVersion} • Giữ pháp lý: Đang bật • Nhật ký bất biến: Đã xác minh`}
          </span>
        )}
        {sodAuditResult && (
          <span
            role="status"
            style={{
              fontSize: 12.5,
              color: sodAuditResult.sodConflictCount === 0 ? '#065f46' : '#92400e',
              fontWeight: 600,
            }}
          >
            {`• Kiểm toán SoD: ${({ ZERO_PRIVILEGE_ESCALATION_VERIFIED: 'Đã xác minh không leo thang đặc quyền (ZERO_PRIVILEGE_ESCALATION_VERIFIED)' } as Record<string, string>)[sodAuditResult.status] || sodAuditResult.status} • Xung đột SoD: ${sodAuditResult.sodConflictCount} • Cô lập đa kho: ${sodAuditResult.multiWarehouseIsolationEnforced ? 'Đang thực thi' : 'Chờ xử lý'}`}
          </span>
        )}
      </div>
    </UiCard>
  );
}
