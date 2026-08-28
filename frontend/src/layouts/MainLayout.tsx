import { Outlet, Link } from 'react-router-dom';
import { LogOut } from 'lucide-react';
import { logout } from '../services/apiClient';
import { canViewStocktakes, currentRole } from '../services/authorization';

const MainLayout = () => {
  const showStocktakes = canViewStocktakes(currentRole());
  return (
    <div style={{ display: 'flex', minHeight: '100vh', fontFamily: 'Arial, sans-serif' }}>
      {/* Sidebar */}
      <aside style={{ width: '250px', backgroundColor: '#2c3e50', color: 'white', padding: '20px' }}>
        <h2>ERP KHO</h2>
        <ul style={{ listStyle: 'none', padding: 0 }}>
          <li style={{ margin: '10px 0' }}><Link to="/" style={{ color: 'white', textDecoration: 'none' }}>Dashboard</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/products" style={{ color: 'white', textDecoration: 'none' }}>Sản phẩm</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/warehouses" style={{ color: 'white', textDecoration: 'none' }}>Kho hàng</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/units" style={{ color: 'white', textDecoration: 'none' }}>Đơn vị tính</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/export-receipts" style={{ color: 'white', textDecoration: 'none' }}>Phiếu xuất kho</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/inventory" style={{ color: 'white', textDecoration: 'none' }}>Tồn kho</Link></li>
          {showStocktakes && <li style={{ margin: '10px 0' }}><Link to="/stocktakes" style={{ color: 'white', textDecoration: 'none' }}>Kiểm kê kho</Link></li>}
          <li style={{ margin: '10px 0' }}><Link to="/stock-transfers" style={{ color: 'white', textDecoration: 'none' }}>Điều chuyển kho</Link></li>
          <li style={{ margin: '10px 0' }}><Link to="/stock-reservations" style={{ color: 'white', textDecoration: 'none' }}>Giữ hàng</Link></li>
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
          <Outlet />
        </main>
      </div>
    </div>
  );
};

export default MainLayout;
