import { Fragment, useState } from 'react';
import {
  PERMISSION_MODULE_GROUPS,
  permissionDescription,
  permissionLabel,
  permissionModuleGroup,
  roleChipLabel,
  roleLabel,
} from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  filterCustomerTenantCatalog,
  sanitizeAndSortCustomerRoles,
  type Permission,
  type Role,
} from './PermissionsHelpersAndTypes';

export function PermissionsRoleMatrixSection({
  catalog,
  roles,
  canReadRoles,
  canAssign,
  effectiveRoleFilter,
  onChangeRoleFilter,
  permissionSearch,
  onChangePermissionSearch,
  pending,
  onChangeGrant,
  onBatchGrantModule,
}: {
  catalog: Permission[];
  roles: Role[];
  canReadRoles: boolean;
  canAssign: boolean;
  effectiveRoleFilter: string;
  onChangeRoleFilter: (v: string) => void;
  permissionSearch: string;
  onChangePermissionSearch: (v: string) => void;
  pending: boolean;
  onChangeGrant: (role: Role, code: string, revoke: boolean) => void;
  onBatchGrantModule?: (role: Role, moduleName: string, codesToGrant: string[]) => void;
}) {
  const [moduleFilter, setModuleFilter] = useState('');
  const customerCatalog = filterCustomerTenantCatalog(catalog);
  const visibleRoles = sanitizeAndSortCustomerRoles(roles);
  const selectedRole = visibleRoles.find(r => r.roleName === effectiveRoleFilter) ?? visibleRoles[0] ?? null;
  const ungrantedInSelectedModule =
    selectedRole && moduleFilter
      ? customerCatalog
          .filter(p => permissionModuleGroup(p.code) === moduleFilter && !selectedRole.permissions.includes(p.code))
          .map(p => p.code)
      : [];

  return (
    <div>
      {customerCatalog.length === 0 && (
        <UiCard>
          <p className="ui-muted-text">Chưa có quyền truy cập để hiển thị.</p>
        </UiCard>
      )}

      {!canReadRoles && customerCatalog.length > 0 && (
        <UiCard title="Danh mục quyền truy cập">
          <div tabIndex={0} role="region" aria-label="Vùng cuộn danh mục quyền truy cập" style={{ maxHeight: 360, overflowY: 'auto' }}>
            <UiTableScroll>
              <table className="permissions-table ui-table-compact" aria-label="Danh mục quyền truy cập">
                <thead>
                  <tr>
                    <th>Quyền</th>
                    <th>Mô tả</th>
                  </tr>
                </thead>
                <tbody>
                  {customerCatalog.map(permission => (
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

      {visibleRoles.length > 0 && (
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
            {visibleRoles.map(role => {
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
                  {roleChipLabel(role.roleName, role.permissions.length)}
                </button>
              );
            })}
          </div>

          <div role="region" aria-label="Bộ lọc ma trận phân quyền" style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, fontWeight: 600, color: '#475569' }}>
              <span>Phân hệ</span>
              <select
                aria-label="Lọc theo phân hệ"
                data-testid="select-permission-module"
                value={moduleFilter}
                onChange={e => setModuleFilter(e.target.value)}
                style={{ minHeight: 32, height: 32, padding: '2px 8px', fontSize: 12.5 }}
              >
                <option value="">Tất cả phân hệ</option>
                {PERMISSION_MODULE_GROUPS.map(group => (
                  <option key={group} value={group}>
                    {group}
                  </option>
                ))}
              </select>
            </label>
            {moduleFilter && canAssign && selectedRole && (
              <button
                type="button"
                className="ui-btn ui-btn-secondary"
                data-testid="btn-batch-grant-module"
                disabled={pending || ungrantedInSelectedModule.length === 0}
                onClick={() => onBatchGrantModule?.(selectedRole, moduleFilter, ungrantedInSelectedModule)}
                style={{ minHeight: 32, height: 32, padding: '2px 10px', fontSize: 12.5, whiteSpace: 'nowrap' }}
              >
                {ungrantedInSelectedModule.length > 0
                  ? `Cấp quyền nhóm đang chọn (${ungrantedInSelectedModule.length})`
                  : 'Đã cấp đủ nhóm đang chọn'}
              </button>
            )}
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

      {visibleRoles
        .filter(role => !effectiveRoleFilter || role.roleName === effectiveRoleFilter)
        .map(role => {
          const normalizedQuery = permissionSearch.trim().toLowerCase();
          const filteredCatalog = customerCatalog.filter(permission => {
            if (moduleFilter && permissionModuleGroup(permission.code) !== moduleFilter) return false;
            if (!normalizedQuery) return true;
            return (
              permission.code.toLowerCase().includes(normalizedQuery) ||
              permissionLabel(permission.code).toLowerCase().includes(normalizedQuery)
            );
          });
          const groupedModules = PERMISSION_MODULE_GROUPS.map(group => ({
            group,
            items: filteredCatalog.filter(p => permissionModuleGroup(p.code) === group),
          })).filter(g => g.items.length > 0);
          return (
            <UiCard key={role.id} title={roleLabel(role.roleName)}>
              <div tabIndex={0} role="region" aria-label={`Vùng cuộn danh sách quyền của ${roleLabel(role.roleName)}`} style={{ maxHeight: 420, overflowY: 'auto' }}>
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
                      {groupedModules.map(({ group, items }) => (
                        <Fragment key={group}>
                          <tr className="permission-module-group-row" style={{ background: '#f8fafc' }}>
                            <td
                              colSpan={canAssign ? 4 : 3}
                              style={{ fontWeight: 700, fontSize: 12, color: '#1e3a8a', padding: '6px 10px', borderBottom: '1px solid #e2e8f0' }}
                            >
                              {`${group} (${items.filter(p => role.permissions.includes(p.code)).length}/${items.length} quyền đã cấp)`}
                            </td>
                          </tr>
                          {items.map(permission => {
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
                                      className={granted ? 'ui-btn-secondary' : undefined}
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
                        </Fragment>
                      ))}
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
