import { Outlet, Link, useLocation } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import type { LucideIcon } from 'lucide-react';
import {
  Boxes,
  CheckCircle,
  Circle,
  Clipboard,
  ClipboardCheck,
  Database,
  FlaskConical,
  Gauge,
  Handshake,
  Home,
  Layers3,
  Lock,
  LogOut,
  Menu,
  MapPinned,
  Map as MapIcon,
  Package,
  PackageOpen,
  Repeat2,
  Ruler,
  Scale,
  Search,
  Shield,
  Warehouse,
  X,
} from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import { erpWmsBlueprint } from '../config/erpWmsBlueprint';
import { mockUsers } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
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
  '/units': Ruler,
  '/business-partners': Handshake,
  '/import-receipts': Clipboard,
  '/putaway-tasks': PackageOpen,
  '/export-receipts': Package,
  '/stock-reservations': Lock,
  '/inventory': Boxes,
  '/inventory-reconciliation': Scale,
  '/stocktakes': ClipboardCheck,
  '/stock-transfers': Repeat2,
  '/approvals': CheckCircle,
  '/permissions': Shield,
};

const blueprintIcons: Record<string, LucideIcon> = {
  '/system-blueprint': Layers3,
  '/system-blueprint/search': Search,
  '/system-blueprint/coverage': Gauge,
  '/system-blueprint/mock-data': Database,
  '/system-blueprint/scenarios': FlaskConical,
};

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const blueprintMode = pathname.startsWith('/system-blueprint');
  const demoRuntime = isBlueprintDemoRuntime();
  const mockDemo = useMockDemo();
  const pageMeta = blueprintMode ? undefined : resolveProductionPage(pathname);
  const mainRef = useRef<HTMLElement>(null);
  const previousPathRef = useRef(pathname);
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  useEffect(() => {
    let active = true;
    const revision = beginPermissionRefresh();

    if (blueprintMode || demoRuntime) {
      setCurrentPermissions(demoRuntime ? [...blueprintDemoReadPermissions] : [], revision);
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
  }, [blueprintMode, demoRuntime, pathname]);

  useEffect(() => {
    const label = blueprintMode ? 'Bản đồ hệ thống' : (pageMeta?.label ?? 'ERP WMS');
    document.title = label + ' • ERP WMS';
  }, [blueprintMode, pageMeta?.label]);

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
    const active = pathname === to || (to !== '/system-blueprint' && pathname.startsWith(to + '/'));
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
          {blueprintMode ? 'System Blueprint / Demo' : 'Warehouse Management System'}
        </div>

        {blueprintMode ? (
          <>
            <Link to="/" className="sidebar-back-link">
              <Home className="sidebar-nav-icon" aria-hidden="true" />
              <span>Quay lại hệ thống thật</span>
            </Link>

            <div className="sidebar-section-label">Blueprint tools</div>
            <nav aria-label="Công cụ Blueprint">
              <ul className="sidebar-nav">
                {navLink('/system-blueprint', 'Bản đồ tổng thể', { accent: true, icon: blueprintIcons['/system-blueprint'] })}
                {navLink('/system-blueprint/search', 'Global Search', { icon: blueprintIcons['/system-blueprint/search'] })}
                {navLink('/system-blueprint/coverage', 'Coverage & Readiness', { icon: blueprintIcons['/system-blueprint/coverage'] })}
                {navLink('/system-blueprint/mock-data', 'Mock Data Lab', { icon: blueprintIcons['/system-blueprint/mock-data'] })}
                {navLink('/system-blueprint/scenarios', 'Golden Scenario Lab', { icon: blueprintIcons['/system-blueprint/scenarios'] })}
              </ul>
            </nav>

            <div className="sidebar-section-label">17 module groups</div>
            <nav aria-label="Module Blueprint">
              <ul className="sidebar-nav">
                {erpWmsBlueprint.map((module) => navLink('/system-blueprint/' + module.key, module.name, { icon: Circle }))}
              </ul>
            </nav>
          </>
        ) : (
          <>
            <Link to="/system-blueprint" className="blueprint-entry-link">
              <Layers3 className="sidebar-nav-icon" aria-hidden="true" />
              <span>Bản đồ hệ thống</span>
            </Link>

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
          </>
        )}
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

            {!blueprintMode && pageMeta && (
              <div className="topbar-page-meta">
                <div className="topbar-eyebrow">{pageMeta.section}</div>
                <div className="topbar-title">{pageMeta.label}</div>
                <div className="topbar-description">{pageMeta.description}</div>
              </div>
            )}

            {blueprintMode && (
              <>
                <span className="blueprint-badge">DEMO / MOCK • READ ONLY</span>
                <label className="persona-control">
                  <span>Persona mô phỏng</span>
                  <select
                    aria-label="Persona mô phỏng"
                    value={mockDemo.selectedUserCode}
                    onChange={(event) => mockDemo.setSelectedUserCode(event.target.value)}
                  >
                    {mockUsers.map((user) => (
                      <option key={user.code} value={user.code}>{user.name} • {user.role}</option>
                    ))}
                  </select>
                  <span>{mockDemo.allowedWarehouses.length} kho scope</span>
                </label>
              </>
            )}
          </div>

          <div className="topbar-actions">
            {blueprintMode ? (
              <span className="topbar-context-text">Blueprint runtime</span>
            ) : demoRuntime ? (
              <>
                <span className="runtime-badge">DEMO RUNTIME • MOCK BACKEND</span>
                <span className="topbar-context-text">Frontend production UI</span>
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
          className={(blueprintMode ? '' : 'production-ui ') + 'app-content'}
        >
          {demoRuntime && !blueprintMode && (
            <section role="note" className="demo-runtime-banner" aria-label="Thông tin môi trường demo">
              <div className="demo-runtime-copy">
                <div className="demo-runtime-kicker">Vercel Blueprint Demo • Read only</div>
                <div className="demo-runtime-title">Production UI đang chạy với demo API adapter, chưa phải backend staging.</div>
                <div className="demo-runtime-detail">
                  GET được phục vụ bằng dữ liệu mock có kiểm soát; POST/PUT/DELETE bị chặn 405 và dữ liệu không được lưu sau phiên test.
                </div>
              </div>
              <div className="demo-runtime-actions">
                <Link to="/system-blueprint">Mở bản đồ hệ thống</Link>
                <Link to="/system-blueprint/coverage">Coverage & Readiness</Link>
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
