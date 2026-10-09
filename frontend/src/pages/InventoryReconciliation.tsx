import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';
import { demoReconciliationRows, demoReconciliationWarehouses } from '../mocks/inventoryReconciliationDemo';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import './InventoryReconciliation.css';

interface WarehouseOption {
  id: number;
  name: string;
  code?: string;
  isActive?: boolean;
}

interface ReconciliationRow {
  productId: number;
  productCode: string;
  productName: string;
  warehouseId: number;
  warehouseName: string;
  currentQuantity: number;
  expectedQuantity: number;
  difference: number;
  importQuantity: number;
  exportQuantity: number;
  transferInQuantity: number;
  transferOutQuantity: number;
  adjustmentIncreaseQuantity: number;
  adjustmentDecreaseQuantity: number;
  status: string;
}

interface PagedResult<T> {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
}

const pageSize = 20;
const numberFormat = new Intl.NumberFormat('vi-VN');

export default function InventoryReconciliation() {
  const demoRuntime = isBlueprintDemoRuntime();
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);
  const [warehouseError, setWarehouseError] = useState('');
  const [filterError, setFilterError] = useState('');
  const productInputRef = useRef<HTMLInputElement>(null);
  const [warehouseId, setWarehouseId] = useState('');
  const [productId, setProductId] = useState('');
  const [keyword, setKeyword] = useState('');
  const [applied, setApplied] = useState({ warehouseId: '', productId: '', keyword: '' });
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<ReconciliationRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (demoRuntime) {
      setWarehouses(demoReconciliationWarehouses);
      return;
    }
    let active = true;
    apiClient.get('/api/InventoryReconciliation/warehouses')
      .then(response => {
        if (!active) return;
        const list: unknown = response.data;
        if (!Array.isArray(list) || !list.every(item =>
          item && Number.isSafeInteger(item.id) && item.id > 0 &&
          typeof item.name === 'string' && item.name.trim().length > 0 &&
          typeof item.code === 'string' && item.code.trim().length > 0
        ) || new Set(list.map(item => item.id)).size !== list.length) {
          throw new Error('Danh sách kho đối chiếu không hợp lệ.');
        }
        setWarehouses(list as WarehouseOption[]);
        setWarehouseError('');
      })
      .catch(() => {
        if (!active) return;
        setWarehouses([]);
        setWarehouseError('Không thể tải danh sách kho được cấp quyền. Hãy thử lại hoặc liên hệ quản trị.');
      });
    return () => { active = false; };
  }, [demoRuntime]);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        if (demoRuntime) {
          const normalizedKeyword = applied.keyword.trim().toLocaleLowerCase('vi');
          const filtered = demoReconciliationRows.filter(row => {
            const warehouseMatches = !applied.warehouseId || row.warehouseId === Number(applied.warehouseId);
            const productMatches = !applied.productId || row.productId === Number(applied.productId);
            const keywordMatches = !normalizedKeyword
              || row.productCode.toLocaleLowerCase('vi').includes(normalizedKeyword)
              || row.productName.toLocaleLowerCase('vi').includes(normalizedKeyword);
            return warehouseMatches && productMatches && keywordMatches;
          });
          const start = (page - 1) * pageSize;
          if (active) {
            setResult({
              items: filtered.slice(start, start + pageSize),
              totalRecords: filtered.length,
              pageIndex: page,
              pageSize,
              totalPages: Math.ceil(filtered.length / pageSize),
            });
          }
          return;
        }

        const params = new URLSearchParams();
        if (applied.warehouseId) params.set('warehouseId', applied.warehouseId);
        if (applied.productId) params.set('productId', applied.productId);
        if (applied.keyword.trim()) params.set('keyword', applied.keyword.trim());
        params.set('page', String(page));
        params.set('pageSize', String(pageSize));
        const response = await apiClient.get('/api/InventoryReconciliation?' + params.toString());
        if (active) setResult(response.data);
      } catch (cause) {
        if (active) {
          setResult(null);
          const code = (cause as { response?: { status?: number } })?.response?.status;
          setError(code === 403
            ? 'Bạn không có quyền xem sổ cái đối chiếu tồn kho.'
            : code === 404
              ? 'Không tìm thấy kho đối chiếu trong phạm vi được cấp quyền.'
              : 'Không thể tải dữ liệu đối chiếu tồn kho. Hãy kiểm tra kết nối và thử lại.');
        }
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [applied, demoRuntime, page]);

  const currentRows = result?.items ?? [];
  const mismatchCount = currentRows.filter(row => row.status !== 'Match').length;
  const absoluteDifference = currentRows.reduce((sum, row) => sum + Math.abs(row.difference), 0);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const value = productId.trim();
    if (value && (!/^[1-9]\d*$/.test(value) || !Number.isSafeInteger(Number(value)))) {
      setFilterError('ID sản phẩm phải là số nguyên dương hợp lệ và an toàn.');
      productInputRef.current?.focus();
      return;
    }
    setFilterError('');
    setPage(1);
    setApplied({ warehouseId, productId: value, keyword });
  };

  const reset = () => {
    setWarehouseId('');
    setProductId('');
    setKeyword('');
    setFilterError('');
    setPage(1);
    setApplied({ warehouseId: '', productId: '', keyword: '' });
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Inventory Control"
        title="Đối chiếu tồn kho & ledger"
        description="So sánh operational balance với immutable ledger để phát hiện chênh lệch. Work center này chỉ đọc; mọi sửa sai phải đi qua transaction có kiểm soát."
      />
      <p role="note" className="ui-muted-text">
        Phạm vi hiện tại chỉ đối chiếu trạng thái AVAILABLE theo cặp kho / sản phẩm.
        Chưa bao phủ toàn bộ Owner, Handling Unit, Reserved, Allocated hay quy trình khôi phục số dư có phê duyệt.
        Kết quả này chỉ để phát hiện sai lệch, không tự động sửa Ledger hoặc Balance.
      </p>

      <UiMetricGrid>
        <UiMetric value={numberFormat.format(result?.totalRecords ?? 0)} label="Cặp kho / sản phẩm" />
        <UiMetric value={numberFormat.format(mismatchCount)} label="Mismatch trang hiện tại" />
        <UiMetric value={numberFormat.format(absoluteDifference)} label="Độ lệch tuyệt đối" />
      </UiMetricGrid>

      <form noValidate onSubmit={submit}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select value={warehouseId} onChange={event => setWarehouseId(event.target.value)} disabled={!!warehouseError}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID sản phẩm">
            <input ref={productInputRef} type="text" inputMode="numeric" value={productId} onChange={event => setProductId(event.target.value)} placeholder="VD: 1001" aria-invalid={!!filterError} />
          </UiToolbarField>
          <UiToolbarField label="Mã / tên sản phẩm">
            <input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Tìm theo mã hoặc tên" />
          </UiToolbarField>
          <div className="reconciliation-filter-actions">
            <button type="submit">Đối chiếu</button>
            <button type="button" onClick={reset}>Xóa lọc</button>
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
            <table className="reconciliation-table">
              <thead>
                <tr>
                  <th>Kho</th>
                  <th>Sản phẩm</th>
                  <th>Tồn hiện tại</th>
                  <th>Ledger kỳ vọng</th>
                  <th>Chênh lệch</th>
                  <th>Trạng thái</th>
                  <th>Movement breakdown</th>
                </tr>
              </thead>
              <tbody>
                {currentRows.length === 0 ? (
                  <tr><td colSpan={7} className="reconciliation-empty">Không có dữ liệu phù hợp. Hãy thay đổi bộ lọc và thử lại.</td></tr>
                ) : currentRows.map(row => {
                  const matches = row.status === 'Match';
                  return (
                    <tr key={row.warehouseId + '-' + row.productId} className={matches ? undefined : 'reconciliation-mismatch'}>
                      <td>{row.warehouseName}</td>
                      <td>
                        <strong>{row.productCode}</strong>
                        <span className="reconciliation-product-name">{row.productName}</span>
                      </td>
                      <td>{numberFormat.format(row.currentQuantity)}</td>
                      <td>{numberFormat.format(row.expectedQuantity)}</td>
                      <td className={row.difference === 0 ? 'reconciliation-difference-zero' : 'reconciliation-difference-alert'}>
                        {numberFormat.format(row.difference)}
                      </td>
                      <td><UiBadge tone={matches ? 'success' : 'danger'}>{matches ? 'Khớp' : 'Lệch'}</UiBadge></td>
                      <td>
                        <div className="reconciliation-movement">
                          <span>Nhập +{numberFormat.format(row.importQuantity)}</span>
                          <span>Xuất -{numberFormat.format(row.exportQuantity)}</span>
                          <span>Chuyển +{numberFormat.format(row.transferInQuantity)} / -{numberFormat.format(row.transferOutQuantity)}</span>
                          <span>Điều chỉnh +{numberFormat.format(row.adjustmentIncreaseQuantity)} / -{numberFormat.format(row.adjustmentDecreaseQuantity)}</span>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </UiTableScroll>
        )}

        {(result?.totalPages ?? 0) > 1 && (
          <nav className="reconciliation-pagination" aria-label="Phân trang đối chiếu">
            <button type="button" disabled={page <= 1 || loading} onClick={() => setPage(value => Math.max(1, value - 1))}>Trang trước</button>
            <span>Trang {result?.pageIndex ?? page} / {result?.totalPages ?? 1}</span>
            <button type="button" disabled={page >= (result?.totalPages ?? 1) || loading} onClick={() => setPage(value => value + 1)}>Trang sau</button>
          </nav>
        )}
      </UiCard>
    </UiPage>
  );
}
