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

interface InvestigationBucket {
  inventoryStockId: number;
  locationId?: number | null; locationCode?: string | null;
  lotId?: number | null; lotNumber?: string | null;
  serialId?: number | null; serialNumber?: string | null;
  quantity: number; reservedQuantity: number;
}
interface InvestigationEvent {
  transactionId: number; transactionType: string;
  locationId?: number | null; locationCode?: string | null;
  lotId?: number | null; lotNumber?: string | null;
  serialId?: number | null; serialNumber?: string | null;
  quantity: number; signedQuantity: number;
  referenceType?: string | null; referenceId?: number | null;
  transactionDate: string;
}
interface Investigation {
  warehouseId:number; warehouseName:string; productId:number;
  productCode:string; productName:string; eventAnchorId:number;
  eventCount:number; bucketCount:number;
  eventBeforeId?:number|null; nextEventBeforeId?:number|null;
  ledgerHasEventsAfterAnchor?:boolean;
  eventsTruncated:boolean; bucketsTruncated:boolean;
  currentQuantity:number; expectedQuantity:number; difference:number;
  isReadOnly:boolean; buckets:InvestigationBucket[]; events:InvestigationEvent[];
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
  const [investigation, setInvestigation] = useState<Investigation | null>(null);
  const [investigationError, setInvestigationError] = useState('');
  const [investigating, setInvestigating] = useState(false);
  // Each cursor is the strict upper bound of one Ledger page. The initial
  // page is null; previous pages can be safely reloaded with the same anchor.
  const [eventPageCursors, setEventPageCursors] = useState<(number | null)[]>([null]);
  const [eventPageIndex, setEventPageIndex] = useState(0);
  const investigationGeneration = useRef(0);
  const investigationTrigger = useRef<HTMLButtonElement | null>(null);

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

  // Invalidate in-flight evidence reads after pagination/filter changes or
  // unmount. Never display stale evidence belonging to another scope.
  useEffect(() => {
    investigationGeneration.current += 1;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(false);
    setEventPageCursors([null]);
    setEventPageIndex(0);
    return () => { investigationGeneration.current += 1; };
  }, [applied, page]);

  const loadInvestigation = async (
    warehouse: number, product: number, anchor?: number, before?: number,
    targetPage = 0, cursors: (number | null)[] = [null],
  ) => {
    if (demoRuntime) return;
    const generation = ++investigationGeneration.current;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(true);
    try {
      const params = new URLSearchParams({
        warehouseId: String(warehouse),
        productId: String(product),
        limit: '50',
      });
      if (anchor !== undefined) params.set('eventAnchorId', String(anchor));
      if (before !== undefined) params.set('eventBeforeId', String(before));
      const response = await apiClient.get('/api/InventoryReconciliation/investigation?' + params.toString());
      if (generation !== investigationGeneration.current) return;
      const evidence = response.data as Investigation;
      if (!evidence || evidence.warehouseId !== warehouse ||
          evidence.productId !== product || evidence.isReadOnly !== true ||
          !Number.isSafeInteger(evidence.eventAnchorId) || evidence.eventAnchorId < 0 ||
          (anchor !== undefined && evidence.eventAnchorId !== anchor) ||
          (before !== undefined && evidence.eventBeforeId !== before) ||
          !Array.isArray(evidence.buckets) || !Array.isArray(evidence.events) ||
          evidence.events.some((e, i) =>
            !Number.isSafeInteger(e.transactionId) || e.transactionId <= 0 ||
            e.transactionId > evidence.eventAnchorId ||
            (before !== undefined && e.transactionId >= before) ||
            (i > 0 && evidence.events[i - 1].transactionId <= e.transactionId)) ||
          (evidence.nextEventBeforeId != null &&
            (!Number.isSafeInteger(evidence.nextEventBeforeId) ||
             evidence.nextEventBeforeId <= 0 || !evidence.eventsTruncated ||
             evidence.events[evidence.events.length - 1]?.transactionId !== evidence.nextEventBeforeId))) {
        throw new Error('Dữ liệu điều tra không hợp lệ.');
      }
      setInvestigation(evidence);
      setEventPageCursors(cursors);
      setEventPageIndex(targetPage);
    } catch (cause) {
      if (generation !== investigationGeneration.current) return;
      const status = (cause as { response?: { status?: number } })?.response?.status;
      setInvestigationError(status === 403
        ? 'Bạn không có quyền xem bằng chứng đối chiếu.'
        : status === 404
          ? 'Không tìm thấy bằng chứng trong kho được cấp quyền.'
          : 'Không thể tải bằng chứng điều tra. Hãy thử lại.');
    } finally {
      if (generation === investigationGeneration.current) setInvestigating(false);
    }
  };

  const openInvestigation = (row: ReconciliationRow) => {
    void loadInvestigation(row.warehouseId, row.productId);
  };

  const changeEventPage = (direction: 'older' | 'newer') => {
    if (!investigation || investigating) return;
    if (direction === 'older') {
      const next = investigation.nextEventBeforeId;
      if (!investigation.eventsTruncated || !Number.isSafeInteger(next) || !next || next <= 0) return;
      const cursors = [...eventPageCursors.slice(0, eventPageIndex + 1), next];
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, next, eventPageIndex + 1, cursors);
    } else if (eventPageIndex > 0) {
      const index = eventPageIndex - 1;
      const before = eventPageCursors[index] ?? undefined;
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, before, index, eventPageCursors);
    }
  };

  const closeInvestigation = () => {
    investigationGeneration.current += 1;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(false);
    setEventPageCursors([null]);
    setEventPageIndex(0);
    investigationTrigger.current?.focus();
  };

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
                  <th>Thành phần biến động</th>
                  <th>Bằng chứng</th>
                </tr>
              </thead>
              <tbody>
                {currentRows.length === 0 ? (
                  <tr><td colSpan={8} className="reconciliation-empty">Không có dữ liệu phù hợp. Hãy thay đổi bộ lọc và thử lại.</td></tr>
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
                      <td>
                        <button type="button" disabled={demoRuntime || investigating || loading}
                          aria-label={`Xem bằng chứng ${row.productCode} tại ${row.warehouseName}`}
                          onClick={event => {
                            investigationTrigger.current = event.currentTarget;
                            void openInvestigation(row);
                          }}>
                          {demoRuntime ? 'Chỉ hệ thống thật' : 'Xem bằng chứng'}
                        </button>
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

      {(investigating || investigationError || investigation) && (
        <UiCard title="Hồ sơ điều tra chênh lệch (chỉ đọc)">
          {investigating && <p role="status">Đang tải bằng chứng theo kho và sản phẩm...</p>}
          {investigationError && <p role="alert">{investigationError}</p>}
          {investigation && (
            <>
              <p><strong>{investigation.productCode}</strong> — {investigation.productName} • {investigation.warehouseName}</p>
              <p className="ui-muted-text">
                Bằng chứng Ledger được cố định tại ID #{investigation.eventAnchorId}.
                Bucket tồn và số dư hiện tại là dữ liệu thời điểm truy vấn, không phải ảnh chụp quá khứ.
                Không sử dụng số chênh lệch này để tự sửa tồn kho hay tạo giao dịch đảo.
              </p>
              {investigation.ledgerHasEventsAfterAnchor && (
                <p role="status" className="ui-muted-text">
                  Đã có giao dịch mới hơn mốc #{investigation.eventAnchorId}. Các trang Ledger và tổng kỳ vọng vẫn chỉ tính đến mốc cũ.
                </p>
              )}
              <button type="button" disabled={investigating}
                onClick={() => void loadInvestigation(investigation.warehouseId, investigation.productId)}>
                Làm mới mốc Ledger
              </button>
              <UiMetricGrid>
                <UiMetric label="Số dư hiện tại" value={numberFormat.format(investigation.currentQuantity)} />
                <UiMetric label="Ledger đến mốc" value={numberFormat.format(investigation.expectedQuantity)} />
                <UiMetric label="Chênh lệch tham khảo" value={numberFormat.format(investigation.difference)} />
              </UiMetricGrid>
              {(investigation.eventsTruncated || investigation.bucketsTruncated) && (
                <p role="status" className="ui-muted-text">
                  Bằng chứng đã giới hạn ({investigation.events.length}/{investigation.eventCount} sự kiện,
                  {investigation.buckets.length}/{investigation.bucketCount} bucket). Không coi danh sách này là toàn bộ hồ sơ.
                </p>
              )}
              <h3>Bucket tồn AVAILABLE hiện tại ({investigation.bucketCount})</h3>
              <UiTableScroll><table aria-label="Bucket tồn phục vụ điều tra chênh lệch">
                <thead><tr><th>ID bucket</th><th>Vị trí</th><th>Lô</th><th>Sê-ri</th><th>Tồn</th><th>Đã giữ</th></tr></thead>
                <tbody>{investigation.buckets.length === 0?
                  <tr><td colSpan={6}>Không có bucket AVAILABLE.</td></tr>:
                  investigation.buckets.map(b=><tr key={b.inventoryStockId}>
                    <td>#{b.inventoryStockId}</td><td>{b.locationCode ?? '—'}</td>
                    <td>{b.lotNumber ?? '—'}</td><td>{b.serialNumber ?? '—'}</td>
                    <td>{numberFormat.format(b.quantity)}</td><td>{numberFormat.format(b.reservedQuantity)}</td>
                  </tr>)}
                </tbody>
              </table></UiTableScroll>
              <h3>Sự kiện Ledger AVAILABLE ({investigation.eventCount})</h3>
              <UiTableScroll><table aria-label="Sự kiện Ledger phục vụ điều tra chênh lệch">
                <thead><tr><th>ID giao dịch</th><th>Loại</th><th>Vị trí</th><th>Lô/sê-ri</th><th>Số lượng có dấu</th><th>Chứng từ</th></tr></thead>
                <tbody>{investigation.events.length === 0?
                  <tr><td colSpan={6}>Không có sự kiện Ledger tại mốc này.</td></tr>:
                  investigation.events.map(e=><tr key={e.transactionId}>
                    <td>#{e.transactionId}</td><td>{e.transactionType}</td>
                    <td>{e.locationCode ?? '—'}</td>
                    <td>{e.lotNumber ?? '—'} / {e.serialNumber ?? '—'}</td>
                    <td>{numberFormat.format(e.signedQuantity)}</td>
                    <td>{e.referenceType ?? '—'}{e.referenceId != null ? ` #${e.referenceId}` : ''}</td>
                  </tr>)}
                </tbody>
              </table></UiTableScroll>
              <nav className="reconciliation-pagination" aria-label="Phân trang sự kiện Ledger">
                <button type="button" disabled={investigating || eventPageIndex === 0}
                  onClick={() => changeEventPage('newer')}>Sự kiện mới hơn</button>
                <span>Trang Ledger {eventPageIndex + 1} — tối đa 50 sự kiện/trang, tổng {investigation.eventCount}</span>
                <button type="button"
                  disabled={investigating || !investigation.eventsTruncated || !investigation.nextEventBeforeId}
                  onClick={() => changeEventPage('older')}>Sự kiện cũ hơn</button>
              </nav>
            </>
          )}
          <button type="button" onClick={closeInvestigation}>Đóng hồ sơ</button>
        </UiCard>
      )}
    </UiPage>
  );
}
