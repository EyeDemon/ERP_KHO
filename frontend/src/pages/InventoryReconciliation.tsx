import { FormEvent, useEffect, useMemo, useState } from 'react';
import apiClient from '../services/apiClient';
import './InventoryReconciliation.css';

interface WarehouseOption {
  id: number;
  name: string;
  isActive: boolean;
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

export default function InventoryReconciliation() {
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);
  const [warehouseId, setWarehouseId] = useState('');
  const [productId, setProductId] = useState('');
  const [keyword, setKeyword] = useState('');
  const [applied, setApplied] = useState({ warehouseId: '', productId: '', keyword: '' });
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<ReconciliationRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    apiClient.get('/api/warehouses')
      .then(response => setWarehouses((response.data as WarehouseOption[]).filter(item => item.isActive)))
      .catch(() => setWarehouses([]));
  }, []);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const params = new URLSearchParams();
        if (applied.warehouseId) params.set('warehouseId', applied.warehouseId);
        if (applied.productId) params.set('productId', applied.productId);
        if (applied.keyword.trim()) params.set('keyword', applied.keyword.trim());
        params.set('page', String(page));
        params.set('pageSize', String(pageSize));
        const response = await apiClient.get('/api/InventoryReconciliation?' + params.toString());
        if (active) setResult(response.data);
      } catch {
        if (active) {
          setResult(null);
          setError('Không thể tải dữ liệu đối chiếu tồn kho. Vui lòng thử lại.');
        }
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [applied, page]);

  const currentRows = result?.items ?? [];
  const mismatchCount = useMemo(() => currentRows.filter(row => row.status !== 'Match').length, [currentRows]);
  const absoluteDifference = useMemo(() => currentRows.reduce((sum, row) => sum + Math.abs(row.difference), 0), [currentRows]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setPage(1);
    setApplied({ warehouseId, productId, keyword });
  };

  const reset = () => {
    setWarehouseId('');
    setProductId('');
    setKeyword('');
    setPage(1);
    setApplied({ warehouseId: '', productId: '', keyword: '' });
  };

  return (
    <section className="reconciliation-page">
      <header className="reconciliation-header">
        <div>
          <span className="reconciliation-kicker">Inventory Integrity</span>
          <h1>Đối chiếu tồn kho & ledger</h1>
          <p>So sánh số dư tồn hiện tại với tổng movement bất biến. Màn này chỉ đọc, không tự sửa số liệu.</p>
        </div>
        <div className="reconciliation-summary" aria-label="Tóm tắt đối chiếu">
          <article><strong>{result?.totalRecords ?? 0}</strong><span>Cặp kho / sản phẩm</span></article>
          <article><strong>{mismatchCount}</strong><span>Mismatch trang hiện tại</span></article>
          <article><strong>{absoluteDifference}</strong><span>Độ lệch tuyệt đối</span></article>
        </div>
      </header>

      <form className="reconciliation-filter" onSubmit={submit}>
        <label>Kho
          <select value={warehouseId} onChange={event => setWarehouseId(event.target.value)}>
            <option value="">Tất cả kho được phép</option>
            {warehouses.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>ID sản phẩm
          <input type="number" min="1" value={productId} onChange={event => setProductId(event.target.value)} placeholder="VD: 1001" />
        </label>
        <label>Mã / tên sản phẩm
          <input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Tìm theo mã hoặc tên" />
        </label>
        <div className="reconciliation-filter-actions">
          <button type="submit">Đối chiếu</button>
          <button type="button" className="secondary" onClick={reset}>Xóa lọc</button>
        </div>
      </form>

      {error && <div role="alert" className="reconciliation-error">{error}</div>}
      {loading ? <p role="status">Đang đối chiếu tồn kho...</p> : (
        <div className="reconciliation-table-wrap">
          <table className="reconciliation-table">
            <thead>
              <tr>
                <th>Kho</th><th>Sản phẩm</th><th>Current</th><th>Ledger expected</th><th>Difference</th><th>Trạng thái</th><th>Movement breakdown</th>
              </tr>
            </thead>
            <tbody>
              {currentRows.length === 0 ? (
                <tr><td colSpan={7} className="empty">Không có dữ liệu phù hợp.</td></tr>
              ) : currentRows.map(row => (
                <tr key={row.warehouseId + '-' + row.productId} className={row.status === 'Match' ? '' : 'mismatch'}>
                  <td>{row.warehouseName}</td>
                  <td><strong>{row.productCode}</strong><span>{row.productName}</span></td>
                  <td>{row.currentQuantity}</td>
                  <td>{row.expectedQuantity}</td>
                  <td className={row.difference === 0 ? 'difference-zero' : 'difference-alert'}>{row.difference}</td>
                  <td><span className={row.status === 'Match' ? 'status-match' : 'status-mismatch'}>{row.status === 'Match' ? 'Khớp' : 'Lệch'}</span></td>
                  <td className="movement">
                    <span>Nhập +{row.importQuantity}</span>
                    <span>Xuất -{row.exportQuantity}</span>
                    <span>Chuyển +{row.transferInQuantity} / -{row.transferOutQuantity}</span>
                    <span>Điều chỉnh +{row.adjustmentIncreaseQuantity} / -{row.adjustmentDecreaseQuantity}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {(result?.totalPages ?? 0) > 1 && (
        <nav className="reconciliation-pagination" aria-label="Phân trang đối chiếu">
          <button type="button" disabled={page <= 1 || loading} onClick={() => setPage(value => Math.max(1, value - 1))}>Trang trước</button>
          <span>Trang {result?.pageIndex ?? page} / {result?.totalPages ?? 1}</span>
          <button type="button" disabled={page >= (result?.totalPages ?? 1) || loading} onClick={() => setPage(value => value + 1)}>Trang sau</button>
        </nav>
      )}
    </section>
  );
}
