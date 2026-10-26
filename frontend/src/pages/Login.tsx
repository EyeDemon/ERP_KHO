import { useEffect, useState } from 'react';
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
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    const clearAutofill = () => {
      const u = document.getElementById('login-username') as HTMLInputElement | null;
      const p = document.getElementById('login-password') as HTMLInputElement | null;
      if (u && u.value) u.value = '';
      if (p && p.value) p.value = '';
      setUsername('');
      setPassword('');
    };
    clearAutofill();
    const timer = window.setTimeout(clearAutofill, 120);
    return () => window.clearTimeout(timer);
  }, []);

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
          <p>Truy cập các phân hệ nghiệp vụ kho theo vai trò và quyền đã được cấp.</p>
        </div>

        <form onSubmit={handleLogin} className="login-form" autoComplete="off">
          {error && <div role="alert">{error}</div>}

          <label className="ui-stack" htmlFor="login-tenant-code">
            <span>Mã doanh nghiệp</span>
            <input
              id="login-tenant-code"
              autoComplete="off"
              type="text"
              placeholder="VD: MA-DOANH-NGHIEP (để trống = mặc định)"
              value={tenantCode}
              onChange={(event) => setTenantCode(event.target.value)}
            />
          </label>

          <label className="ui-stack" htmlFor="login-username">
            <span>Tên đăng nhập</span>
            <input
              id="login-username"
              autoComplete="off"
              type="text"
              placeholder="Nhập tên đăng nhập"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              required
            />
          </label>

          <div className="ui-stack">
            <label htmlFor="login-password" style={{ fontWeight: 700, fontSize: 13 }}>
              <span>Mật khẩu</span>
            </label>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', width: '100%' }}>
              <input
                id="login-password"
                autoComplete="current-password"
                type={showPassword ? 'text' : 'password'}
                placeholder="Nhập mật khẩu"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
                style={{ flex: 1, minWidth: 0, width: 'auto' }}
              />
              <button
                type="button"
                data-testid="toggle-password-visibility"
                aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
                aria-pressed={showPassword}
                onClick={() => setShowPassword((prev) => !prev)}
                style={{ width: 'auto', minHeight: 40, padding: '0 14px', whiteSpace: 'nowrap', flexShrink: 0 }}
              >
                {showPassword ? 'Ẩn' : 'Hiện'}
              </button>
            </div>
          </div>

          <button type="submit" className="ui-primary-button" disabled={submitting}>
            {submitting ? 'Đang đăng nhập...' : 'Đăng nhập'}
          </button>
        </form>
      </section>
    </main>
  );
};

export default Login;
