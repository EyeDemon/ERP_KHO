import { UiBadge, UiCard, UiEmptyState, UiTableScroll } from '../../ui/ProductionUi';
import { isoDay, isPoOverdue, labels, tone, type PurchaseOrderList } from './PurchaseOrderComponents';

export function PurchaseOrderSecondaryFiltersBar({
  warehouseFilter,
  setWarehouseFilter,
  warehouseFilterOptions,
  supplierFilter,
  setSupplierFilter,
  supplierFilterOptions,
}: {
  warehouseFilter: string;
  setWarehouseFilter: (val: string) => void;
  warehouseFilterOptions: string[];
  supplierFilter: string;
  setSupplierFilter: (val: string) => void;
  supplierFilterOptions: Array<[string, string]>;
}) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end', padding: '10px 14px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
      <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 220, maxWidth: 320, flex: '1 1 220px' }}>
        <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Lọc theo Kho nhận</span>
        <select aria-label="Lọc đơn mua theo kho nhận" value={warehouseFilter} onChange={e => setWarehouseFilter(e.target.value)} style={{ maxWidth: 320 }}>
          <option value="">Tất cả kho nhận</option>
          {warehouseFilterOptions.map(wh => <option key={wh} value={wh}>Kho: {wh}</option>)}
        </select>
      </label>
      <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start', minWidth: 240, maxWidth: 320, flex: '1 1 240px' }}>
        <span style={{ fontSize: 12.5, fontWeight: 600, color: '#334155' }}>Lọc theo Nhà cung cấp</span>
        <select aria-label="Lọc đơn mua theo nhà cung cấp" value={supplierFilter} onChange={e => setSupplierFilter(e.target.value)} style={{ maxWidth: 320 }}>
          <option value="">Tất cả nhà cung cấp</option>
          {supplierFilterOptions.map(([codeVal, labelVal]) => <option key={codeVal} value={codeVal}>NCC: {labelVal}</option>)}
        </select>
      </label>
      {(warehouseFilter || supplierFilter) && (
        <button type="button" onClick={() => { setWarehouseFilter(''); setSupplierFilter(''); }} style={{ height: 38, background: '#ffffff', border: '1px solid #cbd5e1', color: '#0f172a' }}>
          Xóa bộ lọc phụ
        </button>
      )}
    </div>
  );
}

export function PurchaseOrderListCard({
  filtered,
  viewDetail,
}: {
  filtered: PurchaseOrderList[];
  viewDetail: (id: number) => Promise<void>;
}) {
  return (
    <UiCard title="Danh sách đơn mua" subtitle="Theo dõi trạng thái tiếp nhận, cảnh báo quá hạn và khởi tạo nhanh ASN từ đơn mua">
      {filtered.length === 0 ? (
        <UiEmptyState title="Chưa có đơn mua phù hợp" detail="Thay đổi bộ lọc hoặc tạo đơn mua mới nếu bạn có quyền." />
      ) : (
        <UiTableScroll>
          <table className="inbound-planning-table ui-table-compact" aria-label="Danh sách đơn mua">
            <thead>
              <tr>
                <th>Mã đơn mua</th>
                <th>Nhà cung cấp</th>
                <th>Kho nhận</th>
                <th>Trạng thái</th>
                <th>Ngày dự kiến</th>
                <th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL đặt</th>
                <th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tổng tiền</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(item => {
                const overdue = isPoOverdue(item);
                return (
                  <tr key={item.id}>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap' }}>
                      <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => void viewDetail(item.id)}>
                        <strong style={{ whiteSpace: 'nowrap' }}>{item.code}</strong>
                      </button>
                      <div className="ui-muted-text" style={{ whiteSpace: 'nowrap' }}>{item.externalPoId}</div>
                    </td>
                    <td><span style={{ whiteSpace: 'nowrap' }}>{item.supplierCode}</span><br /><small>{item.supplierName}</small></td>
                    <td>{item.warehouseName}</td>
                    <td>
                      <div style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                        <UiBadge tone={tone(item.status)}><span style={{ whiteSpace: 'nowrap' }}>{labels[item.status] ?? item.status}</span></UiBadge>
                        {overdue && <UiBadge tone="danger"><span style={{ whiteSpace: 'nowrap' }}>Quá hạn</span></UiBadge>}
                      </div>
                    </td>
                    <td style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{isoDay(item.expectedDate)}</td>
                    <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.baseOrderedQuantity.toLocaleString('vi-VN')} THÙNG</td>
                    <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{Number(item.totalAmount || 0).toLocaleString('vi-VN')} đ</td>
                    <td>
                      <div className="ui-inline-actions" style={{ gap: 6, flexWrap: 'nowrap' }}>
                        <button type="button" style={{ whiteSpace: 'nowrap', background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1' }} onClick={() => void viewDetail(item.id)}>Xem chi tiết</button>
                        {['Open', 'PartiallyReceived'].includes(item.status) && (
                          <button
                            type="button"
                            style={{ whiteSpace: 'nowrap', background: '#eff6ff', color: '#1d4ed8', border: '1px solid #1d4ed8', fontWeight: 600 }}
                            onClick={() => {
                              try { sessionStorage.setItem('erp_preselect_po_id', String(item.id)); } catch {/* ignore */ }
                              window.location.assign(`/asns?poId=${item.id}`);
                            }}
                          >
                            Tạo ASN từ PO
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
      )}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0', fontSize: 12.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
        <span>Hiển thị {filtered.length === 0 ? 0 : 1}–{filtered.length} / Tổng {filtered.length} đơn mua</span>
        <span>Trang 1 / 1 (Cỡ trang: 20)</span>
      </div>
    </UiCard>
  );
}
