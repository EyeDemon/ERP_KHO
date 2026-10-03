import { Outlet, Link, useLocation } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { Layers3, LogOut } from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import { erpWmsBlueprint } from '../config/erpWmsBlueprint';
import { mockUsers } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import { canViewApprovals, canViewStocktakes, hasPermission, beginPermissionRefresh, setCurrentPermissions, usePermissionSet } from '../services/authorization';

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const blueprintMode = pathname.startsWith('/system-blueprint');
  const mockDemo = useMockDemo();
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');

  useEffect(() => {
    let active = true;
    const revision = beginPermissionRefresh();

    if (blueprintMode) {
      setCurrentPermissions([], revision);
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
  }, [blueprintMode, pathname]);

  const showStocktakes = canViewStocktakes();
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
          <ul style={{ listStyle: 'none', padding: 0 }}>
            <li style={{ margin: '10px 0' }}><Link to="/" style={{ color: 'white', textDecoration: 'none' }}>Tổng quan</Link></li>
            <li style={{ margin: '10px 0' }}>
              <Link to="/system-blueprint" style={{ color: '#8fc3ff', textDecoration: 'none', display: 'flex', gap: 7, alignItems: 'center', fontWeight: 700 }}>
                <Layers3 size={16} /> Bản đồ hệ thống
              </Link>
            </li>
            {hasPermission('product.read') && <li style={{ margin: '10px 0' }}><Link to="/products" style={{ color: 'white', textDecoration: 'none' }}>Sản phẩm</Link></li>}
            {hasPermission('warehouse.read') && <li style={{ margin: '10px 0' }}><Link to="/warehouses" style={{ color: 'white', textDecoration: 'none' }}>Kho hàng</Link></li>}
            {hasPermission('uom.read') && <li style={{ margin: '10px 0' }}><Link to="/units" style={{ color: 'white', textDecoration: 'none' }}>Đơn vị tính</Link></li>}
            {hasPermission('partner.read') && <li style={{ margin: '10px 0' }}><Link to="/business-partners" style={{ color: 'white', textDecoration: 'none' }}>Đối tác</Link></li>}
            {hasPermission('receipt.read') && <li style={{ margin: '10px 0' }}><Link to="/import-receipts" style={{ color: 'white', textDecoration: 'none' }}>Phiếu nhập kho</Link></li>}
            <li style={{ margin: '10px 0' }}><Link to="/export-receipts" style={{ color: 'white', textDecoration: 'none' }}>Phiếu xuất kho</Link></li>
            <li style={{ margin: '10px 0' }}><Link to="/inventory" style={{ color: 'white', textDecoration: 'none' }}>Tồn kho</Link></li>
            <li style={{ margin: '10px 0' }}><Link to="/inventory-reconciliation" style={{ color: 'white', textDecoration: 'none' }}>Đối chiếu tồn kho</Link></li>
            {hasPermission('putaway.read') && <li style={{ margin: '10px 0' }}><Link to="/putaway-tasks" style={{ color: 'white', textDecoration: 'none' }}>Cất hàng</Link></li>}
            {showStocktakes && <li style={{ margin: '10px 0' }}><Link to="/stocktakes" style={{ color: 'white', textDecoration: 'none' }}>Kiểm kê kho</Link></li>}
            <li style={{ margin: '10px 0' }}><Link to="/stock-transfers" style={{ color: 'white', textDecoration: 'none' }}>Điều chuyển kho</Link></li>
            <li style={{ margin: '10px 0' }}><Link to="/stock-reservations" style={{ color: 'white', textDecoration: 'none' }}>Giữ hàng</Link></li>
            {canViewApprovals() && <li style={{ margin: '10px 0' }}><Link to="/approvals" style={{ color: 'white', textDecoration: 'none' }}>Phê duyệt</Link></li>}
            {hasPermission('permission.read') && <li><Link to="/permissions" style={{ color: 'white' }}>Quản trị quyền truy cập</Link></li>}
          </ul>
        )}
      </aside>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <header style={{ height: '60px', backgroundColor: '#ffffff', borderBottom: '1px solid #d9e1eb', display: 'flex', alignItems: 'center', padding: '0 20px', justifyContent: 'flex-end' }}>
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
          {identityState === 'loading' ? <p role="status">Đang xác minh quyền truy cập...</p>
            : identityState === 'error' ? <p role="alert">Không thể xác minh quyền truy cập. Vui lòng tải lại.</p>
              : <Outlet />}
        </main>
      </div>
    </div>
  );
};

export default MainLayout;
