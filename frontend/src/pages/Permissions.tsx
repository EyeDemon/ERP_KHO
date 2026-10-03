import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { beginPermissionRefresh, setCurrentPermissions, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError, permissionLabel, roleLabel } from '../services/permissionPresentation';

type Permission = { code: string; description: string };
type Role = { id: number; roleName: string; rowVersion: string; permissions: string[] };

export default function Permissions() {
  const canReadCatalog = usePermission('permission.read');
  const canReadRoles = usePermission('role.read');
  const canAssign = usePermission('permission.assign');
  const [catalog, setCatalog] = useState<Permission[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [loading, setLoading] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const inFlight = useRef(false);
  const requestVersion = useRef(0);

  const load = useCallback(async () => {
    const version = ++requestVersion.current;
    setCatalog([]); setRoles([]); setLoading(true); setError('');
    try {
      const [permissions, roleGrants] = await Promise.all([
        canReadCatalog ? apiClient.get('/api/permissions') : Promise.resolve({ data: [] }),
        canReadRoles ? apiClient.get('/api/permissions/roles') : Promise.resolve({ data: [] }),
      ]);
      if (version === requestVersion.current) { setCatalog(permissions.data); setRoles(roleGrants.data); }
    } catch (failure) { if (version === requestVersion.current) setError(permissionError(failure)); }
    finally { if (version === requestVersion.current) setLoading(false); }
  }, [canReadCatalog, canReadRoles]);
  useEffect(() => {
    const requests = requestVersion;
    void load();
    return () => { requests.current++; };
  }, [load]);

  const change = async (role: Role, code: string, revoke: boolean) => {
    if (!canAssign || inFlight.current) return;
    if (!window.confirm(`${revoke ? 'Thu hồi' : 'Cấp'} quyền “${permissionLabel(code)}” cho ${roleLabel(role.roleName)}?`)) return;
    inFlight.current = true; setPending(true); setError(''); setSuccess('');
    const action = `permission-${revoke ? 'revoke' : 'grant'}:${role.id}:${code}:${role.rowVersion}`;
    try {
      const headers = idempotencyHeaders(action);
      if (revoke) await apiClient.delete(`/api/permissions/roles/${role.id}/grants/${encodeURIComponent(code)}`, { headers, data: { rowVersion: role.rowVersion } });
      else await apiClient.post(`/api/permissions/roles/${role.id}/grants`, { permissionCode: code, rowVersion: role.rowVersion }, { headers });
      completeIdempotentAction(action);
      const revision = beginPermissionRefresh();
      const identity = await apiClient.get('/api/auth/me'); setCurrentPermissions(identity.data.permissions, revision);
      await load(); setSuccess('Đã cập nhật quyền truy cập.');
    } catch (failure) { setRoles([]); setCatalog([]); setError(permissionError(failure)); }
    finally { inFlight.current = false; setPending(false); }
  };
  return <section aria-label="Quản trị quyền truy cập">
    <h2>Quản trị quyền truy cập</h2>
    {error && <p role="alert">{error}</p>}
    {success && <p role="status">{success}</p>}
    {loading ? <p role="status">Đang tải quyền truy cập...</p> : <>
      {catalog.length === 0 && <p>Chưa có quyền truy cập để hiển thị.</p>}
      {!canReadRoles && catalog.length > 0 && <ul aria-label="Danh mục quyền truy cập">
        {catalog.map(permission => <li key={permission.code}>{permissionLabel(permission.code)}</li>)}
      </ul>}
      {roles.map(role => <section key={role.id} aria-label={`Quyền của ${roleLabel(role.roleName)}`}>
        <h3>{roleLabel(role.roleName)}</h3>
        <ul>{catalog.map(permission => {
          const granted = role.permissions.includes(permission.code);
          return <li key={permission.code}>{permissionLabel(permission.code)} — {granted ? 'Đã cấp' : 'Chưa cấp'}
            {canAssign && <button type="button" disabled={pending} onClick={() => void change(role, permission.code, granted)}
              aria-label={`${granted ? 'Thu hồi' : 'Cấp'} ${permissionLabel(permission.code)} cho ${roleLabel(role.roleName)}`}>
              {pending ? 'Đang lưu...' : granted ? 'Thu hồi quyền' : 'Cấp quyền'}</button>}
          </li>;
        })}</ul>
      </section>)}
      <button type="button" disabled={pending || loading} onClick={() => void load()}>Tải lại</button>
    </>}
  </section>;
}
