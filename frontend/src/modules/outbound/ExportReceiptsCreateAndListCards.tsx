import { useEffect, useRef, useState } from 'react';
import apiClient from '../../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import { roundQty4, sanitizeScannerCode } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { ExportReceipt, ExportStatusQuickFilter, Partner, Product, ReceiptDetailForm, Warehouse } from './exportReceiptsTypes';
import { safeRequestError, statusLabel, statusTone } from './exportReceiptsTypes';

interface ExportReceiptsCreateCardProps {
  showCreateForm: boolean;
  setShowCreateForm: React.Dispatch<React.SetStateAction<boolean>>;
  warehouseId: number | '';
  setWarehouseId: (val: number | '') => void;
  warehouses: Warehouse[];
  canReadPartners: boolean;
  customers: Partner[];
  products: Product[];
  salesOrders: Array<{ id: number; orderCode?: string; code?: string; customerName?: string }>;
  setError: (msg: string) => void;
  setSuccessMsg: (msg: string) => void;
  fetchReceipts: () => Promise<void>;
}

export function ExportReceiptsCreateCard({
  showCreateForm, setShowCreateForm, warehouseId, setWarehouseId, warehouses, canReadPartners,
  customers, products, salesOrders, setError, setSuccessMsg, fetchReceipts,
}: ExportReceiptsCreateCardProps) {
  const [code, setCode] = useState('');
  const [note, setNote] = useState('');
  const [customerId, setCustomerId] = useState<number | ''>('');
  const [salesOrderId, setSalesOrderId] = useState<string>('');
  const [marketplaceOrderId, setMarketplaceOrderId] = useState<string>('');
  const [carrierCode, setCarrierCode] = useState<string>('');
  const [trackingNumber, setTrackingNumber] = useState<string>('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);
  const [stockCache, setStockCache] = useState<Record<string, number>>({});
  const [stockLoading, setStockLoading] = useState<Record<string, boolean>>({});
  const [stockError, setStockError] = useState<Record<string, boolean>>({});
  const stockInFlightRef = useRef<Set<string>>(new Set());
  const createInFlight = useRef(false);
  const [creating, setCreating] = useState(false);

  const fetchStockForKey = async (wId: number, pId: number): Promise<number | null> => {
    const key = `${wId}_${pId}`;
    if (stockInFlightRef.current.has(key)) return null;
    stockInFlightRef.current.add(key);
    setStockLoading(prev => ({ ...prev, [key]: true }));
    setStockError(prev => ({ ...prev, [key]: false }));
    try {
      const res = await apiClient.get('/api/v1/inventory-stocks/current', { params: { warehouseId: wId, productId: pId } });
      const stock = Array.isArray(res.data)
        ? roundQty4(res.data.reduce((total: number, row: { availableQuantity?: number }) => total + Number(row.availableQuantity || 0), 0))
        : 0;
      setStockCache(prev => ({ ...prev, [key]: stock }));
      setStockError(prev => ({ ...prev, [key]: false }));
      return stock;
    } catch {
      setStockError(prev => ({ ...prev, [key]: true }));
      return null;
    } finally {
      stockInFlightRef.current.delete(key);
      setStockLoading(prev => ({ ...prev, [key]: false }));
    }
  };

  useEffect(() => {
    details.forEach(d => {
      if (warehouseId !== '' && d.productId !== '') {
        const key = `${warehouseId}_${d.productId}`;
        if (stockCache[key] === undefined && !stockInFlightRef.current.has(key)) {
          void fetchStockForKey(Number(warehouseId), Number(d.productId));
        }
      }
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [warehouseId, details]);

  const handleAddDetail = () => setDetails([...details, { productId: '', quantity: '', unitPrice: '', note: '' }]);
  const handleRemoveDetail = (index: number) => setDetails(details.filter((_, i) => i !== index));
  const handleDetailChange = (index: number, field: keyof ReceiptDetailForm, value: any) => {
    const newDetails = [...details];
    newDetails[index] = { ...newDetails[index], [field]: value };
    setDetails(newDetails);
  };

  const getStockDisplay = (wId: number | '', pId: number | '') => {
    if (wId === '' || pId === '') return '-';
    const key = `${wId}_${pId}`;
    const stock = stockCache[key];
    if (stock !== undefined) return stock;
    if (stockLoading[key]) return 'Đang tải...';
    if (stockError[key]) return 'Lỗi tải tồn';
    return 'Đang tải...';
  };

  const isFormInvalid = () => {
    if (!sanitizeScannerCode(code) || warehouseId === '' || details.length === 0) return true;
    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '' || d.quantity === '' || Number(d.quantity) <= 0 || d.unitPrice === '' || Number(d.unitPrice) < 0) return true;
      const key = `${warehouseId}_${d.productId}`;
      const stock = stockCache[key];
      if (stockLoading[key] || (stock === undefined && !stockError[key]) || (stock !== undefined && roundQty4(Number(d.quantity)) > stock)) return true;
    }
    return false;
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (createInFlight.current) return;
    setError(''); setSuccessMsg('');
    const normalizedCode = sanitizeScannerCode(code);
    if (!normalizedCode) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');

    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.quantity === '' || !Number.isFinite(Number(d.quantity)) || Number(d.quantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng xuất phải > 0`);
      if (d.unitPrice === '' || !Number.isFinite(Number(d.unitPrice)) || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);
      const key = `${warehouseId}_${d.productId}`;
      let stock: number | undefined | null = stockCache[key];
      if (stock === undefined) {
        if (stockLoading[key]) return setError(`Dòng ${i + 1}: Đang tải tồn kho...`);
        stock = await fetchStockForKey(Number(warehouseId), Number(d.productId));
        if (stock === null) return setError(`Dòng ${i + 1}: Lỗi tải tồn kho!`);
      }
      const normalizedQty = roundQty4(Number(d.quantity));
      if (normalizedQty > stock) return setError(`Dòng ${i + 1}: Số lượng xuất (${normalizedQty}) vượt quá tồn khả dụng (${stock})`);
    }

    createInFlight.current = true; setCreating(true);
    try {
      const payload = {
        code: normalizedCode, warehouseId: Number(warehouseId), customerId: customerId === '' ? null : Number(customerId),
        ...(salesOrderId !== '' ? { salesOrderId: Number(salesOrderId) } : {}), note,
        ...(marketplaceOrderId.trim() ? { marketplaceOrderId: marketplaceOrderId.trim() } : {}),
        ...(carrierCode.trim() ? { carrierCode: carrierCode.trim() } : {}),
        ...(trackingNumber.trim() ? { trackingNumber: trackingNumber.trim() } : {}),
        details: details.map(d => ({ productId: Number(d.productId), quantity: roundQty4(Number(d.quantity)), unitPrice: Number(d.unitPrice), note: d.note })),
      };
      const action = `export-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/v1/export-receipts', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Tạo phiếu xuất (nháp) thành công!');
      setCode(''); setWarehouseId(''); setCustomerId(''); setSalesOrderId(''); setNote('');
      setMarketplaceOrderId(''); setCarrierCode(''); setTrackingNumber(''); setDetails([]);
      void fetchReceipts();
    } catch (err: any) {
      setError(safeRequestError(err, 'Không thể tạo phiếu xuất.'));
    } finally {
      createInFlight.current = false; setCreating(false);
    }
  };

  return (
    <div className="no-print">
      <UiCard title="Tạo Phiếu Xuất Kho">
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: showCreateForm ? 12 : 0 }}>
          <button type="button" onClick={() => setShowCreateForm(prev => !prev)}>{showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo phiếu xuất kho mới'}</button>
        </div>
        <div style={showCreateForm ? undefined : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}>
          <form onSubmit={handleCreate} className="export-form">
            <div className="export-form-grid">
              <label className="ui-stack"><span>Mã phiếu</span><input aria-label="Mã phiếu" value={code} onChange={e => setCode(e.target.value)} required /></label>
              <label className="ui-stack">
                <span>Kho</span>
                <select aria-label="Kho" value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required>
                  <option value="">-- Chọn kho --</option>
                  {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
                </select>
              </label>
              <label className="ui-stack"><span>Ghi chú phiếu</span><input aria-label="Ghi chú phiếu" value={note} onChange={e => setNote(e.target.value)} /></label>
              {canReadPartners && (
                <label className="ui-stack">
                  <span>Khách hàng</span>
                  <select aria-label="Khách hàng" value={customerId} onChange={e => setCustomerId(e.target.value ? Number(e.target.value) : '')}>
                    <option value="">-- Không chọn --</option>
                    {customers.filter(x => x.isActive).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}
                  </select>
                </label>
              )}
            </div>

            <h3>Chi tiết phiếu</h3>
            <div className="ui-stack">
              {details.map((d, i) => {
                const stockDisplay = getStockDisplay(warehouseId, d.productId);
                const isExceed = typeof stockDisplay === 'number' && Number(d.quantity) > stockDisplay;
                return (
                  <div key={i} className="export-line">
                    <label className="ui-stack">
                      <span>Sản phẩm</span>
                      <select aria-label={'Sản phẩm dòng ' + (i + 1)} value={d.productId} onChange={e => handleDetailChange(i, 'productId', e.target.value ? Number(e.target.value) : '')} required>
                        <option value="">-- Chọn sản phẩm --</option>
                        {products.map(p => <option key={p.id} value={p.id}>{p.code} - {p.name}</option>)}
                      </select>
                    </label>
                    <label className="ui-stack">
                      <span>Số lượng</span>
                      <input className={isExceed ? 'export-stock-input danger' : 'export-stock-input'} aria-label={'Số lượng dòng ' + (i + 1)} type="number" placeholder="Số lượng" value={d.quantity} onChange={e => handleDetailChange(i, 'quantity', e.target.value ? Number(e.target.value) : '')} required min="0.01" step="0.01" />
                    </label>
                    <div className={isExceed ? 'export-stock danger' : 'export-stock'} aria-live="polite">Tồn: {stockDisplay}</div>
                    <label className="ui-stack">
                      <span>Đơn giá</span>
                      <input aria-label={'Đơn giá dòng ' + (i + 1)} type="number" placeholder="Đơn giá" value={d.unitPrice} onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')} required min="0" step="0.01" />
                    </label>
                    <label className="ui-stack">
                      <span>Ghi chú</span>
                      <input aria-label={'Ghi chú dòng ' + (i + 1)} placeholder="Ghi chú" value={d.note} onChange={e => handleDetailChange(i, 'note', e.target.value)} />
                    </label>
                    <div className="export-actions">
                      <button type="button" aria-label={'Xóa dòng ' + (i + 1)} onClick={() => handleRemoveDetail(i)}>Xóa</button>
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="export-form-grid" style={{ marginTop: 10 }}>
              <label className="ui-stack">
                <span>Đơn bán hàng (SO) / Yêu cầu xuất liên kết</span>
                <select aria-label="Liên kết Đơn bán hàng (SO)" value={salesOrderId} onChange={e => setSalesOrderId(e.target.value)}>
                  <option value="">-- Không liên kết SO --</option>
                  {salesOrders.map(so => <option key={so.id} value={String(so.id)}>{so.orderCode || so.code}{so.customerName ? ` — ${so.customerName}` : ''}</option>)}
                </select>
              </label>
              <label className="ui-stack"><span>Mã đơn sàn TMĐT</span><input aria-label="Mã đơn sàn TMĐT" placeholder="VD: MP-SHOPEE-2026" value={marketplaceOrderId} onChange={e => setMarketplaceOrderId(e.target.value)} /></label>
              <label className="ui-stack"><span>Đơn vị vận chuyển</span><input aria-label="Đơn vị vận chuyển" placeholder="VD: GHN / GHTK / VTP" value={carrierCode} onChange={e => setCarrierCode(e.target.value)} /></label>
              <label className="ui-stack"><span>Mã vận đơn (AWB)</span><input aria-label="Mã vận đơn (AWB)" placeholder="VD: AWB-88990011" value={trackingNumber} onChange={e => setTrackingNumber(e.target.value)} /></label>
            </div>

            <div className="export-section-actions" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 10 }}>
              <button type="button" onClick={handleAddDetail}>+ Thêm dòng</button>
              <button type="submit" disabled={creating || isFormInvalid()}>{creating ? 'Đang lưu...' : 'Tạo Phiếu Xuất'}</button>
              {!creating && isFormInvalid() && (
                <span data-testid="export-create-disabled-hint" style={{ fontSize: 12.5, color: '#b45309', background: '#fffbeb', border: '1px solid #fde68a', borderRadius: 6, padding: '6px 10px' }}>
                  Hướng dẫn: Vui lòng nhập Mã phiếu, chọn Kho và bấm &quot;+ Thêm dòng&quot; (chọn sản phẩm, nhập số lượng &gt; 0 không vượt tồn khả dụng) để kích hoạt nút Tạo Phiếu Xuất.
                </span>
              )}
            </div>
          </form>
        </div>
      </UiCard>
    </div>
  );
}

interface ExportReceiptsListCardProps {
  receipts: ExportReceipt[]; filteredReceipts: ExportReceipt[]; pagedReceipts: ExportReceipt[];
  receiptPage: number; totalReceiptPages: number;
  setReceiptPage: React.Dispatch<React.SetStateAction<number>>;
  setStatusQuickFilter: (val: ExportStatusQuickFilter) => void;
  userId: number | null; canApproveAndReserve: boolean; canDispatch: boolean; canCancel: boolean;
  actionInFlight: string | null; printLoadingId: number | null;
  handleViewDetails: (id: number) => Promise<void>;
  openPrintPreview: (id: number, trigger: HTMLButtonElement) => Promise<void>;
  handleWorkflowAction: (id: number, action: 'approve-and-reserve' | 'approve-and-dispatch' | 'dispatch') => Promise<void>;
  handleCancel: (id: number) => Promise<void>;
}

export function ExportReceiptsListCard({
  receipts, filteredReceipts, pagedReceipts, receiptPage, totalReceiptPages, setReceiptPage,
  setStatusQuickFilter, userId, canApproveAndReserve, canDispatch, canCancel, actionInFlight,
  printLoadingId, handleViewDetails, openPrintPreview, handleWorkflowAction, handleCancel,
}: ExportReceiptsListCardProps) {
  return (
    <div className="no-print">
      <UiCard title="Danh Sách Phiếu Xuất">
        <UiTableScroll>
          <table aria-label="Danh sách phiếu xuất" style={{ minWidth: 980 }}>
            <thead>
              <tr>
                <th>Mã phiếu</th><th>Kho</th><th>Khách hàng</th><th>Vận đơn &amp; Đơn sàn TMĐT</th>
                <th style={{ whiteSpace: 'nowrap' }}>Trạng thái</th><th>Người tạo</th><th style={{ whiteSpace: 'nowrap' }}>Hành động</th>
              </tr>
            </thead>
            <tbody>
              {pagedReceipts.map(r => (
                <tr key={r.id}>
                  <td style={{ whiteSpace: 'nowrap' }}><strong>{r.code}</strong></td>
                  <td>{r.warehouseName}</td>
                  <td>{r.customerCode ? r.customerCode + ' - ' + r.customerName : '—'}</td>
                  <td>{r.marketplaceOrderId || r.trackingNumber ? `${r.marketplaceOrderId ?? '—'} • ${r.carrierCode ?? '—'} • ${r.trackingNumber ?? '—'}` : '—'}</td>
                  <td style={{ whiteSpace: 'nowrap' }}><UiBadge tone={statusTone(r.status)}>{statusLabel(r.status)}</UiBadge></td>
                  <td>{r.createdByName}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <div className="export-row-actions" style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap', alignItems: 'center' }}>
                      <div style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap', alignItems: 'center' }}>
                        <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => void handleViewDetails(r.id)}>Chi tiết</button>
                        <button type="button" style={{ whiteSpace: 'nowrap' }} disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)}>
                          {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
                        </button>
                      </div>
                      {r.status === 'Draft' && (
                        <>
                          {canApproveAndReserve && r.createdBy !== userId && (
                            <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => void handleWorkflowAction(r.id, 'approve-and-reserve')}>Duyệt và giữ hàng</button>
                          )}
                          {r.allowPerReceiptDispatchMode && canApproveAndReserve && canDispatch && r.createdBy !== userId && (
                            <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => void handleWorkflowAction(r.id, 'approve-and-dispatch')}>Duyệt và xuất ngay (tương thích)</button>
                          )}
                          {canCancel && <button type="button" disabled={actionInFlight !== null} onClick={() => void handleCancel(r.id)}>Hủy</button>}
                        </>
                      )}
                      {r.status === 'Approved' && (
                        <>
                          {canDispatch && r.approvedBy !== userId && (
                            <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => void handleWorkflowAction(r.id, 'dispatch')}>Xác nhận xuất kho</button>
                          )}
                          {canCancel && (
                            <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => void handleCancel(r.id)}>Hủy và giải phóng hàng</button>
                          )}
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {receipts.length === 0 && <tr><td colSpan={7} className="ui-empty-cell">Chưa có phiếu xuất nào</td></tr>}
              {receipts.length > 0 && filteredReceipts.length === 0 && (
                <tr>
                  <td colSpan={7} className="ui-empty-cell">
                    Không có phiếu xuất nào khớp với bộ lọc nhanh hiện tại.{' '}
                    <button type="button" onClick={() => { setStatusQuickFilter('ALL'); setReceiptPage(1); }}>Xem tất cả ({receipts.length})</button>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </UiTableScroll>
        <div className="ui-pagination" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, marginTop: 10 }}>
          <span style={{ fontSize: 12.5, color: '#475569' }}>
            Tổng số: <strong>{filteredReceipts.length}</strong> / {receipts.length} phiếu xuất kho • Trang {receiptPage}/{totalReceiptPages}
          </span>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <button type="button" disabled={receiptPage <= 1} onClick={() => setReceiptPage(p => Math.max(1, p - 1))}>Trước</button>
            <span style={{ fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>Trang {receiptPage}/{totalReceiptPages}</span>
            <button type="button" disabled={receiptPage >= totalReceiptPages} onClick={() => setReceiptPage(p => Math.min(totalReceiptPages, p + 1))}>Sau</button>
          </div>
        </div>
      </UiCard>
    </div>
  );
}
