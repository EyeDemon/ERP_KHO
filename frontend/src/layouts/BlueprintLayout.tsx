import { Outlet, Link, useLocation } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import type { LucideIcon } from 'lucide-react';
import {
  Circle,
  Database,
  FlaskConical,
  Gauge,
  Home,
  Layers3,
  Menu,
  Search,
  X,
} from 'lucide-react';
import { erpWmsBlueprint } from '../config/erpWmsBlueprint';
import { mockUsers } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import {
  beginPermissionRefresh,
  setCurrentPermissions,
} from '../services/authorization';
import './MainLayout.css';

const blueprintIcons: Record<string, LucideIcon> = {
  '/system-blueprint': Layers3,
  '/system-blueprint/search': Search,
  '/system-blueprint/coverage': Gauge,
  '/system-blueprint/mock-data': Database,
  '/system-blueprint/scenarios': FlaskConical,
};

const BlueprintLayout = () => {
  const { pathname } = useLocation();
  const mockDemo = useMockDemo();
  const mainRef = useRef<HTMLElement>(null);
  const previousPathRef = useRef(pathname);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  useEffect(() => {
    const revision = beginPermissionRefresh();
    setCurrentPermissions([], revision);
  }, [pathname]);

  useEffect(() => {
    document.title = 'Bản đồ hệ thống • ERP WMS Blueprint';
  }, [pathname]);

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

      <aside id="system-sidebar" className={'app-sidebar' + (mobileNavOpen ? ' open' : '')} aria-label="Điều hướng bản thiết kế">
        <div className="app-sidebar-header">
          <h2 className="app-brand">ERP WMS</h2>
          <button type="button" className="sidebar-mobile-close" aria-label="Đóng menu điều hướng" onClick={() => setMobileNavOpen(false)}>
            <X aria-hidden="true" />
          </button>
        </div>
        <div className="app-brand-context">
          Bản thiết kế hệ thống / Mô phỏng độc lập
        </div>

        <Link to="/" className="sidebar-back-link">
          <Home className="sidebar-nav-icon" aria-hidden="true" />
          <span>Mở hệ thống vận hành thật</span>
        </Link>

        <div className="sidebar-section-label">Công cụ bản thiết kế</div>
        <nav aria-label="Công cụ bản thiết kế">
          <ul className="sidebar-nav">
            {navLink('/system-blueprint', 'Bản đồ tổng thể', { accent: true, icon: blueprintIcons['/system-blueprint'] })}
            {navLink('/system-blueprint/search', 'Tìm kiếm toàn hệ thống', { icon: blueprintIcons['/system-blueprint/search'] })}
            {navLink('/system-blueprint/coverage', 'Độ phủ & mức sẵn sàng', { icon: blueprintIcons['/system-blueprint/coverage'] })}
            {navLink('/system-blueprint/mock-data', 'Phòng dữ liệu mô phỏng', { icon: blueprintIcons['/system-blueprint/mock-data'] })}
            {navLink('/system-blueprint/scenarios', 'Phòng kịch bản chuẩn', { icon: blueprintIcons['/system-blueprint/scenarios'] })}
          </ul>
        </nav>

        <div className="sidebar-section-label">17 nhóm phân hệ</div>
        <nav aria-label="Bản thiết kế theo phân hệ">
          <ul className="sidebar-nav">
            {erpWmsBlueprint.map((module) => navLink('/system-blueprint/' + module.key, module.name, { icon: Circle }))}
          </ul>
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

            <span className="blueprint-badge">MÔ PHỎNG • CHỈ ĐỌC</span>
            <label className="persona-control">
              <span>Vai trò mô phỏng</span>
              <select
                aria-label="Vai trò mô phỏng"
                value={mockDemo.selectedUserCode}
                onChange={(event) => mockDemo.setSelectedUserCode(event.target.value)}
              >
                {mockUsers.map((user) => (
                  <option key={user.code} value={user.code}>{user.name} • {user.role}</option>
                ))}
              </select>
              <span>{mockDemo.allowedWarehouses.length} kho trong phạm vi</span>
            </label>
          </div>

          <div className="topbar-actions">
            <span className="topbar-context-text">Môi trường bản thiết kế</span>
          </div>
        </header>

        <main
          id="main-content"
          ref={mainRef}
          tabIndex={-1}
          className="app-content"
        >
          <Outlet />
        </main>
      </div>
    </div>
  );
};

export default BlueprintLayout;
