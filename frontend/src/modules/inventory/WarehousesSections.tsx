import React from 'react';
import { Link } from 'react-router-dom';
import { UiBadge, UiCard, UiDrawer } from '../../ui/ProductionUi';

export interface Warehouse {
  id: number;
  code: string;
  name: string;
  address?: string;
  isActive: boolean;
}

export type WarehouseForm = {
  id?: number;
  code: string;
  name: string;
  address: string;
  isActive: boolean;
};

export interface NetworkMheEval {
  evaluationId: string;
  networkTopologyMode: string;
  capacityForecastStatus: string;
  mheFleetReadiness: string;
}

export const WAREHOUSE_PAGE_SIZE = 10;

export const viNetworkTerm = (val: string) =>
  ({
    HUB_AND_SPOKE: 'Mô hình Trung tâm – Vệ tinh',
    OPTIMAL_MHE_BALANCED: 'Đã cân bằng tải & thiết bị MHE tối ưu',
    CAPACITY_WITHIN_THRESHOLD: 'Sức chứa trong ngưỡng an toàn',
    MHE_FLEET_READY: 'Đội xe nâng/MHE sẵn sàng 100%',
    READY: 'Sẵn sàng',
  }[val] || val);

export const warehouseMessageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { data?: { message?: string } } };
  return response.response?.data?.message || fallback;
};

export const emptyWarehouseForm = (): WarehouseForm => ({
  code: '',
  name: '',
  address: '',
  isActive: true,
});

export const WarehousesTopologyTabs: React.FC<{ inRouter: boolean }> = ({ inRouter }) => (
  <div
    data-testid="warehouse-topology-tabs"
    style={{
      display: 'flex',
      flexWrap: 'wrap',
      gap: 8,
      marginBottom: 12,
      padding: 6,
      borderRadius: 10,
      background: '#f1f5f9',
      border: '1px solid #cbd5e1',
    }}
  >
    {[
      { to: '/warehouses', label: 'Danh mục Kho hàng', testId: 'tab-warehouses', active: true },
      { to: '/warehouse-locations', label: 'Cấu trúc & Mã vạch Vị trí Kệ', testId: 'tab-warehouse-locations', active: false },
      { to: '/warehouse-map', label: 'Bản đồ Nhiệt Kho 2D', testId: 'tab-warehouse-map', active: false },
    ].map((tab) => {
      const style: React.CSSProperties = {
        padding: '8px 14px',
        borderRadius: 8,
        fontWeight: 700,
        fontSize: 13,
        textDecoration: 'none',
        background: tab.active ? '#0f172a' : '#ffffff',
        color: tab.active ? '#ffffff' : '#334155',
        border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
      };
      return inRouter ? (
        <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
          {tab.label}
        </Link>
      ) : (
        <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
          {tab.label}
        </a>
      );
    })}
  </div>
);

interface WarehousesNetworkOverviewCardProps {
  warehouses: Warehouse[];
  activeCount: number;
  loading: boolean;
  saving: boolean;
  governanceBusy: boolean;
  networkMheEval: NetworkMheEval | null;
  onEvaluateNetworkMhe: () => void;
}

export const WarehousesNetworkOverviewCard: React.FC<WarehousesNetworkOverviewCardProps> = ({
  warehouses,
  activeCount,
  loading,
  saving,
  governanceBusy,
  networkMheEval,
  onEvaluateNetworkMhe,
}) => (
  <UiCard title="Tổng quan Mạng lưới Kho & Trạng thái Vận hành">
    <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, maxWidth: '100%' }}>
      <div className="ui-subcard">
        <strong>Tỷ lệ kho đang vận hành</strong><br />
        {activeCount}/{warehouses.length} kho hoạt động ({warehouses.length > 0 ? Math.round((activeCount / warehouses.length) * 100) : 0}%)
      </div>
      <div className="ui-subcard">
        <strong>Chuẩn hóa địa chỉ vật lý</strong><br />
        {warehouses.filter((warehouse) => Boolean(warehouse.address && warehouse.address.trim())).length}/{warehouses.length} kho đã khai báo địa chỉ
      </div>
      <div className="ui-subcard">
        <strong>Kho tạm ngưng hoạt động</strong><br />
        {warehouses.length - activeCount} kho ngừng hoạt động
      </div>
    </div>
    <div className="ui-inline-wrap">
      <button type="button" disabled={loading || saving || governanceBusy} onClick={onEvaluateNetworkMhe}>
        Đánh giá Quy hoạch Mạng lưới Kho (Trung tâm / Vệ tinh), Dự báo Sức chứa & Thiết bị MHE
      </button>
      {networkMheEval && (
        <UiBadge tone="success">
          {`Đã đánh giá ${networkMheEval.evaluationId}: ${viNetworkTerm(networkMheEval.networkTopologyMode)} • ${viNetworkTerm(networkMheEval.capacityForecastStatus)} • ${viNetworkTerm(networkMheEval.mheFleetReadiness)}`}
        </UiBadge>
      )}
    </div>
  </UiCard>
);

interface WarehouseFormDrawerProps {
  open: boolean;
  editing: boolean;
  saving: boolean;
  form: WarehouseForm;
  setForm: React.Dispatch<React.SetStateAction<WarehouseForm>>;
  formError: string;
  onClose: () => void;
  onSave: (event: React.FormEvent) => void;
}

export const WarehouseFormDrawer: React.FC<WarehouseFormDrawerProps> = ({
  open,
  editing,
  saving,
  form,
  setForm,
  formError,
  onClose,
  onSave,
}) => (
  <UiDrawer
    open={open}
    title={editing ? `Sửa kho ${form.code}` : 'Thêm kho mới'}
    busy={saving}
    onClose={onClose}
  >
    <form className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 12, maxWidth: '100%' }} onSubmit={onSave}>
      {formError && <p role="alert">{formError}</p>}

      <label className="ui-stack">
        <span>Mã kho *</span>
        <input
          aria-label="Mã kho"
          value={form.code}
          onChange={(event) => setForm((current) => ({ ...current, code: event.target.value }))}
          disabled={saving || editing}
          maxLength={50}
          placeholder="VD: WH-HCM"
          style={{ height: 38, boxSizing: 'border-box' }}
          required
        />
      </label>

      <label className="ui-stack">
        <span>Tên kho *</span>
        <input
          aria-label="Tên kho"
          value={form.name}
          onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
          disabled={saving}
          maxLength={200}
          placeholder="Nhập tên kho"
          style={{ height: 38, boxSizing: 'border-box' }}
          required
        />
      </label>

      <label className="ui-stack">
        <span>Địa chỉ</span>
        <textarea
          aria-label="Địa chỉ kho"
          value={form.address}
          onChange={(event) => setForm((current) => ({ ...current, address: event.target.value }))}
          disabled={saving}
          rows={2}
          placeholder="Nhập địa chỉ kho"
          style={{ minHeight: 60, boxSizing: 'border-box' }}
        />
      </label>

      {editing && (
        <label className="ui-checkbox-label">
          <input
            type="checkbox"
            checked={form.isActive}
            onChange={(event) => setForm((current) => ({ ...current, isActive: event.target.checked }))}
            disabled={saving}
          />
          Kho đang hoạt động
        </label>
      )}

      <div className="ui-inline-actions">
        <button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu kho'}</button>
        <button type="button" disabled={saving} onClick={onClose}>Hủy</button>
      </div>
    </form>
  </UiDrawer>
);
