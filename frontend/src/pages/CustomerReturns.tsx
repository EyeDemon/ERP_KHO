import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermissionSet } from '../services/authorization';
import { useHardwareScannerWedge } from '../hooks/useHardwareScannerWedge';
import { CustomerReturnsTableAndCards } from '../modules/returns/CustomerReturnsTableAndCards';
import ReturnDispositionDrawer, { createRmaIdempotencyKey, normalizeCustomerReturnDto, type CustomerReturnDto } from '../modules/returns/ReturnDispositionDrawer';
import { UiCard, UiEmptyState, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';
import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../utils/warehouseLabel';

interface WarehouseOption { id: number; code: string; name: string; }
interface CustomerReturnListResponse {
  items?: CustomerReturnDto[];
  totalCount?: number;
  costHiddenByRbac?: boolean;
  summary?: { quarantinedCount?: number; inspectedCount?: number; restockedCount?: number; refurbishRtvScrapCount?: number };
}

export default function CustomerReturns() {
  usePermissionSet();
  const canReadCost = hasPermission('inventory.cost.read');
  const canCreateIntake =
    hasPermission('return.create') || hasPermission('return.receive') || hasPermission('receipt.create') || hasPermission('receipt.read');
  const canExecuteScrap =
    hasPermission('return.post') || hasPermission('return.disposition') || hasPermission('receipt.update');

  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);
  const [items, setItems] = useState<CustomerReturnDto[]>([]);
  const [costHiddenByRbac, setCostHiddenByRbac] = useState(false);
  const [summary, setSummary] = useState({
    quarantinedCount: 0,
    inspectedCount: 0,
    restockedCount: 0,
    refurbishRtvScrapCount: 0,
  });

  const [loading, setLoading] = useState(true);
  const [isSubmittingIntake, setIsSubmittingIntake] = useState(false);
  const [notice, setNotice] = useState('');
  const [errorState, setErrorState] = useState<{ status?: number; errorCode?: string; message: string } | null>(null);

  const [warehouseFilter, setWarehouseFilter] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [conditionFilter, setConditionFilter] = useState<string>('');
  const [dispositionFilter, setDispositionFilter] = useState<string>('');
  const [keyword, setKeyword] = useState<string>('');

  const [intakeWarehouseId, setIntakeWarehouseId] = useState<number>(1);
  const [intakeScanCode, setIntakeScanCode] = useState<string>('');
  const [intakeProductCode, setIntakeProductCode] = useState<string>('SKU-1001');
  const [intakeQuantity, setIntakeQuantity] = useState<string>('1');
  const [intakeReason, setIntakeReason] = useState<string>('BUYER_REJECTED_DELIVERY');
  const intakeIdempotencyKeyRef = useRef<string>(createRmaIdempotencyKey());
  const [selectedItem, setSelectedItem] = useState<CustomerReturnDto | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setErrorState(null);
    try {
      const [whRes, rmaRes] = await Promise.all([
        apiClient.get<WarehouseOption[]>('/api/v1/warehouses').catch(() => ({ data: [] as WarehouseOption[] })),
        apiClient.get<CustomerReturnListResponse | CustomerReturnDto[]>('/api/v1/customer-returns', {
          params: {
            warehouseId: warehouseFilter ? Number(warehouseFilter) : undefined,
            status: statusFilter || undefined,
            condition: conditionFilter || undefined,
            disposition: dispositionFilter || undefined,
            keyword: keyword.trim() || undefined,
            pageNumber: 1,
            pageSize: 50,
          },
        }),
      ]);

      const whList = (Array.isArray(whRes.data) ? whRes.data : []).map(w => ({
        ...w,
        name: formatWarehouseLabel(w.name, w.code, w.id),
      }));
      setWarehouses(whList);
      if (whList.length > 0 && !intakeWarehouseId) {
        setIntakeWarehouseId(whList[0].id);
      }

      const raw = rmaRes.data;
      const rawRows = Array.isArray(raw) ? raw : Array.isArray(raw?.items) ? raw.items : [];
      const rows = rawRows.map(normalizeCustomerReturnDto);
      setItems(rows);
      setCostHiddenByRbac(!Array.isArray(raw) && Boolean(raw?.costHiddenByRbac));

      const s = !Array.isArray(raw) ? raw?.summary : undefined;
      setSummary({
        quarantinedCount:
          s?.quarantinedCount ??
          rows.filter(r => ['QUARANTINED', 'PENDING_INSPECTION'].includes(r.status?.toUpperCase())).length,
        inspectedCount: s?.inspectedCount ?? rows.filter(r => r.status?.toUpperCase() === 'INSPECTED').length,
        restockedCount: s?.restockedCount ?? rows.filter(r => r.status?.toUpperCase() === 'COMPLETED' && r.disposition === 'RESTOCK_AVAILABLE').length,
        refurbishRtvScrapCount:
          s?.refurbishRtvScrapCount ??
          rows.filter(r => r.status?.toUpperCase() === 'COMPLETED' && ['REFURBISH_REPACK', 'RTV_VENDOR', 'SCRAP_DISPOSAL'].includes(r.disposition)).length,
      });
    } catch (err) {
      const resp = (err as { response?: { status?: number; data?: Record<string, unknown> } })?.response;
      setErrorState({
        status: resp?.status,
        errorCode: String(resp?.data?.errorCode ?? resp?.data?.code ?? ''),
        message: String(resp?.data?.message ?? 'Không thể tải danh sách hàng hoàn & đổi trả (RMA).'),
      });
    } finally {
      setLoading(false);
    }
  }, [warehouseFilter, statusFilter, conditionFilter, dispositionFilter, keyword, intakeWarehouseId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  useHardwareScannerWedge({
    enabled: !selectedItem,
    onScan: barcode => {
      setIntakeScanCode(barcode);
      intakeIdempotencyKeyRef.current = createRmaIdempotencyKey();
      setNotice(`Đã nhận mã quét súng HID: ${barcode}`);
    },
  });

  const handleIntakeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmedScan = intakeScanCode.trim();
    if (!trimmedScan) {
      setErrorState({
        status: 422,
        errorCode: 'RMA_TRACKING_OR_ORDER_REQUIRED',
        message: 'Vui lòng quét hoặc nhập Mã vận đơn (AWB) / Mã phiếu xuất / Mã đơn hàng gốc.',
      });
      return;
    }

    setIsSubmittingIntake(true);
    setErrorState(null);
    setNotice('');
    try {
      const isExportCode = /^EX-/i.test(trimmedScan);
      const isSalesOrder = /^SO-/i.test(trimmedScan);
      await apiClient.post(
        '/api/v1/customer-returns/intake',
        {
          warehouseId: intakeWarehouseId || 1,
          trackingNumber: !isExportCode && !isSalesOrder ? trimmedScan : undefined,
          exportReceiptCode: isExportCode ? trimmedScan : undefined,
          salesOrderCode: isSalesOrder ? trimmedScan : undefined,
          returnReason: intakeReason,
          productCode: intakeProductCode.trim() || 'SKU-1001',
          quantity: Math.max(1, Number(intakeQuantity) || 1),
          quarantineLocationCode: 'QUARANTINE-01',
        },
        { headers: { 'Idempotency-Key': intakeIdempotencyKeyRef.current } },
      );
      intakeIdempotencyKeyRef.current = createRmaIdempotencyKey();
      setIntakeScanCode('');
      setNotice(`Đã tiếp nhận kiện hoàn (${trimmedScan}) vào Khu biệt trữ RMA thành công.`);
      await loadData();
    } catch (err) {
      const resp = (err as { response?: { status?: number; data?: Record<string, unknown> } })?.response;
      setErrorState({
        status: resp?.status,
        errorCode: String(resp?.data?.errorCode ?? resp?.data?.code ?? ''),
        message: String(resp?.data?.message ?? 'Không thể tiếp nhận kiện hoàn vào Khu biệt trữ RMA.'),
      });
    } finally {
      setIsSubmittingIntake(false);
    }
  };

  const openDispositionDrawer = async (row: CustomerReturnDto) => {
    setSelectedItem(normalizeCustomerReturnDto(row));
    try {
      const detailRes = await apiClient.get<CustomerReturnDto>(`/api/v1/customer-returns/${row.id}`);
      if (detailRes.data && detailRes.data.id) {
        setSelectedItem(normalizeCustomerReturnDto(detailRes.data));
      }
    } catch {
      // Giữ bản ghi đã chuẩn hóa trên bảng
    }
  };

  const showUnitCost = canReadCost && !costHiddenByRbac;
  const kpiCards = [
    { label: 'Đang biệt trữ chờ kiểm định', subtitle: 'Kiện hoàn chờ kiểm định QC', value: summary.quarantinedCount, testId: 'kpi-rma-quarantined' },
    { label: 'Đã kiểm định chờ xử lý', subtitle: 'Sẵn sàng phân luồng định đoạt', value: summary.inspectedCount, testId: 'kpi-rma-inspected' },
    { label: 'Đã nhập lại tồn khả dụng', subtitle: 'Hoàn nhập kệ bán hàng bình thường', value: summary.restockedCount, testId: 'kpi-rma-restocked' },
    { label: 'Tân trang / Xuất trả NCC / Xuất hủy', subtitle: 'Thay vỏ hộp, trả NCC hoặc xuất hủy', value: summary.refurbishRtvScrapCount, testId: 'kpi-rma-other-dispositions' },
  ];

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Nhập kho & Hậu cần thu hồi"
        title="Hàng hoàn & Đổi trả (RMA)"
        description="Tiếp nhận kiện hoàn TMĐT vào Khu biệt trữ RMA, kiểm định chất lượng và phân luồng định đoạt 4 hướng vào Sổ cái tồn kho."
      />

      <UiMetricGrid>
        {kpiCards.map(card => {
          const isZero = card.value === 0;
          return (
            <div
              key={card.testId}
              className={`ui-metric${isZero ? ' ui-metric--zero' : ''}`}
              data-zero={isZero ? 'true' : 'false'}
              style={{ height: '100%', boxSizing: 'border-box' }}
            >
              <div className="ui-metric-label">{card.label}</div>
              <strong
                data-testid={card.testId}
                className="ui-metric-value"
                style={{ fontSize: 24, fontVariantNumeric: 'tabular-nums', color: isZero ? '#475569' : '#0f172a' }}
              >
                {card.value}
              </strong>
              <span style={{ fontSize: 12, color: '#475569' }}>{card.subtitle}</span>
            </div>
          );
        })}
      </UiMetricGrid>

      {notice && (
        <div role="status" aria-live="polite" data-testid="rma-success-banner" style={{ padding: '10px 14px', borderRadius: 8, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontSize: 13.5, fontWeight: 600 }}>
          {notice}
        </div>
      )}

      {errorState && errorState.status === 409 && (
        <div role="alert" data-testid="rma-conflict-banner" style={{ padding: '10px 14px', borderRadius: 8, background: '#fffbeb', border: '1px solid #f59e0b', color: '#92400e', fontSize: 13.5 }}>
          <strong>Xung đột phiên bản (Mã 409):</strong> {errorState.message}{' '}
          <button type="button" className="ui-btn ui-btn-secondary" onClick={() => void loadData()}>Tải lại</button>
        </div>
      )}

      {errorState && errorState.status === 403 && (
        <div role="alert" data-testid="rma-forbidden-banner" style={{ padding: '10px 14px', borderRadius: 8, background: '#fef2f2', border: '1px solid #f87171', color: '#991b1b', fontSize: 13.5 }}>
          <strong>Không đủ quyền truy cập (Mã 403):</strong> {errorState.message}
        </div>
      )}

      {errorState && errorState.status !== 409 && errorState.status !== 403 && (
        <div role="alert" data-testid="rma-error-banner" style={{ padding: '10px 14px', borderRadius: 8, background: '#fef2f2', border: '1px solid #fca5a5', color: '#991b1b', fontSize: 13.5 }}>
          <strong>Thông báo lỗi:</strong> {errorState.message}
        </div>
      )}

      <UiCard title="Quét tiếp nhận nhanh kiện hoàn vào Khu biệt trữ RMA (1 bước)">
        <form onSubmit={handleIntakeSubmit} data-testid="rma-intake-scan-bar" style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'flex-end', maxWidth: 1120 }}>
          <label className="ui-field" style={{ flex: '1 1 205px', minWidth: 195 }}>
            <span>Mã vận đơn AWB / Đơn gốc</span>
            <input aria-label="Mã vận đơn AWB hoặc mã đơn gốc" data-testid="input-rma-intake-scan" value={intakeScanCode} onChange={e => { setIntakeScanCode(e.target.value); intakeIdempotencyKeyRef.current = createRmaIdempotencyKey(); }} placeholder="Quét AWB, đơn gốc..." />
          </label>
          <label className="ui-field" style={{ flex: '0 1 150px', minWidth: 135 }}>
            <span>Mã sản phẩm (SKU)</span>
            <input aria-label="Mã sản phẩm hoàn" data-testid="input-rma-intake-product" value={intakeProductCode} onChange={e => setIntakeProductCode(e.target.value)} placeholder="VD: SKU-1001" />
          </label>
          <label className="ui-field" style={{ flex: '0 1 110px', minWidth: 96 }}>
            <span>Số lượng hoàn</span>
            <input type="number" min={1} aria-label="Số lượng hoàn" data-testid="input-rma-intake-qty" value={intakeQuantity} onChange={e => setIntakeQuantity(e.target.value)} />
          </label>
          <label className="ui-field" style={{ flex: '1 1 215px', minWidth: 205 }}>
            <span>Kho tiếp nhận</span>
            <select aria-label="Kho tiếp nhận" value={intakeWarehouseId} onChange={e => setIntakeWarehouseId(Number(e.target.value))} style={{ minWidth: 205 }}>
              {warehouses.length === 0 && <option value={1}>{CANONICAL_WH01_LABEL}</option>}
              {warehouses.map(w => <option key={w.id} value={w.id}>{formatWarehouseLabel(w.name, w.code, w.id)}</option>)}
            </select>
          </label>
          <label className="ui-field" style={{ flex: '1 1 250px', minWidth: 245 }}>
            <span>Lý do hoàn trả</span>
            <select aria-label="Lý do hoàn trả" value={intakeReason} onChange={e => setIntakeReason(e.target.value)} style={{ minWidth: 245 }}>
              <option value="BUYER_REJECTED_DELIVERY">Khách từ chối nhận hàng</option>
              <option value="DAMAGED_IN_TRANSIT">Móp vỡ khi vận chuyển</option>
              <option value="DEFECTIVE_WARRANTY">Lỗi kỹ thuật / Bảo hành</option>
              <option value="WRONG_ITEM_SENT">Giao sai mẫu / phân loại</option>
            </select>
          </label>
          <div style={{ display: 'flex', alignItems: 'flex-end', marginLeft: 'auto' }}>
            <button
              type="submit"
              data-testid="btn-submit-rma-intake"
              className="ui-btn ui-btn-primary ui-primary-button"
              disabled={isSubmittingIntake || !canCreateIntake}
              title={!canCreateIntake ? 'Tài khoản hiện tại không có quyền tiếp nhận hàng hoàn' : undefined}
              style={{ minHeight: 38, padding: '0 18px', whiteSpace: 'nowrap' }}
            >
              {isSubmittingIntake ? 'Đang tiếp nhận...' : 'Tiếp nhận vào Khu biệt trữ RMA'}
            </button>
          </div>
        </form>
      </UiCard>

      <UiCard title="Danh sách Phiếu Hàng Hoàn & Định Đoạt (RMA)">
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginBottom: 12 }}>
          <input
            type="search"
            aria-label="Tìm mã RMA, AWB hoặc SKU"
            placeholder="Tìm mã RMA, vận đơn, đơn gốc..."
            value={keyword}
            onChange={e => setKeyword(e.target.value)}
            style={{ minWidth: 265, flex: '1 1 265px', maxWidth: 340 }}
          />
          <select aria-label="Lọc theo trạng thái RMA" value={statusFilter} onChange={e => setStatusFilter(e.target.value)} style={{ minWidth: 190 }}>
            <option value="">Tất cả trạng thái</option>
            <option value="QUARANTINED">Đang biệt trữ</option>
            <option value="PENDING_INSPECTION">Chờ kiểm định QC</option>
            <option value="INSPECTED">Đã kiểm định</option>
            <option value="COMPLETED">Đã hoàn tất</option>
          </select>
          <select aria-label="Lọc theo tình trạng vật lý" value={conditionFilter} onChange={e => setConditionFilter(e.target.value)} style={{ minWidth: 205 }}>
            <option value="">Tất cả tình trạng vật lý</option>
            <option value="GOOD">Nguyên vẹn</option>
            <option value="DAMAGED_BOX">Móp vỏ hộp</option>
            <option value="DEFECTIVE">Lỗi kỹ thuật / Hư hỏng</option>
          </select>
          <select aria-label="Lọc theo hướng định đoạt" value={dispositionFilter} onChange={e => setDispositionFilter(e.target.value)} style={{ minWidth: 215 }}>
            <option value="">Tất cả hướng định đoạt</option>
            <option value="PENDING">Chờ quyết định</option>
            <option value="RESTOCK_AVAILABLE">Nhập lại tồn khả dụng</option>
            <option value="REFURBISH_REPACK">Tân trang &amp; Đóng gói lại</option>
            <option value="RTV_VENDOR">Xuất trả NCC (RTV)</option>
            <option value="SCRAP_DISPOSAL">Xuất hủy</option>
          </select>
          {warehouseFilter && <button type="button" className="ui-btn ui-btn-secondary" onClick={() => setWarehouseFilter('')}>Xóa lọc kho</button>}
        </div>

        {loading ? (
          <div role="status" style={{ padding: 20, color: '#475569' }}>Đang tải danh sách hàng hoàn RMA...</div>
        ) : items.length === 0 ? (
          <UiEmptyState title="Chưa có kiện hàng hoàn trả nào khớp bộ lọc" detail="Sử dụng thanh Quét tiếp nhận nhanh ở trên để đưa kiện hoàn AWB vào Khu biệt trữ RMA." />
        ) : (
          <CustomerReturnsTableAndCards
            items={items}
            showUnitCost={showUnitCost}
            onOpenDisposition={openDispositionDrawer}
          />
        )}
      </UiCard>

      <ReturnDispositionDrawer
        open={Boolean(selectedItem)}
        item={selectedItem}
        canExecuteScrap={canExecuteScrap}
        onClose={() => setSelectedItem(null)}
        onCompleted={msg => { setSelectedItem(null); setNotice(msg); void loadData(); }}
        onReloadConflict={() => { setSelectedItem(null); void loadData(); }}
      />
    </UiPage>
  );
}
