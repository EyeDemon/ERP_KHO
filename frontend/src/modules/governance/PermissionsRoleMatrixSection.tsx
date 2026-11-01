import { permissionDescription, permissionLabel, roleLabel } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { Permission, Role } from './PermissionsHelpersAndTypes';

export function PermissionsRoleMatrixSection({
  catalog,
  roles,
  canReadRoles,
  canAssign,
  roleFilter,
  effectiveRoleFilter,
  onChangeRoleFilter,
  permissionSearch,
  onChangePermissionSearch,
  pending,
  onChangeGrant,
}: {
  catalog: Permission[];
  roles: Role[];
  canReadRoles: boolean;
  canAssign: boolean;
  roleFilter: string;
  effectiveRoleFilter: string;
  onChangeRoleFilter: (v: string) => void;
  permissionSearch: string;
  onChangePermissionSearch: (v: string) => void;
  pending: boolean;
  onChangeGrant: (role: Role, code: string, revoke: boolean) => void;
}) {
  return (
    <div>
      {catalog.length === 0 && (
        <UiCard>
          <p className="ui-muted-text">Chưa có quyền truy cập để hiển thị.</p>
        </UiCard>
      )}

      {!canReadRoles && catalog.length > 0 && (
        <UiCard title="Danh mục quyền truy cập">
          <div style={{ maxHeight: 360, overflowY: 'auto' }}>
            <UiTableScroll>
              <table className="permissions-table ui-table-compact" aria-label="Danh mục quyền truy cập">
                <thead>
                  <tr>
                    <th>Quyền</th>
                    <th>Mô tả</th>
                  </tr>
                </thead>
                <tbody>
                  {catalog.map(permission => (
                    <tr key={permission.code}>
                      <td>{permissionLabel(permission.code)}</td>
                      <td>{permissionDescription(permission.code, permission.description)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        </UiCard>
      )}

      {roles.length > 0 && (
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 8,
            padding: '6px 10px',
            borderRadius: 10,
            background: '#ffffff',
            border: '1px solid #e2e8f0',
          }}
        >
          <div
            data-testid="permissions-role-tabs"
            role="tablist"
            aria-label="Chọn vai trò phân quyền"
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              gap: 4,
              padding: 3,
              borderRadius: 8,
              background: '#f1f5f9',
              border: '1px solid #e2e8f0',
            }}
          >
            {roles.map(role => {
              const isSelected = effectiveRoleFilter === role.roleName;
              return (
                <button
                  key={role.id}
                  type="button"
                  role="tab"
                  aria-selected={isSelected}
                  data-testid={`role-tab-${role.roleName}`}
                  onClick={() => onChangeRoleFilter(role.roleName)}
                  style={{
                    minHeight: 30,
                    padding: '4px 10px',
                    borderRadius: 6,
                    fontSize: 12.5,
                    fontWeight: 600,
                    background: isSelected ? '#ffffff' : 'transparent',
                    color: isSelected ? '#0f172a' : '#475569',
                    border: isSelected ? '1px solid #cbd5e1' : '1px solid transparent',
                    boxShadow: isSelected ? '0 1px 2px rgba(15, 23, 42, 0.06)' : 'none',
                    cursor: 'pointer',
                  }}
                >
                  {`${roleLabel(role.roleName)} (${role.permissions.length})`}
                </button>
              );
            })}
          </div>

          <div role="region" aria-label="Bộ lọc ma trận phân quyền" style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, fontWeight: 600, color: '#475569' }}>
              <span>Lọc theo vai trò</span>
              <select
                aria-label="Lọc theo vai trò"
                value={roleFilter === '__ALL__' ? '__ALL__' : effectiveRoleFilter}
                onChange={e => onChangeRoleFilter(e.target.value || '__ALL__')}
                style={{ minHeight: 32, height: 32, padding: '2px 8px', fontSize: 12.5 }}
              >
                {roles.map(role => (
                  <option key={role.id} value={role.roleName}>
                    {`${roleLabel(role.roleName)} (${role.roleName})`}
                  </option>
                ))}
                <option value="__ALL__">Tất cả vai trò ({roles.length})</option>
              </select>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, fontWeight: 600, color: '#475569' }}>
              <span>Tìm kiếm quyền</span>
              <input
                aria-label="Tìm kiếm quyền"
                placeholder="Tìm tên hoặc mã quyền..."
                style={{ minWidth: 210, minHeight: 32, height: 32, padding: '2px 10px', fontSize: 12.5 }}
                value={permissionSearch}
                onChange={e => onChangePermissionSearch(e.target.value)}
              />
            </label>
          </div>
        </div>
      )}

      {roles
        .filter(role => !effectiveRoleFilter || role.roleName === effectiveRoleFilter)
        .map(role => {
          const normalizedQuery = permissionSearch.trim().toLowerCase();
          const filteredCatalog = normalizedQuery
            ? catalog.filter(
                permission =>
                  permission.code.toLowerCase().includes(normalizedQuery) ||
                  permissionLabel(permission.code).toLowerCase().includes(normalizedQuery),
              )
            : catalog;
          return (
            <UiCard key={role.id} title={roleLabel(role.roleName)}>
              <div style={{ maxHeight: 360, overflowY: 'auto' }}>
                <UiTableScroll>
                  <table className="permissions-table ui-table-compact" aria-label={`Quyền của ${roleLabel(role.roleName)}`}>
                    <thead>
                      <tr>
                        <th>Quyền</th>
                        <th>Mã quyền</th>
                        <th>Trạng thái</th>
                        {canAssign && <th>Thao tác</th>}
                      </tr>
                    </thead>
                    <tbody>
                      {filteredCatalog.map(permission => {
                        const granted = role.permissions.includes(permission.code);
                        return (
                          <tr key={permission.code}>
                            <td>{permissionLabel(permission.code)}</td>
                            <td>
                              <code>{permission.code}</code>
                            </td>
                            <td>
                              <UiBadge tone={granted ? 'success' : 'neutral'}>{granted ? 'Đã cấp' : 'Chưa cấp'}</UiBadge>
                            </td>
                            {canAssign && (
                              <td>
                                <button
                                  type="button"
                                  disabled={pending}
                                  onClick={() => onChangeGrant(role, permission.code, granted)}
                                  aria-label={`${granted ? 'Thu hồi' : 'Cấp'} ${permissionLabel(permission.code)} cho ${roleLabel(role.roleName)}`}
                                >
                                  {pending ? 'Đang lưu...' : granted ? 'Thu hồi quyền' : 'Cấp quyền'}
                                </button>
                              </td>
                            )}
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </UiTableScroll>
              </div>
            </UiCard>
          );
        })}
    </div>
  );
}
