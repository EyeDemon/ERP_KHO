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
import { blueprintDemoReadPermissions, isBlueprintDemoRuntime } from '../services/runtimeMode';
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
  '/backorders': ShoppingCart,
  '/inventory': Boxes,
  '/inventory-locks': Lock,
  '/inventory-movements': Repeat2,
  '/inventory-reversals': Repeat2,
  '/inventory-traceability': ClipboardCheck,
  '/inventory-reconciliation': Scale,
  '/stocktakes': ClipboardCheck,
  '/stock-transfers': Repeat2,
  '/approvals': CheckCircle,
  '/permissions': Shield,
};

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const demoRuntime = isBlueprintDemoRuntime();
  const pageMeta = resolveProductionPage(pathname);
  const mainRef = useRef<HTMLElement>(null);
  const previousPathRef = useRef(pathname);
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  useEffect(() => {
    let active = true;
    const revision = beginPermissionRefresh();

    if (demoRuntime) {
      setCurrentPermissions([...blueprintDemoReadPermissions], revision);
      setIdentityState('ready');
      return () => { active = false; };
    }

    setIdentityState('loading');
    apiClient.get('/api/auth/me').then(response => {
      if (!active) return;
      setCurrentPermissions(response.data.permissions, revision);
      setIdentityState('ready');
    }).catch(() => {
      if (!active) return;
      setCurrentPermissions([], revision);
      setIdentityState('error');
    });
    return () => { active = false; };
  }, [demoRuntime, pathname]);

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
    if (item.permission) return hasPermission(item.permission);
    if (item.access === 'stocktake') return showStocktakes;
    if (item.access === 'approvals') return canViewApprovals();
    return true;
  };

  const navLink = (
    to: string,
    label: string,
    options: { accent?: boolean; icon?: LucideIcon } = {},
  ) => {
    const active = pathname === to || (to !== '/' && pathname.startsWith(to + '/'));
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

      <aside id="system-sidebar" className={'app-sidebar' + (mobileNavOpen ? ' open' : '')} aria-label="Điều hướng hệ thống">
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
            {demoRuntime ? (
              <>
                <span className="runtime-badge">MÔI TRƯỜNG MÔ PHỎNG • PHÍA MÁY CHỦ MÔ PHỎNG</span>
                <span className="topbar-context-text">Giao diện hệ thống thật</span>
              </>
            ) : (
              <>
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
      </div>
    </div>
  );
};

export default MainLayout;
