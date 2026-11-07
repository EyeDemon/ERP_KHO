import React, { type FormEvent, type RefObject } from 'react';
import { Link } from 'react-router-dom';
import type { TraceabilityWarehouse } from '../../services/traceabilityWarehouses';
import { UiCard, UiToolbarField } from '../../ui/ProductionUi';

export interface TraceabilityFilterFormState {
  warehouseId: string;
  productId: string;
  lotNumber: string;
  serialNumber: string;
  referenceType: string;
  referenceId: string;
}

export const ReconciliationTraceabilityTabsBar: React.FC<{ inRouter: boolean; activeRoute: 'reconciliation' | 'traceability' }> = ({
  inRouter,
  activeRoute,
}) => (
  <div
    data-testid="reconciliation-traceability-tabs"
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
      {
        to: '/inventory-reconciliation',
        label: 'Đối chiếu Tồn kho & Hàng đợi Đồng bộ Sổ cái',
        testId: 'tab-inventory-reconciliation',
        active: activeRoute === 'reconciliation',
      },
      {
        to: '/inventory-traceability',
        label: 'Truy vết & Phả hệ Lô / Sê-ri',
        testId: 'tab-inventory-traceability',
        active: activeRoute === 'traceability',
      },
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

interface InventoryTraceabilityFilterCardProps {
  form: TraceabilityFilterFormState;
  updateField: (field: keyof TraceabilityFilterFormState, value: string) => void;
  warehouses: TraceabilityWarehouse[];
  warehousesLoading: boolean;
  warehousesError: string;
  onReloadWarehouses: () => void;
  eventLimit: 50 | 100 | 200 | 500;
  onChangeEventLimit: (limit: 50 | 100 | 200 | 500) => void;
  loading: boolean;
  referencePairErrorActive: boolean;
  validationError: string;
  resultStatus: string;
  productIdRef: RefObject<HTMLInputElement | null>;
  referenceTypeRef: RefObject<HTMLInputElement | null>;
  referenceIdRef: RefObject<HTMLInputElement | null>;
  onSearch: (e: FormEvent) => void;
  onClearFilters: () => void;
}

export const InventoryTraceabilityFilterCard: React.FC<InventoryTraceabilityFilterCardProps> = ({
  form,
  updateField,
  warehouses,
  warehousesLoading,
  warehousesError,
  onReloadWarehouses,
  eventLimit,
  onChangeEventLimit,
  loading,
  referencePairErrorActive,
  validationError,
  resultStatus,
  productIdRef,
  referenceTypeRef,
  referenceIdRef,
  onSearch,
  onClearFilters,
}) => (
  <UiCard title="Điều kiện truy vết">
    <form
      onSubmit={onSearch}
      noValidate
      className="ui-form-grid traceability-filter-grid"
      style={{ maxWidth: '100%' }}
      aria-busy={loading}
    >
      <UiToolbarField label="Kho truy vết">
        <select
          aria-label="Kho truy vết"
          value={form.warehouseId}
          disabled={warehousesLoading || Boolean(warehousesError) || warehouses.length === 0}
          aria-describedby="traceability-warehouse-help"
          onChange={(e) => updateField('warehouseId', e.target.value)}
        >
          <option value="">Tất cả kho được phân quyền</option>
          {warehouses.map((x) => (
            <option key={x.id} value={x.id}>
              {x.code} — {x.name}
            </option>
          ))}
        </select>
      </UiToolbarField>
      <UiToolbarField label="ID sản phẩm">
        <input
          ref={productIdRef}
          type="text"
          value={form.productId}
          onChange={(e) => updateField('productId', e.target.value)}
          inputMode="numeric"
          placeholder="VD: 1001"
        />
      </UiToolbarField>
      <UiToolbarField label="Mã lô">
        <input value={form.lotNumber} onChange={(e) => updateField('lotNumber', e.target.value)} autoComplete="off" />
      </UiToolbarField>
      <UiToolbarField label="Số sê-ri">
        <input value={form.serialNumber} onChange={(e) => updateField('serialNumber', e.target.value)} autoComplete="off" />
      </UiToolbarField>
      <UiToolbarField label="Loại tham chiếu">
        <input
          ref={referenceTypeRef}
          value={form.referenceType}
          onChange={(e) => updateField('referenceType', e.target.value)}
          aria-invalid={referencePairErrorActive || undefined}
          aria-describedby={referencePairErrorActive ? 'traceability-reference-help traceability-validation-error' : 'traceability-reference-help'}
          autoComplete="off"
        />
      </UiToolbarField>
      <UiToolbarField label="ID tham chiếu">
        <input
          ref={referenceIdRef}
          type="text"
          min="1"
          value={form.referenceId}
          onChange={(e) => updateField('referenceId', e.target.value)}
          aria-invalid={referencePairErrorActive || undefined}
          aria-describedby={referencePairErrorActive ? 'traceability-reference-help traceability-validation-error' : 'traceability-reference-help'}
          inputMode="numeric"
        />
      </UiToolbarField>
      <UiToolbarField label="Giới hạn sự kiện">
        <select
          aria-label="Giới hạn sự kiện truy vết"
          value={eventLimit}
          disabled={loading}
          onChange={(e) => onChangeEventLimit(Number(e.target.value) as 50 | 100 | 200 | 500)}
        >
          <option value={50}>50 sự kiện</option>
          <option value={100}>100 sự kiện</option>
          <option value={200}>200 sự kiện</option>
          <option value={500}>500 sự kiện</option>
        </select>
      </UiToolbarField>
      <div className="ui-inline-actions" style={{ alignSelf: 'end', height: 38, alignItems: 'center' }}>
        <button type="submit" className="ui-primary-button" style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }} disabled={loading}>
          {loading ? 'Đang truy vết...' : 'Truy vết'}
        </button>
        <button type="button" style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }} disabled={loading} onClick={onClearFilters}>
          Xóa bộ lọc
        </button>
      </div>
      <p id="traceability-warehouse-help" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
        Chọn một kho để xem toàn bộ tồn hiện tại và sự kiện gần nhất; 500 nhóm tồn mỗi trang. Để truy vết nhiều kho, cần nhập thêm sản phẩm, lô, sê-ri hoặc chứng từ.
      </p>
      {warehousesLoading && (
        <p role="status" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
          Đang tải kho được cấp quyền...
        </p>
      )}
      {warehousesError && (
        <div className="ui-stack" style={{ gridColumn: '1 / -1' }}>
          <p role="alert">{warehousesError}</p>
          <button type="button" onClick={onReloadWarehouses}>
            Tải lại danh sách kho
          </button>
        </div>
      )}
      {!warehousesLoading && !warehousesError && warehouses.length === 0 && (
        <p role="status" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
          Chưa có kho được cấp quyền truy vết. Liên hệ quản trị để kiểm tra quyền truy cập.
        </p>
      )}
      <p id="traceability-reference-help" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
        Loại tham chiếu và ID tham chiếu là một cặp; nhập cả hai khi truy theo chứng từ. Kết hợp chứng từ với bộ lọc khác chỉ lấy nhóm tồn liên quan đến chứng từ đó, kể cả khi giới hạn sự kiện thấp.
      </p>
      {validationError && (
        <p id="traceability-validation-error" role="alert" style={{ gridColumn: '1 / -1', margin: 0 }}>
          {validationError}
        </p>
      )}
      {resultStatus && (
        <p role="status" aria-live="polite" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
          {resultStatus}
        </p>
      )}
    </form>
  </UiCard>
);
