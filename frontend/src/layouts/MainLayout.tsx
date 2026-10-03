import { Outlet, Link, useLocation } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { Layers3, LogOut } from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import { erpWmsBlueprint } from '../config/erpWmsBlueprint';
import { mockUsers } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import { canViewApprovals, canViewStocktakes, hasPermission, beginPermissionRefresh, setCurrentPermissions, usePermissionSet } from '../services/authorization';
import { blueprintDemoReadPermissions, isBlueprintDemoRuntime } from '../services/runtimeMode';
import { productionNavigation, productionSections, resolveProductionPage, type ProductionNavItem } from '../config/productionNavigation';

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const blueprintMode = pathname.startsWith('/system-blueprint');
  const demoRuntime = isBlueprintDemoRuntime();
  const mockDemo = useMockDemo();
  const pageMeta = blueprintMode ? undefined : resolveProductionPage(pathname);
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');

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

  const showStocktakes = demoRuntime || canViewStocktakes();
  const canShowProductionItem = (item: ProductionNavItem) => {
    if (item.permission) return hasPermission(item.permission);
    if (item.access === 'stocktake') return showStocktakes;
    if (item.access === 'approvals') return canViewApprovals();
    return true;
  };
  const navLink = (to: string, label: string, accent = false) => {
    const active = pathname === to || (to !== '/system-blueprint' && pathname.startsWith(to + '/'));
    let linkColor = '#dbe5f1';
    if (accent) linkColor = '#8fc3ff';
    if (active) linkColor = '#ffffff';

    return (
    <li style={{ margin: '8px 0' }} key={to}>
      <Link
        to={to}
        style={{
          color: linkColor,
          background: active ? '#1e3b60' : 'transparent',
          textDecoration: 'none',
          display: 'block',
          borderRadius: 7,
          padding: '7px 9px',
          fontSize: 12,
          fontWeight: accent ? 700 : 500,
        }}
      >
        {label}
      </Link>
    </li>
    );
  };

  return (
    <div style={{ display: 'flex', minHeight: '100vh', fontFamily: 'Arial, sans-serif' }}>
      <aside style={{ width: '250px', flex: '0 0 250px', height: '100vh', position: 'sticky', top: 0, overflowY: 'auto', backgroundColor: '#0f1f35', color: 'white', padding: '20px' }}>
        <h2 style={{ marginBottom: 6 }}>ERP WMS</h2>
        <div style={{ color: '#91a4bc', fontSize: 12, marginBottom: 18 }}>
          {blueprintMode ? 'System Blueprint / Demo' : 'Warehouse Management System'}
        </div>

        {blueprintMode ? (
          <>
            <Link to="/" style={{ color: '#9eb0c6', textDecoration: 'none', fontSize: 11 }}>← Quay lại hệ thống thật</Link>
            <div style={{ margin: '14px 0 7px', color: '#7188a4', fontSize: 9, fontWeight: 800, letterSpacing: '.08em', textTransform: 'uppercase' }}>Blueprint tools</div>
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {navLink('/system-blueprint', 'Bản đồ tổng thể', true)}
              {navLink('/system-blueprint/search', 'Global Search')}
              {navLink('/system-blueprint/coverage', 'Coverage & Readiness')}
              {navLink('/system-blueprint/mock-data', 'Mock Data Lab')}
              {navLink('/system-blueprint/scenarios', 'Golden Scenario Lab')}
            </ul>
            <div style={{ margin: '16px 0 7px', color: '#7188a4', fontSize: 9, fontWeight: 800, letterSpacing: '.08em', textTransform: 'uppercase' }}>17 module groups</div>
            <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {erpWmsBlueprint.map((module) => navLink('/system-blueprint/' + module.key, module.name))}
            </ul>
          </>
        ) : (
          <>
            <div style={{ margin: '2px 0 10px' }}>
              <Link to="/system-blueprint" style={{ color: '#8fc3ff', textDecoration: 'none', display: 'flex', gap: 7, alignItems: 'center', fontWeight: 700, fontSize: 12, padding: '7px 9px' }}>
                <Layers3 size={16} /> Bản đồ hệ thống
              </Link>
            </div>
            {productionSections.map((section) => {
              const items = productionNavigation.filter((item) => item.section === section && canShowProductionItem(item));
              if (items.length === 0) return null;
              return <div key={section}>
                <div style={{ margin: '14px 9px 5px', color: '#7188a4', fontSize: 9, fontWeight: 800, letterSpacing: '.08em', textTransform: 'uppercase' }}>{section}</div>
                <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
                  {items.map((item) => navLink(item.path, item.label))}
                </ul>
              </div>;
            })}
          </>
        )}
      </aside>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <header style={{ minHeight: '64px', backgroundColor: '#ffffff', borderBottom: '1px solid #d9e1eb', display: 'flex', alignItems: 'center', padding: '8px 20px', justifyContent: 'space-between', gap: 16 }}>
          {!blueprintMode && pageMeta && (
            <div style={{ minWidth: 0 }}>
              <div style={{ color: '#7a899c', fontSize: 9, fontWeight: 800, letterSpacing: '.08em', textTransform: 'uppercase' }}>{pageMeta.section}</div>
              <div style={{ color: '#21344a', fontSize: 14, fontWeight: 800, marginTop: 2 }}>{pageMeta.label}</div>
              <div style={{ color: '#6c7c90', fontSize: 10, marginTop: 2, maxWidth: 760, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{pageMeta.description}</div>
            </div>
          )}
          {blueprintMode && (
            <>
              <span style={{ marginRight: 10, fontSize: 11, fontWeight: 800, color: '#6f4bc3', background: '#f4efff', border: '1px solid #dfd3f8', borderRadius: 999, padding: '5px 8px' }}>DEMO / MOCK • READ ONLY</span>
              <label style={{ marginRight: 'auto', display: 'flex', alignItems: 'center', gap: 6, fontSize: 10, color: '#607086' }}>
                <span>Persona mô phỏng</span>
                <select
                  aria-label="Persona mô phỏng"
                  value={mockDemo.selectedUserCode}
                  onChange={(event) => mockDemo.setSelectedUserCode(event.target.value)}
                  style={{ height: 30, border: '1px solid #d2dbe6', borderRadius: 6, background: '#fff', padding: '0 7px', fontSize: 10 }}
                >
                  {mockUsers.map((user) => <option key={user.code} value={user.code}>{user.name} • {user.role}</option>)}
                </select>
                <span>{mockDemo.allowedWarehouses.length} kho scope</span>
              </label>
            </>
          )}
          {blueprintMode ? (
            <span style={{ marginLeft: 12, fontSize: 11, color: '#718096' }}>Blueprint runtime</span>
          ) : demoRuntime ? (
            <>
              <span style={{ marginRight: 10, fontSize: 11, fontWeight: 800, color: '#75520b', background: '#fff7dc', border: '1px solid #ead58a', borderRadius: 999, padding: '5px 8px' }}>
                DEMO RUNTIME • MOCK BACKEND
              </span>
              <span style={{ fontSize: 11, color: '#718096' }}>Frontend production UI</span>
            </>
          ) : (
            <>
              <span style={{ marginRight: '12px' }}>{localStorage.getItem('username') || 'Người dùng'}</span>
              <button type="button" onClick={() => void logout()} title="Đăng xuất" aria-label="Đăng xuất" style={{ border: 0, background: 'transparent', cursor: 'pointer', padding: '8px' }}>
                <LogOut size={20} />
              </button>
            </>
          )}
        </header>

        <main style={{ padding: '20px', flex: 1, backgroundColor: '#f5f7fb', minWidth: 0 }}>
          {demoRuntime && !blueprintMode && (
            <section role="note" style={{ display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'center', flexWrap: 'wrap', marginBottom: 16, padding: '12px 14px', background: '#fff9e8', border: '1px solid #ead58a', borderRadius: 10, color: '#5f4a18' }}>
              <div style={{ minWidth: 260, flex: 1 }}>
                <div style={{ fontSize: 10, fontWeight: 900, letterSpacing: '.08em', textTransform: 'uppercase' }}>Vercel Blueprint Demo • Read only</div>
                <div style={{ fontSize: 12, fontWeight: 800, marginTop: 3 }}>Đây là production UI đang chạy với demo API adapter, chưa phải backend staging.</div>
                <div style={{ fontSize: 10, marginTop: 4, lineHeight: 1.5 }}>GET được phục vụ bằng dữ liệu mock có kiểm soát; POST/PUT/DELETE bị chặn 405 và dữ liệu không được lưu sau phiên test.</div>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <Link to="/system-blueprint" style={{ color: '#315f91', fontSize: 10, fontWeight: 800, textDecoration: 'none', background: '#fff', border: '1px solid #d5dfeb', borderRadius: 7, padding: '7px 9px' }}>Mở bản đồ hệ thống</Link>
                <Link to="/system-blueprint/coverage" style={{ color: '#315f91', fontSize: 10, fontWeight: 800, textDecoration: 'none', background: '#fff', border: '1px solid #d5dfeb', borderRadius: 7, padding: '7px 9px' }}>Coverage & Readiness</Link>
              </div>
            </section>
          )}
          {identityState === 'loading' ? <p role="status">Đang xác minh quyền truy cập...</p>
            : identityState === 'error' ? <p role="alert">Không thể xác minh quyền truy cập. Vui lòng tải lại.</p>
              : <Outlet />}
        </main>
      </div>
    </div>
  );
};

export default MainLayout;
