import React from 'react';
import type { FormEvent } from 'react';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  OutboxMessageItem,
  PagedResult,
  ReconciliationRow,
  SiteReadinessVerdict,
  WarehouseOption,
} from './inventoryReconciliationValidators';
import { numberFormat } from './inventoryReconciliationValidators';
import { ReconciliationGovernanceCards } from './ReconciliationGovernanceCards';

interface ReconciliationGovernanceAndTableProps {
  currentRows: ReconciliationRow[];
  mismatchCount: number;
  indeterminateCount: number;
  loading: boolean;
  demoRuntime: boolean;
  investigating: boolean;
  appliedWarehouseId: string;
  outboxMessages: OutboxMessageItem[];
  outboxDispatchNotice: string;
  setOutboxDispatchNotice: (val: string) => void;
  rebuildNotice: string;
  setRebuildNotice: (val: string) => void;
  siteReadinessVerdict: SiteReadinessVerdict | null;
  setSiteReadinessVerdict: (val: SiteReadinessVerdict | null) => void;
  setError: (val: string) => void;
  warehouseId: string;
  setWarehouseId: (val: string) => void;
  warehouses: WarehouseOption[];
  warehouseError: string;
  productId: string;
  setProductId: (val: string) => void;
  productInputRef: React.RefObject<HTMLInputElement | null>;
  filterError: string;
  keyword: string;
  setKeyword: (val: string) => void;
  error: string;
  onSubmit: (event: FormEvent) => void;
  onReset: () => void;
  result: PagedResult<ReconciliationRow> | null;
  page: number;
  setPage: React.Dispatch<React.SetStateAction<number>>;
  onOpenInvestigation: (row: ReconciliationRow, trigger: HTMLButtonElement) => void;
}

const rightNumStyle: React.CSSProperties = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' };

export const ReconciliationGovernanceAndTable: React.FC<ReconciliationGovernanceAndTableProps> = ({
  currentRows, mismatchCount, indeterminateCount, loading, demoRuntime, investigating, appliedWarehouseId,
  outboxMessages, outboxDispatchNotice, setOutboxDispatchNotice, rebuildNotice, setRebuildNotice,
  siteReadinessVerdict, setSiteReadinessVerdict, setError, warehouseId, setWarehouseId, warehouses, warehouseError,
  productId, setProductId, productInputRef, filterError, keyword, setKeyword, error, onSubmit, onReset,
  result, page, setPage, onOpenInvestigation,
}) => (
  <>
    <ReconciliationGovernanceCards
      currentRows={currentRows}
      mismatchCount={mismatchCount}
      indeterminateCount={indeterminateCount}
      loading={loading}
      appliedWarehouseId={appliedWarehouseId}
      outboxMessages={outboxMessages}
      outboxDispatchNotice={outboxDispatchNotice}
      setOutboxDispatchNotice={setOutboxDispatchNotice}
      rebuildNotice={rebuildNotice}
      setRebuildNotice={setRebuildNotice}
      siteReadinessVerdict={siteReadinessVerdict}
      setSiteReadinessVerdict={setSiteReadinessVerdict}
      setError={setError}
    />

    <form noValidate onSubmit={onSubmit}>
      <UiToolbar>
        <UiToolbarField label="Kho">
          <select value={warehouseId} onChange={(event) => setWarehouseId(event.target.value)} disabled={!!warehouseError}>
            <option value="">Tất cả kho được phép</option>
            {warehouses.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="ID sản phẩm">
          <input ref={productInputRef} type="text" inputMode="numeric" value={productId} onChange={(event) => setProductId(event.target.value)} placeholder="VD: 1001" aria-invalid={!!filterError} />
        </UiToolbarField>
        <UiToolbarField label="Mã / tên sản phẩm">
          <input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="Tìm theo mã hoặc tên" />
        </UiToolbarField>
        <div className="reconciliation-filter-actions">
          <button type="submit">Đối chiếu</button>
          <button type="button" onClick={onReset}>Xóa lọc</button>
        </div>
      </UiToolbar>
    </form>

    {warehouseError && <div role="alert">{warehouseError}</div>}
    {filterError && <div role="alert">{filterError}</div>}
    {error && <div role="alert">{error}</div>}

    <UiCard title="Kết quả đối chiếu">
      {loading ? (
        <p role="status">Đang đối chiếu tồn kho...</p>
      ) : (
        <UiTableScroll>
          <table className="reconciliation-table" aria-label="Bảng đối chiếu tồn kho và sổ cái">
            <thead>
              <tr>
                <th style={{ minWidth: 115 }}>Kho</th>
                <th style={{ minWidth: 150 }}>Sản phẩm</th>
                <th style={{ ...rightNumStyle, minWidth: 96, whiteSpace: 'nowrap' }}>Tồn hiện tại</th>
                <th style={{ ...rightNumStyle, minWidth: 105, whiteSpace: 'nowrap' }}>Sổ cái kỳ vọng</th>
                <th style={{ ...rightNumStyle, minWidth: 92, whiteSpace: 'nowrap' }}>Chênh lệch</th>
                <th style={{ minWidth: 115 }}>Trạng thái khả dụng</th>
                <th style={{ minWidth: 145 }}>Đối chiếu 8 trạng thái</th>
                <th style={{ minWidth: 155 }}>Thành phần biến động</th>
                <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Bằng chứng</th>
              </tr>
            </thead>
            <tbody>
              {currentRows.length === 0 ? (
                <tr><td colSpan={9} className="reconciliation-empty">Không có dữ liệu phù hợp. Hãy thay đổi bộ lọc và thử lại.</td></tr>
              ) : (
                currentRows.map((row) => {
                  const matches = row.status === 'Match';
                  const indeterminate = row.status === 'Indeterminate' || row.expectedQuantity == null || row.difference == null;
                  return (
                    <tr
                      key={row.warehouseId + '-' + row.productId}
                      className={indeterminate || row.allStatusStatus === 'Indeterminate' ? 'reconciliation-indeterminate' : !matches || row.allStatusStatus === 'Mismatch' ? 'reconciliation-mismatch' : undefined}
                    >
                      <td>{row.warehouseName}</td>
                      <td>
                        <strong className="ui-code" style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{row.productCode}</strong>
                        <span className="reconciliation-product-name">{row.productName}</span>
                      </td>
                      <td style={rightNumStyle}>{numberFormat.format(row.currentQuantity)}</td>
                      <td style={rightNumStyle}>{row.expectedQuantity == null ? 'Chưa xác định' : numberFormat.format(row.expectedQuantity)}</td>
                      <td style={rightNumStyle} className={indeterminate ? undefined : row.difference === 0 ? 'reconciliation-difference-zero' : 'reconciliation-difference-alert'}>
                        {row.difference == null ? 'Chưa xác định' : numberFormat.format(row.difference)}
                      </td>
                      <td><UiBadge tone={indeterminate ? 'warning' : matches ? 'success' : 'danger'}>{indeterminate ? 'Chưa xác định' : matches ? 'Khớp' : 'Lệch'}</UiBadge></td>
                      <td>
                        {row.allStatusStatus ? (
                          <div className="reconciliation-movement">
                            <UiBadge tone={row.allStatusStatus === 'Indeterminate' ? 'warning' : row.allStatusStatus === 'Match' ? 'success' : 'danger'}>
                              {row.allStatusStatus === 'Indeterminate' ? 'Chưa xác định' : row.allStatusStatus === 'Match' ? 'Khớp tất cả' : 'Lệch theo trạng thái'}
                            </UiBadge>
                            <span>Tồn: {numberFormat.format(row.allStatusCurrentQuantity ?? 0)}</span>
                            <span>Sổ cái: {row.allStatusExpectedQuantity == null ? 'Chưa xác định' : numberFormat.format(row.allStatusExpectedQuantity)}</span>
                            <span>Chênh lệch tổng: {row.allStatusDifference == null ? 'Chưa xác định' : numberFormat.format(row.allStatusDifference)}</span>
                            {row.historyInsufficientForNonAvailableStock && <span>Thiếu lịch sử Ledger — cần đối chiếu chứng từ</span>}
                            {(row.negativePhysicalBucketCount ?? 0) > 0 && <span>{row.negativePhysicalBucketCount} bucket có tồn vật lý âm — cần kiểm tra nguồn gốc</span>}
                            {(row.unclassifiedStockBucketCount ?? 0) > 0 && <span>{row.unclassifiedStockBucketCount} bucket có trạng thái không hỗ trợ — cần kiểm tra dữ liệu gốc</span>}
                          </div>
                        ) : (
                          <span className="ui-muted-text">Chưa có đối chiếu 8 trạng thái</span>
                        )}
                      </td>
                      <td>
                        <div className="reconciliation-movement">
                          <span>Nhập +{numberFormat.format(row.importQuantity)}</span>
                          <span>Xuất -{numberFormat.format(row.exportQuantity)}</span>
                          <span>Chuyển +{numberFormat.format(row.transferInQuantity)} / -{numberFormat.format(row.transferOutQuantity)}</span>
                          <span>Điều chỉnh +{numberFormat.format(row.adjustmentIncreaseQuantity)} / -{numberFormat.format(row.adjustmentDecreaseQuantity)}</span>
                          <span>Đổi trạng thái +{numberFormat.format(row.statusChangeInQuantity ?? 0)} / -{numberFormat.format(row.statusChangeOutQuantity ?? 0)}</span>
                        </div>
                      </td>
                      <td>
                        <button
                          type="button"
                          disabled={demoRuntime || investigating || loading}
                          style={{ whiteSpace: 'nowrap', padding: '5px 8px', minHeight: 30, fontSize: 11.5 }}
                          aria-label={`Xem bằng chứng ${row.productCode} tại ${row.warehouseName}`}
                          onClick={(event) => onOpenInvestigation(row, event.currentTarget)}
                        >
                          {demoRuntime ? 'Chỉ hệ thống thật' : 'Xem bằng chứng'}
                        </button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </UiTableScroll>
      )}

      {(result?.totalPages ?? 0) > 1 && (
        <nav className="reconciliation-pagination" aria-label="Phân trang đối chiếu">
          <button type="button" disabled={page <= 1 || loading} onClick={() => setPage((value) => Math.max(1, value - 1))}>Trang trước</button>
          <span>Trang {result?.pageIndex ?? page} / {result?.totalPages ?? 1}</span>
          <button type="button" disabled={page >= (result?.totalPages ?? 1) || loading} onClick={() => setPage((value) => value + 1)}>Trang sau</button>
        </nav>
      )}
    </UiCard>
  </>
);
