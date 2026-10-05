import { Outlet, Link, useLocation } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { LogOut } from 'lucide-react';
import apiClient, { logout } from '../services/apiClient';
import { canViewApprovals, canViewStocktakes, hasPermission, beginPermissionRefresh, setCurrentPermissions, usePermissionSet } from '../services/authorization';

const MainLayout = () => {
  usePermissionSet();
  const { pathname } = useLocation();
  const [identityState, setIdentityState] = useState<'loading' | 'ready' | 'error'>('loading');
  useEffect(() => {
    let active = true;
    const revision = beginPermissionRefresh();
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
  }, [pathname]);
  const showStocktakes = canViewStocktakes();
  return (
    <div style={{ display: 'flex', minHeight: '100vh', fontFamily: 'Arial, sans-serif' }}>
      {/* Sidebar */}
      <aside style={{ width: '250px', backgroundColor: '#2c3e50', color: 'white', padding: '20px' }}>
        <h2>ERP KHO</h2>
        <ul style={{ listStyle: 'none', padding: 0 }}>
          <li style={{ margin: '10px 0' }}><Link to="/" style={{ color: 'white', textDecoration: 'none' }}>Tổng quan</Link></li>
          {hasPermission('product.read') && <li style={{ margin: '10px 0' }}><Link to="/products" style={{ color: 'white', textDecoration: 'none' }}>Sản phẩm</Link></li>}
          {hasPermission('warehouse.read') && <li style={{ margin: '10px 0' }}><Link to="/warehouses" style={{ color: 'white', textDecoration: 'none' }}>Kho hàng</Link></li>}
          {hasPermission('uom.read') && <li style={{ margin: '10px 0' }}><Link to="/units" style={{ color: 'white', textDecoration: 'none' }}>Đơn vị tính</Link></li>}
          {hasPermission('partner.read') && <li style={{ margin: '10px 0' }}><Link to="/business-partners" style={{ color: 'white', textDecoration: 'none' }}>Đối tác</Link></li>}
          {hasPermission('receipt.read') && <li style={{ margin: '10px 0' }}><Link to="/import-receipts" style={{ color: 'white', textDecoration: 'none' }}>Phiếu nhập kho</Link></li>}
          {hasPermission('export_receipt.read') && <li style={{ margin: '10px 0' }}><Link to="/export-receipts" style={{ color: 'white', textDecoration: 'none' }}>Phiếu xuất kho</Link></li>}
          <li style={{ margin: '10px 0' }}><Link to="/inventory" style={{ color: 'white', textDecoration: 'none' }}>Tồn kho</Link></li>
          {hasPermission('putaway.read') && <li style={{ margin: '10px 0' }}><Link to="/putaway-tasks" style={{ color: 'white', textDecoration: 'none' }}>Cất hàng</Link></li>}
          {showStocktakes && <li style={{ margin: '10px 0' }}><Link to="/stocktakes" style={{ color: 'white', textDecoration: 'none' }}>Kiểm kê kho</Link></li>}
          <li style={{ margin: '10px 0' }}><Link to="/stock-transfers" style={{ color: 'white', textDecoration: 'none' }}>Điều chuyển kho</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/stock-reservations" style={{ color: 'white', textDecoration: 'none' }}>Giữ hàng</Link></li>
          {canViewApprovals() && <li style={{ margin: '10px 0' }}><Link to="/approvals" style={{ color: 'white', textDecoration: 'none' }}>Phê duyệt</Link></li>}
          {hasPermission('permission.read') && <li><Link to="/permissions" style={{ color: 'white' }}>Quản trị quyền truy cập</Link></li>}
        </ul>
      </aside>

      {/* Main Content */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <header style={{ height: '60px', backgroundColor: '#ecf0f1', display: 'flex', alignItems: 'center', padding: '0 20px', justifyContent: 'flex-end' }}>
          <span style={{ marginRight: '12px' }}>{localStorage.getItem('username') || 'Người dùng'}</span>
          <button type="button" onClick={() => void logout()} title="Đăng xuất" aria-label="Đăng xuất" style={{ border: 0, background: 'transparent', cursor: 'pointer', padding: '8px' }}>
            <LogOut size={20} />
          </button>
        </header>
        
        <main style={{ padding: '20px', flex: 1, backgroundColor: '#f4f6f8' }}>
          {identityState === 'loading' ? <p role="status">Đang xác minh quyền truy cập...</p>
            : identityState === 'error' ? <p role="alert">Không thể xác minh quyền truy cập. Vui lòng tải lại.</p>
              : <Outlet />}
        </main>
      </div>
    </div>
  );
};

export default MainLayout;
