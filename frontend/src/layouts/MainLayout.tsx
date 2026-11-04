import { Outlet, Link, useLocation, useNavigate } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import { Boxes, ChevronLeft, ChevronRight, Circle, LogOut, X } from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import { beginPermissionRefresh, canViewApprovals, canViewStocktakes, hasPermission, setCurrentPermissions, usePermissionSet } from '../services/authorization';
import { readOnlyDemoPermissions, isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { productionNavigation, productionSections, resolveProductionPage, type ProductionNavItem } from '../config/productionNavigation';
import { clearActiveImpersonationState, getActiveImpersonationState, type ActiveImpersonationState } from '../config/portalMode';
import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../utils/warehouseLabel';
import { MainLayoutBanners, MainLayoutBottomNav, MainLayoutTopbar, productionIcons, SECTION_SLUGS, sectionIcons } from './MainLayoutParts';
import './MainLayout.css';

export function sectionSlug(title: string): string {
  return SECTION_SLUGS[title] ?? title.toLowerCase().replace(/\s+/g, '-');
}

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const demoRuntime = isReadOnlyDemoRuntime();
  const pageMeta = resolveProductionPage(pathname);
  const mainRef = useRef<HTMLElement>(null);
  const sidebarRef = useRef<HTMLElement>(null);
  const flyoutRef = useRef<HTMLDivElement>(null);
  const groupBtnRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const pendingFocusIndexRef = useRef<number | null>(null);
  const previousPathRef = useRef(pathname);
  const openTimerRef = useRef<number | null>(null);
  const closeTimerRef = useRef<number | null>(null);
  const lastPointerTypeRef = useRef<string>('mouse');

  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => typeof window !== 'undefined' && window.innerWidth >= 768 && window.innerWidth <= 1024);
  const [hoveredSection, setHoveredSection] = useState<string | null>(null);
  const [pinnedSection, setPinnedSection] = useState<string | null>(null);
  const [impersonationState, setImpersonationState] = useState<ActiveImpersonationState | null>(() => getActiveImpersonationState());
  const [impersonationRemainSec, setImpersonationRemainSec] = useState<number>(1794);
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [isCompactViewport, setIsCompactViewport] = useState(() => (typeof window !== 'undefined' ? window.innerWidth <= 767 : false));
  const [diagCopiedNotice, setDiagCopiedNotice] = useState('');
  const [complexityMode, setComplexityMode] = useState<'FULL' | 'EXPRESS'>(() => typeof window !== 'undefined' && window.localStorage?.getItem('wmsComplexityMode') === 'EXPRESS' ? 'EXPRESS' : 'FULL');
  const activeFlyoutSection = hoveredSection ?? pinnedSection;

  const clearFlyoutTimers = () => {
    if (openTimerRef.current !== null && typeof window !== 'undefined') { window.clearTimeout(openTimerRef.current); openTimerRef.current = null; }
    if (closeTimerRef.current !== null && typeof window !== 'undefined') { window.clearTimeout(closeTimerRef.current); closeTimerRef.current = null; }
  };

  const handleGroupMouseEnter = (section: string) => {
    if (isCompactViewport || mobileNavOpen || lastPointerTypeRef.current === 'touch') return;
    clearFlyoutTimers();
    if (productionNavigation.filter((item) => item.section === section).length <= 1) { setHoveredSection(null); return; }
    setHoveredSection(section);
  };

  const handleSidebarCorridorLeave = () => {
    if (isCompactViewport || mobileNavOpen || lastPointerTypeRef.current === 'touch') return;
    if (openTimerRef.current !== null && typeof window !== 'undefined') { window.clearTimeout(openTimerRef.current); openTimerRef.current = null; }
    if (typeof window !== 'undefined') closeTimerRef.current = window.setTimeout(() => setHoveredSection(null), 220);
  };
  useEffect(() => () => clearFlyoutTimers(), []);

  useEffect(() => {
    const syncImp = () => setImpersonationState(getActiveImpersonationState());
    syncImp();
    window.addEventListener('erp-impersonation-changed', syncImp);
    return () => window.removeEventListener('erp-impersonation-changed', syncImp);
  }, [pathname]);

  useEffect(() => {
    if (!impersonationState || typeof window === 'undefined') return;
    const parsedMs = Date.parse(impersonationState.expiresAtUtc);
    const now = Date.now();
    const targetMs = Number.isFinite(parsedMs) && parsedMs > now && parsedMs - now <= 7200_000 ? parsedMs : now + 1794_000;
    const tick = () => setImpersonationRemainSec(Math.max(0, Math.floor((targetMs - Date.now()) / 1000)));
    tick();
    const timer = window.setInterval(tick, 1000);
    return () => window.clearInterval(timer);
  }, [impersonationState]);

  useEffect(() => {
    const handleOutsideInteract = (event: Event) => {
      if (sidebarRef.current && event.target instanceof Node && !sidebarRef.current.contains(event.target)) {
        setHoveredSection(null); setPinnedSection(null);
      }
    };
    document.addEventListener('pointerdown', handleOutsideInteract);
    document.addEventListener('mousedown', handleOutsideInteract);
    document.addEventListener('touchstart', handleOutsideInteract, { passive: true });
    return () => {
      document.removeEventListener('pointerdown', handleOutsideInteract);
      document.removeEventListener('mousedown', handleOutsideInteract);
      document.removeEventListener('touchstart', handleOutsideInteract);
    };
  }, []);

  const toggleComplexityMode = () => setComplexityMode((current) => {
    const next = current === 'FULL' ? 'EXPRESS' : 'FULL';
    if (typeof window !== 'undefined' && window.localStorage) window.localStorage.setItem('wmsComplexityMode', next);
    return next;
  });

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const syncViewport = () => { setIsCompactViewport(window.innerWidth <= 767); if (window.innerWidth >= 768 && window.innerWidth <= 1024) setSidebarCollapsed(true); };
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
    apiClient.get('/api/v1/auth/me').then(response => {
      if (!active) return;
      if (response.data?.username) localStorage.setItem('username', String(response.data.username));
      if (response.data?.role) localStorage.setItem('role', String(response.data.role));
      const uid = response.data?.userId ?? response.data?.id;
      if (uid !== undefined && uid !== null) localStorage.setItem('userId', String(uid));
      setCurrentPermissions(response.data.permissions, revision);
      setIdentityState('ready');
    }).catch((err: any) => {
      if (!active) return;
      setCurrentPermissions([], revision);
      const isVitest = Boolean(typeof import.meta !== 'undefined' && (import.meta.env?.VITEST || import.meta.env?.MODE === 'test'));
      if (err?.response?.status === 401 && !isVitest && typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
        try { localStorage.removeItem('token'); localStorage.removeItem('accessToken'); localStorage.removeItem('jwt'); sessionStorage.clear(); } catch { /* ignore */ }
        window.location.replace('/login');
        return;
      }
      setIdentityState('error');
    });
    return () => { active = false; };
  }, [demoRuntime]);

  useEffect(() => { document.title = (pageMeta?.label ?? 'ERP WMS') + ' • ERP WMS'; }, [pageMeta?.label]);

  useEffect(() => {
    if (previousPathRef.current !== pathname) {
      setMobileNavOpen(false); setHoveredSection(null); setPinnedSection(null);
      mainRef.current?.focus();
      previousPathRef.current = pathname;
    }
  }, [pathname]);

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        const prevSection = activeFlyoutSection;
        const focusInSidebar = Boolean(sidebarRef.current && document.activeElement && sidebarRef.current.contains(document.activeElement));
        setMobileNavOpen(false);
        setHoveredSection(null);
        setPinnedSection(null);
        if (prevSection && focusInSidebar) groupBtnRefs.current[prevSection]?.focus();
      }
    };
    window.addEventListener('keydown', closeOnEscape);
    return () => window.removeEventListener('keydown', closeOnEscape);
  }, [activeFlyoutSection]);

  const showStocktakes = demoRuntime || canViewStocktakes();
  const canShowProductionItem = (item: ProductionNavItem) => {
    if (complexityMode === 'EXPRESS' && item.expressMode === false && pathname !== item.path) return false;
    if (item.path === '/sales-orders' || item.path === '/backorders') return hasPermission('sales_order.read') || hasPermission('backorder.read');
    if (item.path === '/customer-returns') return hasPermission('return.read') || hasPermission('receipt.read');
    if (item.permission) return hasPermission(item.permission);
    if (item.access === 'stocktake') return showStocktakes;
    if (item.access === 'approvals') return canViewApprovals();
    return true;
  };

  const resolvedTenantBadgeLabel = (() => {
    const rawWhName = localStorage.getItem('activeWarehouseName') || localStorage.getItem('warehouseName') || '';
    const rawCode = localStorage.getItem('activeWarehouseCode') || localStorage.getItem('tenantCode') || '';
    const cleanedCode = ['DEFAULT', 'TENANT-1', 'TENANT-001', 'MẶC ĐỊNH'].includes(rawCode.trim().toUpperCase()) ? 'WH01' : rawCode;
    return formatWarehouseLabel(rawWhName, cleanedCode || 'WH01', 1) || CANONICAL_WH01_LABEL;
  })();

  const renderFlyoutNavItem = (item: ProductionNavItem) => {
    const active = pathname === item.path || (item.path !== '/' && pathname.startsWith(item.path + '/')) || Boolean(item.aliases?.some(a => pathname === a || pathname.startsWith(a + '/')));
    const Icon = productionIcons[item.path] ?? Circle;
    return (
      <li className="sidebar-nav-item" key={item.path}>
        <Link to={item.path} className={'sidebar-nav-link' + (active ? ' active' : '')} aria-current={active ? 'page' : undefined} onClick={() => { setMobileNavOpen(false); setHoveredSection(null); setPinnedSection(null); }}>
          <Icon className="sidebar-nav-icon" strokeWidth={2} aria-hidden="true" />
          <div className="wms-flyout-item-copy">
            <span className="wms-flyout-item-title">{item.label}</span>
            <small className="wms-flyout-item-desc wms-mobile-subitem-desc" aria-hidden="true">{item.description}</small>
          </div>
        </Link>
      </li>
    );
  };

  const sidebarHiddenOffCanvas = isCompactViewport && !mobileNavOpen;
  const primarySidebarWidth = sidebarCollapsed ? '64px' : '232px';
  const isInlineMobileMenu = isCompactViewport || mobileNavOpen;
  const activeFlyoutItems = activeFlyoutSection ? productionNavigation.filter((item) => item.section === activeFlyoutSection && canShowProductionItem(item)) : [];
  const showDesktopFlyout = !isInlineMobileMenu && Boolean(activeFlyoutSection && activeFlyoutItems.length > 0);

  useEffect(() => {
    if (pendingFocusIndexRef.current === null || !activeFlyoutSection) return;
    const idx = pendingFocusIndexRef.current;
    pendingFocusIndexRef.current = null;
    const container = isInlineMobileMenu
      ? sidebarRef.current?.querySelector<HTMLElement>(`[data-testid="mobile-accordion-${sectionSlug(activeFlyoutSection)}"]`)
      : flyoutRef.current;
    const links = container?.querySelectorAll<HTMLAnchorElement>('a.sidebar-nav-link');
    if (links && links.length > 0) links[Math.min(idx, links.length - 1)]?.focus();
  }, [showDesktopFlyout, activeFlyoutSection, isInlineMobileMenu]);

  const handleGroupKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>, section: string, isSingleItemSection: boolean) => {
    if (isSingleItemSection) return;
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      const closing = activeFlyoutSection === section;
      if (!closing) pendingFocusIndexRef.current = 0;
      setPinnedSection(closing ? null : section);
      setHoveredSection(closing ? null : section);
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowRight') {
      event.preventDefault();
      if (activeFlyoutSection === section) {
        const container = isInlineMobileMenu ? sidebarRef.current?.querySelector<HTMLElement>(`[data-testid="mobile-accordion-${sectionSlug(section)}"]`) : flyoutRef.current;
        container?.querySelectorAll<HTMLAnchorElement>('a.sidebar-nav-link')[0]?.focus();
      } else {
        pendingFocusIndexRef.current = 0;
        setPinnedSection(section);
        setHoveredSection(section);
      }
    } else if (event.key === 'Escape' && activeFlyoutSection) {
      event.preventDefault(); event.stopPropagation();
      setPinnedSection(null); setHoveredSection(null);
      groupBtnRefs.current[section]?.focus();
    }
  };

  const handleFlyoutKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    const links = Array.from(event.currentTarget.querySelectorAll<HTMLAnchorElement>('a.sidebar-nav-link'));
    const cur = links.indexOf(document.activeElement as HTMLAnchorElement);
    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Home' || event.key === 'End') && links.length > 0) {
      event.preventDefault();
      const nextIdx = event.key === 'ArrowDown' ? (cur + 1) % links.length : event.key === 'ArrowUp' ? (cur - 1 + links.length) % links.length : event.key === 'Home' ? 0 : links.length - 1;
      links[nextIdx]?.focus();
    } else if (event.key === 'Escape') {
      event.preventDefault(); event.stopPropagation();
      const parentSection = activeFlyoutSection;
      setHoveredSection(null); setPinnedSection(null);
      if (parentSection) groupBtnRefs.current[parentSection]?.focus();
    }
  };

  const lifecycleStatus = typeof window !== 'undefined' ? window.localStorage?.getItem('tenantLifecycleStatus') : null;
  const isTenantSuspended = typeof window !== 'undefined' && (lifecycleStatus === 'SUSPENDED' || lifecycleStatus === 'READ_ONLY' || window.location.search.includes('suspendedPreview=1'));
  const impersonationCountdownText = `${String(Math.floor(impersonationRemainSec / 60)).padStart(2, '0')}:${String(impersonationRemainSec % 60).padStart(2, '0')}`;

  return (
    <div className="app-shell production-shell wms-shell" style={{ ['--wms-primary-sidebar-width' as string]: primarySidebarWidth }}>
      <a className="skip-link" href="#main-content">Bỏ qua điều hướng</a>
      {mobileNavOpen && <button type="button" className="sidebar-backdrop" aria-label="Đóng menu điều hướng" title="Đóng menu điều hướng" onClick={() => setMobileNavOpen(false)} />}

      <aside id="system-sidebar" ref={sidebarRef} className={'app-sidebar' + (mobileNavOpen ? ' open' : '') + (sidebarCollapsed ? ' is-collapsed' : '')} aria-label="Điều hướng hệ thống" aria-hidden={sidebarHiddenOffCanvas ? 'true' : undefined} inert={sidebarHiddenOffCanvas ? true : undefined} onMouseLeave={handleSidebarCorridorLeave}>
        <div className="app-sidebar-sticky">
          <div className="app-sidebar-header">
            {!sidebarCollapsed && <h2 className="app-brand">ERP WMS</h2>}
            <button type="button" data-testid="btn-toggle-sidebar-collapse" className="sidebar-collapse-toggle" aria-label={sidebarCollapsed ? 'Mở rộng thanh điều hướng' : 'Thu gọn thanh điều hướng'} title={sidebarCollapsed ? 'Mở rộng thanh điều hướng' : 'Thu gọn thanh điều hướng'} onClick={() => setSidebarCollapsed((prev) => !prev)}>
              {sidebarCollapsed ? <ChevronRight size={16} strokeWidth={2} aria-hidden="true" /> : <ChevronLeft size={16} strokeWidth={2} aria-hidden="true" />}
            </button>
            <button type="button" className="sidebar-mobile-close" aria-label="Đóng menu điều hướng" title="Đóng menu điều hướng" onClick={() => setMobileNavOpen(false)}><X aria-hidden="true" /></button>
          </div>
          {!sidebarCollapsed && <div className="app-brand-context">Hệ thống quản lý kho</div>}

          <nav aria-label="Điều hướng nghiệp vụ">
            <div data-testid="wms-primary-sidebar" className="wms-primary-sidebar">
              {productionSections.map((section) => {
                const SectionIcon = sectionIcons[section] ?? Boxes;
                const allSectionItems = productionNavigation.filter((item) => item.section === section);
                const items = allSectionItems.filter((item) => canShowProductionItem(item));
                const isSingleItemSection = allSectionItems.length === 1;
                const directTargetPath = isSingleItemSection ? (items[0]?.path ?? allSectionItems[0]?.path) : (items.length === 1 ? items[0].path : null);
                const isGroupActive = pageMeta?.section === section;
                const isGroupHovered = !isSingleItemSection && activeFlyoutSection === section;
                const flyoutControlId = isInlineMobileMenu ? `mobile-accordion-${sectionSlug(section)}` : 'wms-secondary-flyout-panel';
                return (
                  <div key={section} className="wms-primary-group-wrap">
                    <button
                      ref={(el) => { groupBtnRefs.current[section] = el; }}
                      type="button"
                      data-testid={`nav-group-${sectionSlug(section)}`}
                      title={section}
                      aria-label={section}
                      className={'wms-primary-group-btn' + (isGroupActive ? ' active-group' : '') + (isGroupHovered ? ' hovered-group' : '')}
                      aria-haspopup={!isSingleItemSection ? 'true' : undefined}
                      aria-expanded={!isSingleItemSection ? isGroupHovered : undefined}
                      aria-controls={!isSingleItemSection ? flyoutControlId : undefined}
                      onPointerDown={(e) => { lastPointerTypeRef.current = e.pointerType || 'mouse'; }}
                      onTouchStart={() => { lastPointerTypeRef.current = 'touch'; }}
                      onMouseEnter={() => handleGroupMouseEnter(section)}
                      onKeyDown={(e) => handleGroupKeyDown(e, section, isSingleItemSection)}
                      onClick={() => {
                        if (isSingleItemSection && directTargetPath) { setMobileNavOpen(false); setHoveredSection(null); setPinnedSection(null); navigate(directTargetPath); return; }
                        if (isInlineMobileMenu) {
                          const next = activeFlyoutSection === section ? null : section;
                          setPinnedSection(next); setHoveredSection(next); return;
                        }
                        clearFlyoutTimers();
                        const shouldClose = lastPointerTypeRef.current === 'touch' ? activeFlyoutSection === section : pinnedSection === section;
                        setPinnedSection(shouldClose ? null : section);
                        setHoveredSection(shouldClose ? null : section);
                      }}
                    >
                      <span className="wms-primary-group-lead">
                        <SectionIcon className="sidebar-nav-icon" strokeWidth={2} aria-hidden="true" />
                        {!sidebarCollapsed && <span className="wms-primary-group-label">{section}</span>}
                      </span>
                      {!sidebarCollapsed && !isSingleItemSection && (
                        <span className="wms-primary-group-meta" aria-hidden="true"><ChevronRight size={14} strokeWidth={2} className={'sidebar-group-chevron' + (isGroupHovered ? ' is-open' : '')} /></span>
                      )}
                    </button>
                    {isInlineMobileMenu && !isSingleItemSection && isGroupHovered && items.length > 0 && (
                      <ul id={`mobile-accordion-${sectionSlug(section)}`} className="sidebar-nav wms-mobile-inline-subnav" data-testid={`mobile-accordion-${sectionSlug(section)}`} onKeyDown={handleFlyoutKeyDown}>
                        {items.map((item) => renderFlyoutNavItem(item))}
                      </ul>
                    )}
                  </div>
                );
              })}
            </div>

            <div id="wms-secondary-flyout-panel" ref={flyoutRef} data-testid="wms-secondary-flyout" className={'wms-secondary-flyout nav-flyout-submenu' + (showDesktopFlyout ? ' is-open' : '')} onMouseEnter={() => clearFlyoutTimers()} onKeyDown={handleFlyoutKeyDown}>
              {showDesktopFlyout && activeFlyoutSection && (
                <>
                  <div className="wms-flyout-header">
                    <span className="wms-flyout-kicker">PHÂN HỆ: {activeFlyoutSection.toUpperCase()}</span>
                    <button type="button" className="wms-flyout-close" aria-label="Đóng bảng menu con" title="Đóng bảng menu con" onClick={() => { const prev = activeFlyoutSection; setHoveredSection(null); setPinnedSection(null); if (prev) groupBtnRefs.current[prev]?.focus(); }}><X size={14} strokeWidth={2} aria-hidden="true" /></button>
                  </div>
                  <ul className="sidebar-nav">{activeFlyoutItems.map((item) => renderFlyoutNavItem(item))}</ul>
                </>
              )}
            </div>

            <div className="wms-flyout-sr-tree">
              {productionSections.filter((section) => section !== activeFlyoutSection).map((section) => {
                const items = productionNavigation.filter((item) => item.section === section && canShowProductionItem(item));
                if (items.length === 0) return null;
                return (
                  <div key={section}>
                    <div className="sidebar-section-label">{section}</div>
                    <ul className="sidebar-nav">{items.map((item) => renderFlyoutNavItem(item))}</ul>
                  </div>
                );
              })}
            </div>
          </nav>
          {isInlineMobileMenu && !demoRuntime && (
            <div className="mobile-drawer-footer">
              <button type="button" data-testid="mobile-drawer-logout-btn" aria-label="Đăng xuất tài khoản" title="Đăng xuất tài khoản" className="mobile-drawer-logout-btn" onClick={() => void logout()}>
                <LogOut size={16} aria-hidden="true" />
                <span>Đăng xuất tài khoản</span>
              </button>
            </div>
          )}
        </div>
      </aside>

      <div className="app-main-column">
        <MainLayoutBanners isTenantSuspended={isTenantSuspended} impersonationState={impersonationState} impersonationCountdownText={impersonationCountdownText} onGotoBilling={() => navigate('/saas-billing')} onExitImpersonation={() => { clearActiveImpersonationState(); setImpersonationState(null); navigate('/platform'); }} />
        <MainLayoutTopbar mobileNavOpen={mobileNavOpen} onToggleMobileNav={() => setMobileNavOpen(value => !value)} pageMeta={pageMeta} complexityMode={complexityMode} onToggleComplexityMode={toggleComplexityMode} pathname={pathname} resolvedTenantBadgeLabel={resolvedTenantBadgeLabel} diagCopiedNotice={diagCopiedNotice} setDiagCopiedNotice={setDiagCopiedNotice} demoRuntime={demoRuntime} onLogout={() => void logout()} />

        <main id="main-content" ref={mainRef} tabIndex={-1} className="production-ui app-content">
          {demoRuntime && (
            <section role="note" className="demo-runtime-banner" aria-label="Thông tin môi trường demo">
              <div className="demo-runtime-copy">
                <div className="demo-runtime-kicker">Bản mô phỏng Vercel • Chỉ đọc</div>
                <div className="demo-runtime-title">Giao diện hệ thống thật đang chạy với bộ chuyển đổi API mô phỏng, chưa phải môi trường máy chủ thử nghiệm.</div>
                <div className="demo-runtime-detail">GET dùng dữ liệu mô phỏng có kiểm soát; POST/PUT/DELETE bị chặn với mã 405 và dữ liệu không được lưu sau phiên kiểm thử.</div>
              </div>
            </section>
          )}
          {identityState === 'loading' ? <p role="status">Đang xác minh quyền truy cập...</p> : identityState === 'error' ? <p role="alert">Không thể xác minh quyền truy cập. Vui lòng tải lại.</p> : <Outlet />}
        </main>

        <MainLayoutBottomNav pathname={pathname} isCompactViewport={isCompactViewport} mobileNavOpen={mobileNavOpen} onOpenMoreMenu={() => setMobileNavOpen(true)} />
      </div>
    </div>
  );
};

export default MainLayout;
