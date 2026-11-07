import React, { useEffect, useState } from 'react';
import { Link, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom';
import type { LucideIcon } from 'lucide-react';
import {
  Activity, Building2, Clock, ExternalLink, Gauge, Headset, LayoutDashboard,
  LogOut, Plug, Receipt, ScrollText, ShieldAlert,
} from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import {
  clearActiveImpersonationState, getActiveImpersonationState,
  normalizeTenantDisplayName, type ActiveImpersonationState,
} from '../config/portalMode';
import { formatUserDisplayNameVi } from '../services/permissionPresentation';
import {
  formatPlatformRoleBadgeLabel,
  PlatformIdentityContext,
  resolveAuthenticatedPlatformIdentity,
  type PlatformLayoutOutletContext,
  type PlatformRoleCode,
} from '../modules/platform/platformBillingHelpers';
import '../ui/production-ui.css';
import '../pages/PlatformControlCenter.css';

interface PlatformNavItem {
  path: string;
  aliases?: string[];
  label: string;
  heading: string;
  icon: LucideIcon;
  testId: string;
  requiredPermission: string;
}

const PLATFORM_NAV_ITEMS: PlatformNavItem[] = [
  { path: '/platform', label: 'Tổng quan nền tảng', heading: 'Tổng quan nền tảng', icon: LayoutDashboard, testId: 'nav-platform-dashboard', requiredPermission: 'platform.dashboard.read' },
  { path: '/platform/tenants', label: 'Quản lý doanh nghiệp', heading: 'Quản lý doanh nghiệp', icon: Building2, testId: 'nav-platform-tenants', requiredPermission: 'platform.tenant.read' },
  { path: '/platform/billing', label: 'Hóa đơn thuê bao SaaS', heading: 'Hóa đơn thuê bao SaaS', icon: Receipt, testId: 'nav-platform-billing', requiredPermission: 'platform.dashboard.read' },
  { path: '/platform/quotas', label: 'Hạn mức tài nguyên', heading: 'Hạn mức tài nguyên', icon: Gauge, testId: 'nav-platform-quotas', requiredPermission: 'platform.tenant.read' },
  { path: '/platform/integrations', label: 'Cổng tích hợp nền tảng', heading: 'Cổng tích hợp nền tảng', icon: Plug, testId: 'nav-platform-integrations', requiredPermission: 'platform.integration.manage' },
  { path: '/platform/monitoring', aliases: ['/platform/workers'], label: 'Giám sát tiến trình nền', heading: 'Giám sát tiến trình nền', icon: Activity, testId: 'nav-platform-workers', requiredPermission: 'platform.dashboard.read' },
  { path: '/platform/support', label: 'Hỗ trợ kỹ thuật', heading: 'Hỗ trợ kỹ thuật', icon: Headset, testId: 'nav-platform-support', requiredPermission: 'platform.support.impersonate' },
  { path: '/platform/audit-logs', aliases: ['/platform/audit'], label: 'Nhật ký kiểm toán', heading: 'Nhật ký kiểm toán', icon: ScrollText, testId: 'nav-platform-audit', requiredPermission: 'platform.audit.read' },
];

const computeRemainingSeconds = (expiresAtUtc?: string): number => {
  if (!expiresAtUtc) return 1800;
  const diff = Math.floor((new Date(expiresAtUtc).getTime() - Date.now()) / 1000);
  return Number.isNaN(diff) || diff <= 0 ? 1800 : diff;
};

export const PlatformControlCenterLayout: React.FC<{ children?: React.ReactNode }> = ({ children }) => {
  const location = useLocation();
  const navigate = useNavigate();
  const [authStatus, setAuthStatus] = useState<'checking' | 'authorized' | 'unauthenticated' | 'forbidden'>('checking');
  const [activeRole, setActiveRole] = useState<PlatformRoleCode>('SuperAdmin');
  const [actorUsername, setActorUsername] = useState<string>('');
  const [permissions, setPermissions] = useState<string[]>([]);
  const [impersonationState, setImpersonationState] = useState<ActiveImpersonationState | null>(() => getActiveImpersonationState());
  const [isTenantSuspended, setIsTenantSuspended] = useState<boolean>(() =>
    typeof window !== 'undefined' && window.localStorage?.getItem('tenantLifecycleStatus') === 'SUSPENDED',
  );
  const [remainingSeconds, setRemainingSeconds] = useState<number>(() => computeRemainingSeconds(getActiveImpersonationState()?.expiresAtUtc));

  useEffect(() => {
    let cancelled = false;
    setAuthStatus('checking');
    apiClient
      .get<Record<string, unknown>>('/api/v1/auth/me')
      .then((res) => {
        if (cancelled) return;
        const resolved = resolveAuthenticatedPlatformIdentity(res?.data);
        if (resolved.status === 'authorized' && resolved.activeRole) {
          setActiveRole(resolved.activeRole);
          setActorUsername(resolved.username);
          setPermissions(resolved.permissions);
          setAuthStatus('authorized');
          if (typeof window !== 'undefined') {
            window.dispatchEvent(new CustomEvent('erp-platform-role-changed', { detail: resolved.activeRole }));
          }
        } else {
          setAuthStatus(resolved.status === 'forbidden' ? 'forbidden' : 'unauthenticated');
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const status = (err as { response?: { status?: number } })?.response?.status;
        setAuthStatus(status === 403 ? 'forbidden' : 'unauthenticated');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const syncImp = () => {
      setImpersonationState(getActiveImpersonationState());
      setIsTenantSuspended(typeof window !== 'undefined' && window.localStorage?.getItem('tenantLifecycleStatus') === 'SUSPENDED');
    };
    syncImp();
    window.addEventListener('erp-impersonation-changed', syncImp);
    window.addEventListener('storage', syncImp);
    return () => {
      window.removeEventListener('erp-impersonation-changed', syncImp);
      window.removeEventListener('storage', syncImp);
    };
  }, [location.pathname]);

  useEffect(() => {
    if (!impersonationState) return;
    setRemainingSeconds(computeRemainingSeconds(impersonationState.expiresAtUtc));
    const timer = window.setInterval(() => {
      setRemainingSeconds((prev) => {
        if (prev <= 1) {
          clearActiveImpersonationState();
          setImpersonationState(null);
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => window.clearInterval(timer);
  }, [impersonationState]);

  if (authStatus === 'checking') {
    return (
      <main className="production-ui" style={{ padding: 24 }}>
        <div role="status" data-testid="platform-auth-loading" style={{ fontSize: 13.5, fontWeight: 600, color: '#334155' }}>
          Đang xác thực phiên Quản trị nền tảng...
        </div>
      </main>
    );
  }

  if (authStatus === 'unauthenticated') {
    return <Navigate to="/login" replace />;
  }

  if (authStatus === 'forbidden') {
    return (
      <main className="production-ui" style={{ padding: 32 }}>
        <div
          role="alert"
          data-testid="platform-domain-forbidden-alert"
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            gap: 10,
            padding: '16px 20px',
            borderRadius: 10,
            backgroundColor: '#fef2f2',
            border: '1px solid #fecaca',
            color: '#991b1b',
            fontWeight: 700,
            fontSize: 14,
          }}
        >
          <ShieldAlert size={18} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0 }} />
          <span>403 Không có quyền truy cập Trung tâm Quản trị Nền tảng (Yêu cầu miền bảo mật PLATFORM).</span>
          <code className="ui-err-code">403 Forbidden • PLATFORM_PERMISSION_REQUIRED</code>
        </div>
      </main>
    );
  }

  const formatCountdown = (sec: number) => `${String(Math.floor(sec / 60)).padStart(2, '0')}:${String(sec % 60).padStart(2, '0')}`;

  const normalizedPath = location.pathname.replace(/\/+$/, '') || '/platform';
  const activeNavItem =
    PLATFORM_NAV_ITEMS.find(
      (item) => item.path === normalizedPath || (item.aliases?.includes(normalizedPath) ?? false),
    ) ?? PLATFORM_NAV_ITEMS[0];

  const handleLogout = async () => {
    clearActiveImpersonationState();
    await logout();
    navigate('/login', { replace: true });
  };

  const outletContext: PlatformLayoutOutletContext = { activeRole, actorUsername, permissions };
  const visibleNavItems = PLATFORM_NAV_ITEMS.filter(
    (item) => activeRole === 'SuperAdmin' || permissions.includes(item.requiredPermission),
  );

  return (
    <div
      data-testid="platform-control-center-layout"
      className="production-ui platform-control-plane"
    >
      <a className="skip-link" href="#platform-main-content">Bỏ qua điều hướng</a>
      <aside
        data-testid="platform-sidebar"
        className="platform-sidebar"
      >
        <div style={{ padding: '20px 18px', borderBottom: '1px solid #1e293b', background: 'linear-gradient(135deg, #0f172a 0%, #1e1b4b 100%)' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
            <span style={{ backgroundColor: '#38bdf8', color: '#0f172a', fontWeight: 800, fontSize: 11, padding: '2px 8px', borderRadius: 999, letterSpacing: '0.02em' }}>
              Quản trị nền tảng
            </span>
          </div>
          <div style={{ fontSize: 16, fontWeight: 700, color: '#ffffff' }}>Quản trị Nền tảng ERP WMS</div>
          <div style={{ fontSize: 12, color: '#cbd5e1', marginTop: 4, lineHeight: 1.45 }}>
            Cổng điều hành dành cho chủ sở hữu SaaS
          </div>
        </div>

        <nav aria-label="Điều hướng Quản trị nền tảng" style={{ padding: '12px 10px', flex: 1, display: 'flex', flexDirection: 'column', gap: 4 }}>
          {visibleNavItems.map((item) => {
            const isActive = normalizedPath === item.path || (item.aliases?.includes(normalizedPath) ?? false);
            const Icon = item.icon;
            return (
              <Link
                key={item.path}
                to={item.path}
                data-testid={item.testId}
                aria-label={item.label}
                title={item.label}
                aria-current={isActive ? 'page' : undefined}
                className={`platform-sidebar-nav-link${isActive ? ' is-active' : ''}`}
              >
                <Icon size={16} strokeWidth={2} aria-hidden="true" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </nav>

        <div style={{ padding: '14px 16px', borderTop: '1px solid #1e293b', backgroundColor: '#020617', fontSize: 12 }}>
          <div style={{ color: '#cbd5e1', marginBottom: 4 }}>Tài khoản quản trị nền tảng</div>
          <div data-testid="platform-sidebar-actor" style={{ fontWeight: 600, color: '#f8fafc', marginBottom: 10 }}>
            {formatUserDisplayNameVi(actorUsername, activeRole)}
          </div>
          <button
            type="button"
            onClick={() => void handleLogout()}
            data-testid="btn-platform-logout"
            aria-label="Đăng xuất khỏi Quản trị nền tảng"
            title="Đăng xuất khỏi Quản trị nền tảng"
            className="platform-btn platform-btn-logout"
            style={{ width: '100%' }}
          >
            <LogOut size={14} strokeWidth={2} aria-hidden="true" />
            <span>Đăng xuất</span>
          </button>
        </div>
      </aside>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        {impersonationState ? (
          <div
            role="status"
            data-testid="active-impersonation-banner"
            className="platform-sticky-impersonation-bar"
            style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}
          >
            <span data-testid="platform-sticky-impersonation-bar" style={{ display: 'none' }} />
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', minWidth: 0 }}>
              <ShieldAlert size={16} strokeWidth={2} aria-hidden="true" color="#b45309" style={{ flexShrink: 0 }} />
              <span>
                {isTenantSuspended
                  ? 'Đang trong phiên chẩn đoán chỉ đọc (Doanh nghiệp đang tạm khóa) — '
                  : 'Đang trong phiên hỗ trợ kỹ thuật chỉ đọc: '}
                <strong>{impersonationState.tenantCode || `TENANT-${String(impersonationState.tenantId).padStart(3, '0')}`}</strong>{' '}
                ({normalizeTenantDisplayName(impersonationState.tenantName)}) • Ticket:{' '}
                <strong>{impersonationState.ticketCode}</strong> •{' '}
              </span>
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontWeight: 700, color: '#b45309', flexShrink: 0, whiteSpace: 'nowrap' }}>
                <Clock size={14} strokeWidth={2} aria-hidden="true" />
                <strong data-testid="impersonation-countdown-timer">Thời gian còn lại: {formatCountdown(remainingSeconds)}</strong>
              </span>
            </div>
            <button
              type="button"
              data-testid="btn-stop-impersonation"
              aria-label="Kết thúc phiên hỗ trợ kỹ thuật"
              title="Kết thúc phiên hỗ trợ kỹ thuật"
              className="platform-btn platform-btn-danger"
              style={{ padding: '4px 10px', fontSize: 12, whiteSpace: 'nowrap', flexShrink: 0 }}
              onClick={() => {
                void apiClient.post('/api/v1/permissions/support-impersonation/stop', {}).catch(() => undefined);
                clearActiveImpersonationState();
                setImpersonationState(null);
              }}
            >
              Kết thúc phiên
            </button>
          </div>
        ) : null}

        <header
          data-testid="platform-header"
          style={{ position: 'sticky', top: 0, zIndex: 40, backgroundColor: '#ffffff', borderBottom: '1px solid #e2e8f0', padding: '8px 20px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 14, flexWrap: 'wrap' }}
        >
          <div>
            <h1 data-testid="platform-page-heading" style={{ margin: 0, fontSize: 18, fontWeight: 800, color: '#0f172a', lineHeight: 1.25 }}>
              {activeNavItem.heading}
            </h1>
            <div style={{ fontSize: 12, color: '#475569', marginTop: 2 }}>
              Trung tâm điều hành SaaS đa thuê bao • Mọi thao tác quản trị đều ghi nhật ký bất biến
            </div>
          </div>
          <div className="platform-header-actions" style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', minWidth: 0 }}>
            <span data-testid="platform-domain-badge" style={{ display: 'none' }}>
              Miền bảo mật: Quản trị nền tảng
            </span>
            <span
              data-testid="platform-role-readonly-badge"
              className="ui-readonly-pill"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                height: 30,
                padding: '4px 12px',
                borderRadius: 999,
                backgroundColor: '#F1F5F9',
                border: '1px solid #E2E8F0',
                boxShadow: 'none',
                cursor: 'default',
                color: '#334155',
                fontSize: 12,
                fontWeight: 600,
                whiteSpace: 'nowrap',
              }}
            >
              {formatPlatformRoleBadgeLabel(activeRole)}
            </span>
            <Link
              to="/dashboard"
              data-testid="btn-switch-to-customer-portal"
              aria-label="Mở cổng khách hàng"
              title="Mở cổng khách hàng"
              className="platform-btn platform-btn-secondary"
              style={{ textDecoration: 'none', whiteSpace: 'nowrap' }}
            >
              <ExternalLink size={15} strokeWidth={2} aria-hidden="true" />
              <span>Mở cổng khách hàng</span>
            </Link>
          </div>
        </header>

        <main id="platform-main-content" tabIndex={-1} style={{ flex: 1, padding: '10px 20px' }}>
          <div className="platform-main-inner" style={{ maxWidth: 1480, width: '100%', marginLeft: 'auto', marginRight: 'auto', boxSizing: 'border-box' }}>
            <PlatformIdentityContext.Provider value={outletContext}>
              {children ?? <Outlet context={outletContext} />}
            </PlatformIdentityContext.Provider>
          </div>
        </main>
      </div>
    </div>
  );
};

export default PlatformControlCenterLayout;
