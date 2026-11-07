import { Fragment, useState, type CSSProperties } from 'react';
import { Check, Plus } from 'lucide-react';
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
  isDefaultGrantedForRole,
  sanitizeAndSortCustomerRoles,
  type Permission,
  type Role,
} from './PermissionsHelpersAndTypes';

const modulePillStyle = (active: boolean): CSSProperties => ({
  minHeight: 30, padding: '4px 10px', borderRadius: 999, fontSize: 12, fontWeight: active ? 700 : 600, whiteSpace: 'nowrap',
  background: active ? '#eff6ff' : '#f8fafc', color: active ? '#1e40af' : '#475569',
  border: active ? '1.5px solid #2563eb' : '1px solid #cbd5e1', cursor: 'pointer',
});

const grantSwitchStyle = (granted: boolean, pending: boolean): CSSProperties => ({
  display: 'inline-flex', alignItems: 'center', gap: 5, minHeight: 32, padding: '4px 12px',
  borderRadius: 999, fontSize: 12.5, fontWeight: 600, whiteSpace: 'nowrap',
  background: granted ? '#ECFDF5' : '#FFFFFF', color: granted ? '#047857' : '#0F172A',
  border: granted ? '1px solid #6EE7B7' : '1px solid #CBD5E1',
  cursor: pending ? 'not-allowed' : 'pointer',
});

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
  pendingGrantKey,
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
  pendingGrantKey?: string | null;
  onChangeGrant: (role: Role, code: string, revoke: boolean) => void;
  onBatchGrantModule?: (role: Role, moduleName: string, codesToGrant: string[]) => void;
}) {
  const [userPickedModule, setUserPickedModule] = useState<string | null>(null);
  const [onlyDiffDefault, setOnlyDiffDefault] = useState(false);

  const customerCatalog = filterCustomerTenantCatalog(catalog);
  const visibleRoles = sanitizeAndSortCustomerRoles(roles);
  const selectedRole = visibleRoles.find(r => r.roleName === effectiveRoleFilter) ?? visibleRoles[0] ?? null;

  const smartDefaultModule =
    customerCatalog.length > 35 ? (permissionSearch.trim() ? 'ALL' : 'Nhập kho') : 'ALL';
  const selectedModule = userPickedModule ?? smartDefaultModule;
  const activeModuleFilter = selectedModule === 'ALL' ? '' : selectedModule;

  const moduleCounts = PERMISSION_MODULE_GROUPS.map(mod => ({
    mod,
    count: customerCatalog.filter(p => permissionModuleGroup(p.code) === mod).length,
  })).filter(entry => entry.count > 0);

  const ungrantedInSelectedModule =
    selectedRole && activeModuleFilter
      ? customerCatalog
          .filter(p => permissionModuleGroup(p.code) === activeModuleFilter && !selectedRole.permissions.includes(p.code))
          .map(p => p.code)
      : [];

  return (
    <div className="ui-stack" style={{ gap: 16 }}>
      {customerCatalog.length === 0 && (
        <UiCard>
          <p className="ui-muted-text">Chưa có quyền truy cập để hiển thị.</p>
        </UiCard>
      )}

      {!canReadRoles && customerCatalog.length > 0 && (
        <UiCard title="Danh mục quyền truy cập">
          <UiTableScroll>
            <table className="permissions-table ui-table-compact" aria-label="Danh mục quyền truy cập">
              <thead>
                <tr>
                  <th style={{ minWidth: 160 }}>Quyền</th>
                  <th style={{ minWidth: 200 }}>Mô tả</th>
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
        </UiCard>
      )}

      {visibleRoles.length > 0 && (
        <div
          className="ui-stack"
          style={{
            gap: 12,
            padding: '12px 14px',
            borderRadius: 10,
            background: '#ffffff',
            border: '1px solid #e2e8f0',
          }}
        >
          <div style={{ display: 'grid', gap: 6 }}>
            <span data-testid="permissions-role-group-label" style={{ fontSize: 12, fontWeight: 700, color: '#0f172a', letterSpacing: '0.01em' }}>
              {`1. Vai trò (${visibleRoles.length}):`}
            </span>
            <div
              data-testid="permissions-role-tabs"
              role="tablist"
              aria-label="Chọn vai trò phân quyền"
              style={{
                display: 'flex',
                flexWrap: 'nowrap',
                overflowX: 'auto',
                gap: 6,
                padding: 4,
                borderRadius: 8,
                background: '#f1f5f9',
                border: '1px solid #cbd5e1',
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
                      minHeight: 34,
                      padding: '5px 14px',
                      borderRadius: 6,
                      fontSize: 12.5,
                      fontWeight: isSelected ? 700 : 600,
                      whiteSpace: 'nowrap',
                      background: isSelected ? '#1d4ed8' : '#ffffff',
                      color: isSelected ? '#ffffff' : '#334155',
                      border: isSelected ? '1px solid #1e40af' : '1px solid #cbd5e1',
                      boxShadow: isSelected ? '0 1px 3px rgba(29, 78, 216, 0.25)' : 'none',
                      cursor: 'pointer',
                    }}
                  >
                    {roleChipLabel(role.roleName, role.permissions.length)}
                  </button>
                );
              })}
            </div>
          </div>

          <div style={{ display: 'grid', gap: 6, paddingTop: 10, borderTop: '1px solid #e2e8f0' }}>
            <span data-testid="permissions-module-group-label" style={{ fontSize: 12, fontWeight: 700, color: '#334155' }}>
              2. Phân hệ nghiệp vụ:
            </span>
            <div
              data-testid="permissions-module-filter-bar"
              role="group"
              aria-label="Lọc nhanh theo phân hệ nghiệp vụ"
              style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}
            >
            <button
              type="button"
              data-testid="perm-module-pill-ALL"
              aria-pressed={selectedModule === 'ALL'}
              onClick={() => setUserPickedModule('ALL')}
              style={modulePillStyle(selectedModule === 'ALL')}
            >
              {`Tất cả quyền (${customerCatalog.length})`}
            </button>
            {moduleCounts.map(({ mod, count }) => {
              const active = selectedModule === mod;
              return (
                <button
                  key={mod}
                  type="button"
                  aria-pressed={active}
                  onClick={() => setUserPickedModule(mod)}
                  style={modulePillStyle(active)}
                >
                  {`${mod} (${count})`}
                </button>
              );
            })}
            <button
              type="button"
              data-testid="btn-toggle-diff-default"
              aria-pressed={onlyDiffDefault}
              onClick={() => setOnlyDiffDefault(v => !v)}
              style={{
                minHeight: 30, padding: '4px 10px', borderRadius: 999, fontSize: 12, fontWeight: 600,
                whiteSpace: 'nowrap', marginLeft: 'auto', cursor: 'pointer',
                background: onlyDiffDefault ? '#eff6ff' : '#ffffff',
                color: onlyDiffDefault ? '#1d4ed8' : '#475569',
                border: onlyDiffDefault ? '1px solid #93c5fd' : '1px solid #cbd5e1',
              }}
            >
              Chỉ hiện quyền khác mặc định
            </button>
            </div>
          </div>

          <div
            role="region"
            aria-label="Bộ lọc ma trận phân quyền"
            style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}
          >
            <div style={{ display: 'flex', alignItems: 'flex-end', gap: 12, flexWrap: 'wrap' }}>
              <label style={{ display: 'grid', gap: 4, textAlign: 'left', fontSize: 12, fontWeight: 600, color: '#475569' }}>
                <span>Tìm kiếm quyền</span>
                <input
                  aria-label="Tìm kiếm quyền"
                  placeholder="Tìm tên hoặc mã quyền..."
                  style={{ minWidth: 220, minHeight: 32, height: 32, padding: '2px 10px', fontSize: 12.5 }}
                  value={permissionSearch}
                  onChange={e => onChangePermissionSearch(e.target.value)}
                />
              </label>
            </div>
            {activeModuleFilter && canAssign && selectedRole && (
              <button
                type="button"
                className="ui-btn ui-btn-secondary"
                data-testid="btn-batch-grant-module"
                disabled={pending || ungrantedInSelectedModule.length === 0}
                onClick={() => onBatchGrantModule?.(selectedRole, activeModuleFilter, ungrantedInSelectedModule)}
                style={{ minHeight: 32, height: 32, padding: '2px 12px', fontSize: 12.5, whiteSpace: 'nowrap' }}
              >
                {ungrantedInSelectedModule.length > 0
                  ? `Cấp quyền nhóm đang chọn (${ungrantedInSelectedModule.length})`
                  : 'Đã cấp đủ nhóm đang chọn'}
              </button>
            )}
          </div>
        </div>
      )}

      {visibleRoles
        .filter(role => !effectiveRoleFilter || role.roleName === effectiveRoleFilter)
        .map(role => {
          const normalizedQuery = permissionSearch.trim().toLowerCase();
          const filteredCatalog = customerCatalog.filter(permission => {
            if (activeModuleFilter && permissionModuleGroup(permission.code) !== activeModuleFilter) return false;
            if (onlyDiffDefault) {
              const granted = role.permissions.includes(permission.code);
              if (granted === isDefaultGrantedForRole(role.roleName, permission.code)) return false;
            }
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
              <UiTableScroll>
                <table className="permissions-table ui-table-compact" aria-label="Danh mục quyền truy cập">
                  <thead>
                    <tr>
                      <th style={{ minWidth: 200 }}>Quyền</th>
                      <th style={{ minWidth: 160 }}>Mã quyền</th>
                      <th style={{ minWidth: 120 }}>Trạng thái quyền</th>
                    </tr>
                  </thead>
                  <tbody>
                    {groupedModules.length === 0 ? (
                      <tr>
                        <td colSpan={3} className="ui-empty-cell">
                          {onlyDiffDefault
                            ? 'Không có quyền nào khác với thiết lập mặc định trong phạm vi đang lọc.'
                            : 'Không tìm thấy quyền phù hợp với bộ lọc hiện tại.'}
                        </td>
                      </tr>
                    ) : (
                      groupedModules.map(({ group, items }) => (
                        <Fragment key={group}>
                          <tr className="permission-module-group-row" style={{ background: '#f8fafc' }}>
                            <td
                              colSpan={3}
                              style={{ fontWeight: 700, fontSize: 12, color: '#0F172A', padding: '6px 10px', borderBottom: '1px solid #e2e8f0' }}
                            >
                              {`${group} (${items.filter(p => role.permissions.includes(p.code)).length}/${items.length} quyền đã cấp)`}
                            </td>
                          </tr>
                          {items.map(permission => {
                            const granted = role.permissions.includes(permission.code);
                            const rowKey = `${role.id}:${permission.code}`;
                            const isRowPending = pending && (!pendingGrantKey || pendingGrantKey === rowKey);
                            return (
                              <tr key={permission.code}>
                                <td>{permissionLabel(permission.code)}</td>
                                <td><code className="ui-code">{permission.code}</code></td>
                                <td style={{ whiteSpace: 'nowrap' }}>
                                  {!canAssign ? (
                                    <UiBadge tone={granted ? 'success' : 'neutral'}>
                                      {granted ? 'Đã cấp' : 'Chưa cấp'}
                                    </UiBadge>
                                  ) : (
                                    <button
                                      type="button"
                                      role="switch"
                                      aria-checked={granted}
                                      aria-label={`${granted ? 'Thu hồi' : 'Cấp'} ${permissionLabel(permission.code)} cho ${roleLabel(role.roleName)}`}
                                      disabled={pending}
                                      onClick={() => onChangeGrant(role, permission.code, granted)}
                                      style={grantSwitchStyle(granted, pending)}
                                    >
                                      {isRowPending ? (
                                        'Đang lưu...'
                                      ) : granted ? (
                                        <>
                                          <Check size={13} aria-hidden="true" /> <span>Đã cấp</span>
                                        </>
                                      ) : (
                                        <>
                                          <Plus size={13} aria-hidden="true" /> <span>Cấp quyền</span>
                                        </>
                                      )}
                                    </button>
                                  )}
                                </td>
                              </tr>
                            );
                          })}
                        </Fragment>
                      ))
                    )}
                  </tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          );
        })}
    </div>
  );
}
