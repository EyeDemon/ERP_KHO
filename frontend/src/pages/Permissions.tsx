import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { beginPermissionRefresh, setCurrentPermissions, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError, permissionLabel, roleLabel } from '../services/permissionPresentation';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';

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
  return <UiPage>
    <UiPageHeader
      eyebrow="Kiểm soát"
      title="Quản trị quyền truy cập"
      description="Theo dõi permission catalog và grant theo role. Mọi thay đổi quyền cần tuân thủ concurrency và idempotency."
      actions={<button type="button" disabled={pending || loading} onClick={() => void load()}>Tải lại</button>}
    />

    {error && <p role="alert">{error}</p>}
    {success && <p role="status" style={{ color: '#256b45', margin: 0 }}>{success}</p>}

    <UiMetricGrid>
      <UiMetric value={catalog.length} label="Permission trong catalog" />
      <UiMetric value={roles.length} label="Role đang hiển thị" />
      <UiMetric value={roles.reduce((sum, role) => sum + role.permissions.length, 0)} label="Grant hiện tại" />
    </UiMetricGrid>

    {loading ? <p role="status">Đang tải quyền truy cập...</p> : <>
      {catalog.length === 0 && <UiCard><p style={{ margin: 0 }}>Chưa có quyền truy cập để hiển thị.</p></UiCard>}

      {!canReadRoles && catalog.length > 0 && <UiCard title="Danh mục quyền truy cập">
        <table aria-label="Danh mục quyền truy cập">
          <thead><tr><th>Quyền</th><th>Mã permission</th></tr></thead>
          <tbody>{catalog.map(permission => <tr key={permission.code}><td>{permissionLabel(permission.code)}</td><td><code>{permission.code}</code></td></tr>)}</tbody>
        </table>
      </UiCard>}

      {roles.map(role => <UiCard key={role.id} title={roleLabel(role.roleName)}>
        <table aria-label={`Quyền của ${roleLabel(role.roleName)}`}>
          <thead><tr><th>Quyền</th><th>Mã permission</th><th>Trạng thái</th>{canAssign && <th>Thao tác</th>}</tr></thead>
          <tbody>{catalog.map(permission => {
            const granted = role.permissions.includes(permission.code);
            return <tr key={permission.code}>
              <td>{permissionLabel(permission.code)}</td>
              <td><code>{permission.code}</code></td>
              <td><UiBadge tone={granted ? 'success' : 'neutral'}>{granted ? 'Đã cấp' : 'Chưa cấp'}</UiBadge></td>
              {canAssign && <td>
                <button type="button" disabled={pending} onClick={() => void change(role, permission.code, granted)}
                  aria-label={`${granted ? 'Thu hồi' : 'Cấp'} ${permissionLabel(permission.code)} cho ${roleLabel(role.roleName)}`}>
                  {pending ? 'Đang lưu...' : granted ? 'Thu hồi quyền' : 'Cấp quyền'}
                </button>
              </td>}
            </tr>;
          })}</tbody>
        </table>
      </UiCard>)}
    </>}
  </UiPage>;
}
