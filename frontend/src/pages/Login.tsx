import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import apiClient, { setAccessToken } from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import { permissionError } from '../services/permissionPresentation';

const Login = () => {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const navigate = useNavigate();

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const response = await apiClient.post('/api/auth/login', { username, password });
      setAccessToken(response.data.token);
      localStorage.setItem('username', response.data.username);
      localStorage.setItem('role', response.data.role);
      localStorage.setItem('userId', String(response.data.userId));
      const identity = await apiClient.get('/api/auth/me');
      setCurrentPermissions(identity.data.permissions || []);
      navigate('/');
    } catch (err: any) {
      setError(permissionError(err, 'Đăng nhập thất bại. Vui lòng kiểm tra thông tin và thử lại.'));
    }
  };

  return (
    <main className="login-page production-ui">
      <section className="login-card" aria-labelledby="login-title">
        <div className="login-heading">
          <div className="ui-eyebrow">ERP WMS</div>
          <h1 id="login-title">Đăng nhập hệ thống</h1>
          <p>Truy cập các work center theo vai trò và quyền đã được cấp.</p>
        </div>

        <form onSubmit={handleLogin} className="login-form">
          {error && <div role="alert">{error}</div>}

          <label className="ui-stack" htmlFor="login-username">
            <span>Tên đăng nhập</span>
            <input
              id="login-username"
              autoComplete="username"
              type="text"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              required
            />
          </label>

          <label className="ui-stack" htmlFor="login-password">
            <span>Mật khẩu</span>
            <input
              id="login-password"
              autoComplete="current-password"
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </label>

          <button type="submit" className="ui-primary-button">Đăng nhập</button>
        </form>
      </section>
    </main>
  );
};

export default Login;
