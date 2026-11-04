import { Link } from 'react-router-dom';
import type { LucideIcon } from 'lucide-react';
import {
  AlertTriangle, Boxes, CheckCircle, Clipboard, ClipboardCheck, Clock,
  DollarSign, Handshake, HelpCircle, Home, Layers3, Lock, LogOut, Menu, MapPinned, Map as MapIcon,
  Package, PackageCheck, PackageOpen, Repeat2, RotateCcw, Ruler, Scale, Search, ShoppingCart, Shield, ShieldAlert,
  Truck, Users, Warehouse, Wrench, Zap,
} from 'lucide-react';
import type { ProductionNavItem } from '../config/productionNavigation';
import type { ActiveImpersonationState } from '../config/portalMode';

export const productionIcons: Record<string, LucideIcon> = {
  '/': Home, '/dock-yard': Truck, '/purchase-orders': ShoppingCart, '/asns': Clock,
  '/import-receipts': ClipboardCheck, '/putaway-tasks': PackageOpen, '/putaway': PackageOpen, '/sales-orders': ShoppingCart,
  '/backorders': AlertTriangle, '/stock-reservations': Lock, '/stock-allocations': Layers3,
  '/picking-tasks': ClipboardCheck, '/packing-sessions': PackageCheck, '/export-receipts': Package,
  '/shipments': Truck, '/handover-manifests': Clipboard, '/customer-returns': Repeat2,
  '/inventory': Boxes, '/inventory-locks': Lock, '/inventory-movements': Repeat2,
  '/stock-transfers': Truck, '/stocktakes': ClipboardCheck, '/inventory-reversals': RotateCcw,
  '/inventory-reconciliation': Scale, '/products': Package, '/categories': Layers3,
  '/units': Ruler, '/kit-work-orders': Wrench, '/kitting': Wrench, '/inventory-traceability': Search,
  '/business-partners': Handshake, '/cod-reconciliation': DollarSign, '/cost-valuation': Scale, '/profit-report': DollarSign,
  '/misa-exports': Clipboard, '/accounting-export': Clipboard, '/partner-debts': Handshake, '/warehouses': Warehouse,
  '/warehouse-structure': MapPinned, '/warehouse-map': MapIcon, '/warehouse-calendar': Clock,
  '/approvals': CheckCircle, '/users': Users, '/roles': Shield, '/warehouse-scopes': Warehouse,
  '/account-security': Lock, '/permissions': Lock, '/saas-billing': DollarSign, '/onboarding-wizard': Zap,
};

export const sectionIcons: Record<string, LucideIcon> = {
  'Điều hành': Home, 'Nhập kho': PackageOpen, 'Xuất kho': PackageCheck, 'Giao vận': Truck,
  'Hàng hoàn': Repeat2, 'Tồn kho': Boxes, 'Sản phẩm': Package, 'Tài chính': DollarSign,
  'Cấu trúc kho': MapPinned, 'Quản trị công ty': Shield,
};

export const SECTION_SLUGS: Record<string, string> = {
  'Điều hành': 'dieu-hanh', 'Nhập kho': 'nhap-kho', 'Xuất kho': 'xuat-kho', 'Giao vận': 'giao-van',
  'Hàng hoàn': 'hang-hoan', 'Tồn kho': 'ton-kho', 'Sản phẩm': 'san-pham', 'Tài chính': 'tai-chinh',
  'Cấu trúc kho': 'cau-truc-kho', 'Quản trị công ty': 'quan-tri-cong-ty',
};

export const pdaBottomNavItems: { path: string; label: string; shortLabel: string; icon: LucideIcon }[] = [
  { path: '/', label: 'Tổng quan', shortLabel: 'Tổng quan', icon: Home },
  { path: '/import-receipts', label: 'Nhận hàng', shortLabel: 'Nhận hàng', icon: Clipboard },
  { path: '/putaway-tasks', label: 'Cất kệ', shortLabel: 'Cất kệ', icon: PackageOpen },
  { path: '/picking-tasks', label: 'Lấy hàng', shortLabel: 'Lấy hàng', icon: ClipboardCheck },
];

interface MainLayoutBannersProps {
  isTenantSuspended: boolean;
  impersonationState: ActiveImpersonationState | null;
  impersonationCountdownText: string;
  onGotoBilling: () => void;
  onExitImpersonation: () => void;
}

export function MainLayoutBanners({
  isTenantSuspended,
  impersonationState,
  impersonationCountdownText,
  onGotoBilling,
  onExitImpersonation,
}: MainLayoutBannersProps) {
  const suspensionReason =
    (typeof window !== 'undefined' && window.localStorage?.getItem('tenantSuspensionReason')) ||
    'Theo quyết định quản trị nền tảng';

  return (
    <>
      {isTenantSuspended && (
        <div
          id="customer-tenant-suspended-banner"
          role="alert"
          data-testid="tenant-suspended-lock-banner"
          style={{ padding: '10px 16px', background: '#991b1b', color: '#ffffff', borderBottom: '2px solid #f87171', fontSize: 12.5, fontWeight: 600, display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, minWidth: 0, flex: '1 1 280px' }}>
            <AlertTriangle size={16} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0 }} />
            <span>
              <strong>Tài khoản doanh nghiệp TENANT-001 (CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO) đang tạm khóa</strong> — Lý do: {suspensionReason}. Dữ liệu tồn kho 9 chiều và sổ cái kế toán được bảo toàn 100%. Vui lòng liên hệ bộ phận Hỗ trợ Quản trị Nền tảng (support@erpkho.vn) để mở khóa lại.
            </span>
          </span>
          <button
            type="button"
            data-testid="btn-goto-billing-unlock"
            onClick={onGotoBilling}
            style={{ padding: '6px 12px', borderRadius: 6, background: '#ffffff', color: '#991b1b', border: '1px solid #fecaca', fontSize: 12, fontWeight: 700, whiteSpace: 'nowrap', cursor: 'pointer', flexShrink: 0 }}
          >
            Thanh toán mở khóa ngay →
          </button>
        </div>
      )}

      {impersonationState && (
        <div
          id="customer-impersonation-banner"
          role="status"
          data-testid="active-impersonation-ribbon"
          style={{ position: 'sticky', top: 0, zIndex: 50, padding: '8px 16px', background: '#fffbeb', color: '#78350f', borderBottom: '2px solid #f59e0b', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10, fontSize: 12.5, fontWeight: 600 }}
        >
          <div data-testid="active-impersonation-banner" style={{ display: 'inline-flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <span data-testid="impersonation-readonly-banner" style={{ display: 'inline-flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <ShieldAlert size={16} strokeWidth={2} aria-hidden="true" color="#b45309" />
              <span>
                {isTenantSuspended ? 'Đang trong phiên chẩn đoán chỉ đọc (Doanh nghiệp đang tạm khóa)' : 'Đang trong phiên hỗ trợ chỉ đọc'} — Doanh nghiệp:{' '}
                <strong>{impersonationState.tenantCode || `TENANT-${String(impersonationState.tenantId).padStart(3, '0')}`}</strong> (
                {!impersonationState.tenantName || impersonationState.tenantName === 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)'
                  ? 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO'
                  : impersonationState.tenantName}
                ) • Ticket: <strong>{impersonationState.ticketCode}</strong> •
              </span>
              <span data-testid="impersonation-countdown-timer" style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: '#b45309', fontWeight: 700 }}>
                <Clock size={14} strokeWidth={2} aria-hidden="true" />
                <strong data-testid="customer-portal-impersonation-countdown">Thời gian còn lại: {impersonationCountdownText}</strong>
              </span>
            </span>
          </div>
          <button
            type="button"
            data-testid="btn-exit-impersonation"
            onClick={onExitImpersonation}
            style={{ padding: '4px 10px', borderRadius: 6, background: '#dc2626', color: '#ffffff', border: '1px solid #b91c1c', fontSize: 12, fontWeight: 700, cursor: 'pointer' }}
          >
            Kết thúc phiên
          </button>
        </div>
      )}
    </>
  );
}

interface MainLayoutTopbarProps {
  mobileNavOpen: boolean;
  onToggleMobileNav: () => void;
  pageMeta?: ProductionNavItem;
  complexityMode: 'FULL' | 'EXPRESS';
  onToggleComplexityMode: () => void;
  pathname: string;
  resolvedTenantBadgeLabel: string;
  diagCopiedNotice: string;
  setDiagCopiedNotice: (msg: string) => void;
  demoRuntime: boolean;
  onLogout: () => void;
}

export function MainLayoutTopbar({
  mobileNavOpen,
  onToggleMobileNav,
  pageMeta,
  complexityMode,
  onToggleComplexityMode,
  pathname,
  resolvedTenantBadgeLabel,
  diagCopiedNotice,
  setDiagCopiedNotice,
  demoRuntime,
  onLogout,
}: MainLayoutTopbarProps) {
  return (
    <header className="app-topbar">
      <div className="topbar-leading">
        <button
          type="button"
          className="mobile-nav-toggle"
          aria-label={mobileNavOpen ? 'Đóng menu điều hướng' : 'Mở menu điều hướng'}
          title={mobileNavOpen ? 'Đóng menu điều hướng' : 'Mở menu điều hướng'}
          aria-expanded={mobileNavOpen}
          aria-controls="system-sidebar"
          onClick={onToggleMobileNav}
        >
          <Menu aria-hidden="true" />
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
          onClick={onToggleComplexityMode}
          aria-label="Chuyển đổi giữa Chế độ Rút gọn (Express) và Chế độ Đầy đủ (Full WMS)"
          title="Chuyển đổi giữa Chế độ Rút gọn (Express) và Chế độ Đầy đủ (Full WMS)"
          style={{ fontSize: 12, padding: '4px 10px', minHeight: 34, whiteSpace: 'nowrap', borderRadius: 6, border: complexityMode === 'EXPRESS' ? '1px solid #1d4ed8' : '1px solid #cbd5e1', background: complexityMode === 'EXPRESS' ? '#eff6ff' : '#f8fafc', color: '#0f172a', fontWeight: 600 }}
        >
          {complexityMode === 'EXPRESS' ? (
            <><Zap size={14} strokeWidth={2} aria-hidden="true" /> Chế độ Rút gọn (Express)</>
          ) : (
            <><Warehouse size={14} strokeWidth={2} aria-hidden="true" /> Chế độ Đầy đủ (Full WMS)</>
          )}
        </button>

        <details className="topbar-help-menu">
          <summary className="topbar-help-trigger" aria-label="Trợ giúp và công cụ chẩn đoán kỹ thuật" title="Trợ giúp và công cụ chẩn đoán kỹ thuật">
            <HelpCircle size={16} aria-hidden="true" />
            <span>Trợ giúp</span>
          </summary>
          <div className="topbar-help-popover">
            <div className="topbar-help-title">Hỗ trợ kỹ thuật và chẩn đoán</div>
            <p className="topbar-help-desc">
              Sao chép nhanh mã chẩn đoán phiên làm việc (Kho, Người dùng, Đường dẫn, Mã lỗi) để gửi bộ phận hỗ trợ.
            </p>
            <button
              type="button"
              data-testid="btn-copy-zalo-diagnostics"
              aria-label="Sao chép thông tin chẩn đoán lỗi gửi hỗ trợ Zalo"
              title="Sao chép thông tin chẩn đoán lỗi gửi hỗ trợ Zalo"
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
              <span role="status" data-testid="zalo-diagnostics-notice" style={{ display: 'block', marginTop: 6, fontSize: 12, color: '#065f46', background: '#ecfdf5', padding: '4px 8px', borderRadius: 6, border: '1px solid #a7f3d0' }}>
                {diagCopiedNotice}
              </span>
            )}
          </div>
        </details>

        {demoRuntime ? (
          <>
            <span className="runtime-badge ui-readonly-pill">MÔI TRƯỜNG MÔ PHỎNG • PHÍA MÁY CHỦ MÔ PHỎNG</span>
            <span className="topbar-context-text">Giao diện hệ thống thật</span>
          </>
        ) : (
          <>
            <span className="runtime-badge ui-readonly-pill" data-testid="topbar-tenant-badge" title="Chi nhánh / Kho làm việc hiện tại">
              {resolvedTenantBadgeLabel}
            </span>
            <span className="topbar-context-text">{localStorage.getItem('username') || 'Người dùng'}</span>
            <button type="button" onClick={onLogout} title="Đăng xuất" aria-label="Đăng xuất" className="logout-button topbar-logout-btn">
              <LogOut size={16} aria-hidden="true" />
              <span>Đăng xuất</span>
            </button>
          </>
        )}
      </div>
    </header>
  );
}

interface MainLayoutBottomNavProps {
  pathname: string;
  isCompactViewport: boolean;
  mobileNavOpen: boolean;
  onOpenMoreMenu: () => void;
}

export function MainLayoutBottomNav({
  pathname,
  isCompactViewport,
  mobileNavOpen,
  onOpenMoreMenu,
}: MainLayoutBottomNavProps) {
  return (
    <nav
      className="app-bottom-nav mobile-bottom-nav"
      data-testid="pda-bottom-nav"
      aria-label="Điều hướng tác nghiệp nhanh PDA"
      aria-hidden={!isCompactViewport ? 'true' : undefined}
    >
      {pdaBottomNavItems.map((item) => {
        const active =
          pathname === item.path ||
          (item.path === '/putaway-tasks' && (pathname === '/putaway' || pathname.startsWith('/putaway/'))) ||
          (item.path !== '/' && pathname.startsWith(item.path + '/'));
        const Icon = item.icon;
        return (
          <Link
            key={item.path}
            to={item.path}
            aria-label={item.label}
            title={item.label}
            className={'app-bottom-nav-link mobile-bottom-nav__item' + (active ? ' active' : '')}
            aria-current={active ? 'page' : undefined}
          >
            <Icon size={18} aria-hidden="true" />
            <span>{item.shortLabel}</span>
          </Link>
        );
      })}
      <button
        type="button"
        data-testid="btn-pda-more-menu"
        aria-label="Mở menu điều hướng"
        title="Mở menu điều hướng"
        className={'app-bottom-nav-link mobile-bottom-nav__item' + (mobileNavOpen ? ' active' : '')}
        onClick={onOpenMoreMenu}
      >
        <Menu size={18} aria-hidden="true" />
        <span>Menu</span>
      </button>
    </nav>
  );
}
