import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import apiClient, { setAccessToken } from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import { permissionError } from '../services/permissionPresentation';

export const parseTenantLoginInput = (
  rawUsername: string,
  rawTenantCode: string,
): { username: string; tenantCode?: string } => {
  let resolvedUsername = rawUsername.trim();
  let resolvedTenant = rawTenantCode.trim();

  if (resolvedUsername.includes('\\')) {
    const [prefix, userPart] = resolvedUsername.split('\\', 2);
    if (prefix?.trim() && userPart?.trim()) {
      if (!resolvedTenant) resolvedTenant = prefix.trim();
      resolvedUsername = userPart.trim();
    }
  } else if (resolvedUsername.includes('@')) {
    const atIndex = resolvedUsername.lastIndexOf('@');
    const userPart = resolvedUsername.slice(0, atIndex).trim();
    const suffix = resolvedUsername.slice(atIndex + 1).trim();
    if (userPart && suffix) {
      if (!resolvedTenant) resolvedTenant = suffix;
      resolvedUsername = userPart;
    }
  }

  return resolvedTenant
    ? { username: resolvedUsername || rawUsername, tenantCode: resolvedTenant.toUpperCase() }
    : { username: resolvedUsername || rawUsername };
};

const Login = () => {
  const [tenantCode, setTenantCode] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const navigate = useNavigate();

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError('');
    try {
      const parsed = parseTenantLoginInput(username, tenantCode);
      const payload = parsed.tenantCode
        ? { username: parsed.username, password, tenantCode: parsed.tenantCode }
        : { username: parsed.username, password };

      const response = await apiClient.post('/api/auth/login', payload);
      const resolvedTenantId = String(response.data.tenantId ?? 1);
      const resolvedTenantCode = String(
        response.data.tenantCode || parsed.tenantCode || `TENANT-${resolvedTenantId}`,
      );

      const previousTenantId = localStorage.getItem('tenantId');
      if (previousTenantId && previousTenantId !== resolvedTenantId) {
        sessionStorage.clear();
      }

      setAccessToken(response.data.token);
      localStorage.setItem('username', response.data.username);
      localStorage.setItem('role', response.data.role);
      localStorage.setItem('userId', String(response.data.userId));
      localStorage.setItem('tenantId', resolvedTenantId);
      localStorage.setItem('tenantCode', resolvedTenantCode);

      const identity = await apiClient.get('/api/auth/me');
      setCurrentPermissions(identity.data.permissions || []);
      navigate('/');
    } catch (err: any) {
      if (err?.response?.status === 401) {
        const msg = err?.response?.data?.message;
        setError(
          typeof msg === 'string' && msg.trim() && msg.trim() !== 'Invalid credentials.'
            ? msg.trim()
            : 'Sai tên đăng nhập hoặc mật khẩu. Vui lòng kiểm tra và thử lại.'
        );
      } else {
        setError(permissionError(err, 'Đăng nhập thất bại. Vui lòng kiểm tra thông tin và thử lại.'));
      }
    } finally {
      setSubmitting(false);
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

          <label className="ui-stack" htmlFor="login-tenant-code">
            <span>Mã doanh nghiệp (Tenant)</span>
            <input
              id="login-tenant-code"
              autoComplete="organization"
              type="text"
              placeholder="Mặc định: TENANT-1 (hoặc nhập user@TENANT-202)"
              value={tenantCode}
              onChange={(event) => setTenantCode(event.target.value)}
            />
          </label>

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

          <button type="submit" className="ui-primary-button" disabled={submitting}>
            {submitting ? 'Đang đăng nhập...' : 'Đăng nhập'}
          </button>
        </form>
      </section>
    </main>
  );
};

export default Login;
