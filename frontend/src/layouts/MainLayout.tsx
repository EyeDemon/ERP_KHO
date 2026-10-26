import { Outlet, Link, useLocation } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import type { LucideIcon } from 'lucide-react';
import {
  Boxes,
  CheckCircle,
  Circle,
  Clipboard,
  ClipboardCheck,
  Handshake,
  HelpCircle,
  Home,
  Layers3,
  Lock,
  LogOut,
  Menu,
  MapPinned,
  Map as MapIcon,
  Package,
  PackageCheck,
  PackageOpen,
  Repeat2,
  Ruler,
  Scale,
  ShoppingCart,
  Shield,
  Warehouse,
  Truck,
  X,
} from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import {
  beginPermissionRefresh,
  canViewApprovals,
  canViewStocktakes,
  hasPermission,
  setCurrentPermissions,
  usePermissionSet,
} from '../services/authorization';
import { readOnlyDemoPermissions, isReadOnlyDemoRuntime } from '../services/runtimeMode';
import {
  productionNavigation,
  productionSections,
  resolveProductionPage,
  type ProductionNavItem,
} from '../config/productionNavigation';
import './MainLayout.css';

const productionIcons: Record<string, LucideIcon> = {
  '/': Home,
  '/products': Package,
  '/warehouses': Warehouse,
  '/warehouse-structure': MapPinned,
  '/warehouse-map': MapIcon,
  '/warehouse-calendar': ClipboardCheck,
  '/dock-yard': Truck,
  '/units': Ruler,
  '/business-partners': Handshake,
  '/partner-debts': Scale,
  '/purchase-orders': ShoppingCart,
  '/asns': PackageCheck,
  '/import-receipts': Clipboard,
  '/putaway-tasks': PackageOpen,
  '/export-receipts': Package,
  '/stock-reservations': Lock,
  '/stock-allocations': Layers3,
  '/picking-tasks': ClipboardCheck,
  '/packing-sessions': PackageCheck,
  '/shipments': Truck,
  '/handover-manifests': Truck,
  '/backorders': ShoppingCart,
  '/inventory': Boxes,
  '/inventory-locks': Lock,
  '/inventory-movements': Repeat2,
  '/inventory-reversals': Repeat2,
  '/inventory-traceability': ClipboardCheck,
  '/inventory-reconciliation': Scale,
  '/stocktakes': ClipboardCheck,
  '/stock-transfers': Repeat2,
  '/cod-reconciliation': Scale,
  '/cost-valuation': Scale,
  '/onboarding-wizard': CheckCircle,
  '/saas-billing': Shield,
  '/approvals': CheckCircle,
  '/permissions': Shield,
};

const pdaBottomNavItems: { path: string; label: string; icon: LucideIcon; permission?: string; access?: 'always' | 'stocktake' }[] = [
  { path: '/', label: 'Tổng quan', icon: Home, access: 'always' },
  { path: '/import-receipts', label: 'Nhận hàng', icon: Clipboard, permission: 'receipt.read' },
  { path: '/putaway-tasks', label: 'Cất hàng', icon: PackageOpen, permission: 'putaway.read' },
  { path: '/picking-tasks', label: 'Lấy hàng', icon: ClipboardCheck, permission: 'picking.read' },
  { path: '/stocktakes', label: 'Kiểm kê', icon: Scale, access: 'stocktake' },
];

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const demoRuntime = isReadOnlyDemoRuntime();
  const pageMeta = resolveProductionPage(pathname);
  const mainRef = useRef<HTMLElement>(null);
  const previousPathRef = useRef(pathname);
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [isCompactViewport, setIsCompactViewport] = useState(() =>
    typeof window !== 'undefined' ? window.innerWidth <= 768 : false,
  );
  const [diagCopiedNotice, setDiagCopiedNotice] = useState('');
  const [complexityMode, setComplexityMode] = useState<'FULL' | 'EXPRESS'>(() => {
    if (typeof window !== 'undefined' && window.localStorage) {
      return window.localStorage.getItem('wmsComplexityMode') === 'EXPRESS' ? 'EXPRESS' : 'FULL';
    }
    return 'FULL';
  });

  const toggleComplexityMode = () => {
    setComplexityMode((current) => {
      const next = current === 'FULL' ? 'EXPRESS' : 'FULL';
      if (typeof window !== 'undefined' && window.localStorage) {
        window.localStorage.setItem('wmsComplexityMode', next);
      }
      return next;
    });
  };

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const syncViewport = () => setIsCompactViewport(window.innerWidth <= 768);
    syncViewport();
    window.addEventListener('resize', syncViewport);
    return () => window.removeEventListener('resize', syncViewport);
  }, []);

  useEffect(() => {
    let active = true;
    const revision = beginPermissionRefresh();

    if (demoRuntime) {
      setCurrentPermissions([...readOnlyDemoPermissions], revision);
      setIdentityState('ready');
      return () => { active = false; };
    }

    setIdentityState('loading');
    apiClient.get('/api/auth/me').then(response => {
      if (!active) return;
      if (response.data?.username) localStorage.setItem('username', String(response.data.username));
      if (response.data?.role) localStorage.setItem('role', String(response.data.role));
      const uid = response.data?.userId ?? response.data?.id;
      if (uid !== undefined && uid !== null) localStorage.setItem('userId', String(uid));
      setCurrentPermissions(response.data.permissions, revision);
      setIdentityState('ready');
    }).catch(() => {
      if (!active) return;
      setCurrentPermissions([], revision);
      setIdentityState('error');
    });
    return () => { active = false; };
  }, [demoRuntime]);

  useEffect(() => {
    const label = pageMeta?.label ?? 'ERP WMS';
    document.title = label + ' • ERP WMS';
  }, [pageMeta?.label]);

  useEffect(() => {
    if (previousPathRef.current !== pathname) {
      setMobileNavOpen(false);
      mainRef.current?.focus();
      previousPathRef.current = pathname;
    }
  }, [pathname]);

  useEffect(() => {
    if (!mobileNavOpen) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMobileNavOpen(false);
    };
    window.addEventListener('keydown', closeOnEscape);
    return () => window.removeEventListener('keydown', closeOnEscape);
  }, [mobileNavOpen]);

  const showStocktakes = demoRuntime || canViewStocktakes();
  const canShowProductionItem = (item: ProductionNavItem) => {
    if (complexityMode === 'EXPRESS' && item.expressMode === false && pathname !== item.path) {
      return false;
    }
    if (item.path === '/backorders') return hasPermission('backorder.read') || hasPermission('sales_order.read');
    if (item.permission) return hasPermission(item.permission);
    if (item.access === 'stocktake') return showStocktakes;
    if (item.access === 'approvals') return canViewApprovals();
    return true;
  };

  const resolvedTenantBadgeLabel = (() => {
    const raw = localStorage.getItem('tenantCode')?.trim();
    if (!raw || raw.toUpperCase() === 'DEFAULT' || raw.toUpperCase() === 'TENANT-1' || raw.toUpperCase() === 'MẶC ĐỊNH') {
      return 'Kho Trung tâm (WH01)';
    }
    return raw;
  })();

  const navLink = (
    to: string,
    label: string,
    options: { accent?: boolean; icon?: LucideIcon } = {},
  ) => {
    const active =
      pathname === to ||
      (to !== '/' && pathname.startsWith(to + '/')) ||
      (to === '/products' && pathname.startsWith('/categories')) ||
      (to === '/cost-valuation' && pathname.startsWith('/misa-exports'));
    const Icon = options.icon ?? Circle;

    return (
      <li className="sidebar-nav-item" key={to}>
        <Link
          to={to}
          className={'sidebar-nav-link' + (active ? ' active' : '') + (options.accent ? ' accent' : '')}
          aria-current={active ? 'page' : undefined}
        >
          <Icon className="sidebar-nav-icon" aria-hidden="true" />
          <span>{label}</span>
        </Link>
      </li>
    );
  };

  const sidebarHiddenOffCanvas = isCompactViewport && !mobileNavOpen;

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">Bỏ qua điều hướng</a>

      {mobileNavOpen && (
        <button
          type="button"
          className="sidebar-backdrop"
          aria-label="Đóng menu điều hướng"
          onClick={() => setMobileNavOpen(false)}
        />
      )}

      <aside
        id="system-sidebar"
        className={'app-sidebar' + (mobileNavOpen ? ' open' : '')}
        aria-label="Điều hướng hệ thống"
        aria-hidden={sidebarHiddenOffCanvas ? 'true' : undefined}
        inert={sidebarHiddenOffCanvas ? true : undefined}
      >
        <div className="app-sidebar-header">
          <h2 className="app-brand">ERP WMS</h2>
          <button type="button" className="sidebar-mobile-close" aria-label="Đóng menu điều hướng" onClick={() => setMobileNavOpen(false)}>
            <X aria-hidden="true" />
          </button>
        </div>
        <div className="app-brand-context">
          Hệ thống quản lý kho
        </div>

        <nav aria-label="Điều hướng nghiệp vụ">
          {productionSections.map((section) => {
            const items = productionNavigation.filter((item) => item.section === section && canShowProductionItem(item));
            if (items.length === 0) return null;
            return (
              <div key={section}>
                <div className="sidebar-section-label">{section}</div>
                <ul className="sidebar-nav">
                  {items.map((item) => navLink(item.path, item.label, { icon: productionIcons[item.path] }))}
                </ul>
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="app-main-column">
        <header className="app-topbar">
          <div className="topbar-leading">
            <button
              type="button"
              className="mobile-nav-toggle"
              aria-label={mobileNavOpen ? 'Đóng menu điều hướng' : 'Mở menu điều hướng'}
              aria-expanded={mobileNavOpen}
              aria-controls="system-sidebar"
              onClick={() => setMobileNavOpen(value => !value)}
            >
              {mobileNavOpen ? <X aria-hidden="true" /> : <Menu aria-hidden="true" />}
            </button>

            {pageMeta && (
              <div className="topbar-page-meta">
                <div className="topbar-eyebrow">{pageMeta.section}</div>
                <div className="topbar-title">{pageMeta.label}</div>
                <div className="topbar-description">{pageMeta.description}</div>
              </div>
            )}
          </div>

          <div className="topbar-actions">
            <button
              type="button"
              className="topbar-mode-toggle"
              data-testid="wms-complexity-mode-toggle"
              onClick={toggleComplexityMode}
              title="Chuyển đổi giữa Chế độ Rút gọn (Express) và Chế độ Đầy đủ (Full WMS)"
              style={{
                fontSize: 12,
                padding: '4px 10px',
                minHeight: 34,
                whiteSpace: 'nowrap',
                borderRadius: 6,
                border: complexityMode === 'EXPRESS' ? '1px solid #0284c7' : '1px solid #cbd5e1',
                background: complexityMode === 'EXPRESS' ? '#f0f9ff' : '#f8fafc',
                color: '#0f172a',
                fontWeight: 600,
              }}
            >
              {complexityMode === 'EXPRESS'
                ? '⚡ Chế độ Rút gọn (Express)'
                : '🏭 Chế độ Đầy đủ (Full WMS)'}
            </button>

            <details className="topbar-help-menu">
              <summary
                className="topbar-help-trigger"
                aria-label="Trợ giúp và công cụ chẩn đoán kỹ thuật"
                title="Trợ giúp & Chẩn đoán kỹ thuật"
              >
                <HelpCircle size={16} aria-hidden="true" />
                <span>Trợ giúp</span>
              </summary>
              <div className="topbar-help-popover">
                <div className="topbar-help-title">Hỗ trợ kỹ thuật & Chẩn đoán</div>
                <p className="topbar-help-desc">
                  Sao chép nhanh mã chẩn đoán phiên làm việc (Kho, Người dùng, Đường dẫn, Mã lỗi) để gửi bộ phận hỗ trợ.
                </p>
                <button
                  type="button"
                  data-testid="btn-copy-zalo-diagnostics"
                  aria-label="Sao chép thông tin chẩn đoán lỗi gửi hỗ trợ Zalo"
                  onClick={() => {
                    const lastErrorCode = localStorage.getItem('lastErrorCode') || 'NONE';
                    const lastCorrelationId = localStorage.getItem('lastCorrelationId') || 'CORR-2026-WMS';
                    const payload = `TenantId=${resolvedTenantBadgeLabel} | WarehouseId=${localStorage.getItem('activeWarehouseId') || 'WH01'} | UserId=${localStorage.getItem('userId') || '1'} | Route=${pathname} | LastErrorCode=${lastErrorCode} | CorrelationId=${lastCorrelationId}`;
                    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
                      void Promise.resolve(navigator.clipboard.writeText(payload)).catch(() => {});
                    }
                    setDiagCopiedNotice(`Đã sao chép mã chẩn đoán [${lastErrorCode} | ${lastCorrelationId}].`);
                  }}
                  style={{ fontSize: 12, padding: '6px 10px', width: '100%' }}
                >
                  Sao chép Chẩn đoán Lỗi 1-Click gửi Zalo
                </button>
                {diagCopiedNotice && (
                  <span
                    role="status"
                    data-testid="zalo-diagnostics-notice"
                    style={{ display: 'block', marginTop: 6, fontSize: 12, color: '#065f46', background: '#ecfdf5', padding: '4px 8px', borderRadius: 6, border: '1px solid #a7f3d0' }}
                  >
                    {diagCopiedNotice}
                  </span>
                )}
              </div>
            </details>

            {demoRuntime ? (
              <>
                <span className="runtime-badge">MÔI TRƯỜNG MÔ PHỎNG • PHÍA MÁY CHỦ MÔ PHỎNG</span>
                <span className="topbar-context-text">Giao diện hệ thống thật</span>
              </>
            ) : (
              <>
                <span className="runtime-badge" data-testid="topbar-tenant-badge" title="Chi nhánh / Kho làm việc hiện tại">
                  {resolvedTenantBadgeLabel}
                </span>
                <span className="topbar-context-text">{localStorage.getItem('username') || 'Người dùng'}</span>
                <button
                  type="button"
                  onClick={() => void logout()}
                  title="Đăng xuất"
                  aria-label="Đăng xuất"
                  className="logout-button"
                >
                  <LogOut size={20} aria-hidden="true" />
                </button>
              </>
            )}
          </div>
        </header>

        <main
          id="main-content"
          ref={mainRef}
          tabIndex={-1}
          className="production-ui app-content"
        >
          {demoRuntime && (
            <section role="note" className="demo-runtime-banner" aria-label="Thông tin môi trường demo">
              <div className="demo-runtime-copy">
                <div className="demo-runtime-kicker">Bản mô phỏng Vercel • Chỉ đọc</div>
                <div className="demo-runtime-title">Giao diện hệ thống thật đang chạy với bộ chuyển đổi API mô phỏng, chưa phải môi trường máy chủ thử nghiệm.</div>
                <div className="demo-runtime-detail">
                  GET dùng dữ liệu mô phỏng có kiểm soát; POST/PUT/DELETE bị chặn với mã 405 và dữ liệu không được lưu sau phiên kiểm thử.
                </div>
              </div>
            </section>
          )}

          {identityState === 'loading' ? (
            <p role="status">Đang xác minh quyền truy cập...</p>
          ) : identityState === 'error' ? (
            <p role="alert">Không thể xác minh quyền truy cập. Vui lòng tải lại.</p>
          ) : (
            <Outlet />
          )}
        </main>

        <nav
          className="app-bottom-nav"
          data-testid="pda-bottom-nav"
          aria-label="Điều hướng tác nghiệp nhanh PDA"
          aria-hidden={!isCompactViewport ? 'true' : undefined}
        >
          {pdaBottomNavItems
            .filter((item) => {
              if (item.permission) return hasPermission(item.permission);
              if (item.access === 'stocktake') return showStocktakes;
              return true;
            })
            .map((item) => {
              const active = pathname === item.path || (item.path !== '/' && pathname.startsWith(item.path + '/'));
              const Icon = item.icon;
              return (
                <Link
                  key={item.path}
                  to={item.path}
                  className={'app-bottom-nav-link' + (active ? ' active' : '')}
                  aria-current={active ? 'page' : undefined}
                >
                  <Icon size={18} aria-hidden="true" />
                  <span>{item.label}</span>
                </Link>
              );
            })}
        </nav>
      </div>
    </div>
  );
};

export default MainLayout;
