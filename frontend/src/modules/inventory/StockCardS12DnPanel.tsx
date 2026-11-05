import { useMemo, useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';

export interface StockCardLineDto {
  transactionId?: number;
  transactionDate?: string;
  documentCode?: string;
  inboundDocumentCode?: string | null;
  outboundDocumentCode?: string | null;
  movementType?: string;
  description?: string;
  unitName?: string;
  inboundQuantity?: number;
  outboundQuantity?: number;
  runningBalance?: number;
}

export interface StockCardReportDto {
  templateCode?: string;
  circularReference?: string;
  warehouseId?: number;
  warehouseName?: string;
  productId?: number;
  productCode?: string;
  productName?: string;
  unitName?: string;
  openingBalance?: number;
  totalInboundQuantity?: number;
  totalOutboundQuantity?: number;
  closingBalance?: number;
  lines?: StockCardLineDto[];
}

export interface StockCardS12DnPanelProps {
  warehouses: Array<{ id: number; name: string }>;
  products: Array<{ productId: number; productCode: string; productName: string; unitName?: string }>;
  defaultWarehouseId?: number;
  defaultProductId?: number;
}

export function StockCardS12DnPanel({
  warehouses,
  products,
  defaultWarehouseId,
  defaultProductId,
}: StockCardS12DnPanelProps) {
  const uniqueProducts = useMemo(
    () =>
      Array.from(
        new Map(
          (Array.isArray(products) ? products : [])
            .filter(p => p && typeof p.productId === 'number' && p.productId > 0)
            .map(p => [p.productId, p]),
        ).values(),
      ),
    [products],
  );
  const [warehouseId, setWarehouseId] = useState<number>(defaultWarehouseId || warehouses[0]?.id || 1);
  const [productId, setProductId] = useState<number>(defaultProductId || uniqueProducts[0]?.productId || 1);
  const [loading, setLoading] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [report, setReport] = useState<StockCardReportDto | null>(null);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');

  const resolvedWhId = warehouseId || defaultWarehouseId || warehouses[0]?.id || 1;
  const resolvedProdId = productId || defaultProductId || uniqueProducts[0]?.productId || 1;

  const handleViewStockCard = async () => {
    setLoading(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.get<StockCardReportDto>('/api/v1/reports/stock-card', {
        params: { warehouseId: resolvedWhId, productId: resolvedProdId },
      });
      setReport(res.data ?? null);
      setNotice('Đã tải Thẻ Kho S12-DN (Thông tư 200/2014/TT-BTC) từ Sổ cái bất biến.');
    } catch (err) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg || 'Không thể tải dữ liệu Thẻ Kho S12-DN.');
    } finally {
      setLoading(false);
    }
  };

  const handleExportExcel = async () => {
    setExporting(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.get<Blob>('/api/v1/reports/stock-card/export', {
        params: { warehouseId: resolvedWhId, productId: resolvedProdId },
        responseType: 'blob',
      });
      if (typeof window !== 'undefined' && typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
        const blob = res.data instanceof Blob ? res.data : new Blob([JSON.stringify(res.data ?? '')]);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `TheKho_S12DN_WH${resolvedWhId}_SP${resolvedProdId}.xlsx`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
      }
      setNotice('Đã xuất tệp Excel Thẻ Kho S12-DN (.xlsx) chuẩn Thông tư 200/2014/TT-BTC.');
    } catch (err) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setError(msg || 'Không thể xuất tệp Excel Thẻ Kho S12-DN.');
    } finally {
      setExporting(false);
    }
  };

  const handlePrintStockCard = () => {
    setNotice('Đã kích hoạt lệnh in Thẻ Kho Mẫu số S12-DN (Thông tư 200/2014/TT-BTC).');
    if (typeof window !== 'undefined' && typeof window.print === 'function') {
      try {
        window.print();
      } catch {
        // Bỏ qua trong môi trường kiểm thử tự động
      }
    }
  };

  const lines = Array.isArray(report?.lines) ? report.lines : [];

  return (
    <UiCard
      title="Thẻ Kho Kế Toán Mẫu số S12-DN (Thông tư 200/2014/TT-BTC)"
      subtitle="Truy xuất Số dư đầu kỳ, chứng từ Nhập/Xuất, Số dư lũy kế từng giao dịch và Số dư cuối kỳ theo chuẩn kế toán kho Việt Nam."
    >
      <div data-testid="stock-card-s12dn-panel" className="ui-stack" style={{ gap: 12 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'flex-end' }}>
          <label className="ui-field" style={{ minWidth: 200 }}>
            <span>Kho lập Thẻ kho</span>
            <select
              aria-label="Kho lập Thẻ kho S12-DN"
              value={resolvedWhId}
              onChange={e => setWarehouseId(Number(e.target.value))}
            >
              {warehouses.length === 0 && <option value={1}>{CANONICAL_WH01_LABEL}</option>}
              {warehouses.map(w => (
                <option key={w.id} value={w.id}>{formatWarehouseLabel(w.name, undefined, w.id)}</option>
              ))}
            </select>
          </label>
          <label className="ui-field" style={{ minWidth: 240 }}>
            <span>Sản phẩm (SKU)</span>
            <select
              aria-label="Sản phẩm lập Thẻ kho S12-DN"
              value={resolvedProdId}
              onChange={e => setProductId(Number(e.target.value))}
            >
              {uniqueProducts.length === 0 && <option value={1}>SKU-1001 — Sản phẩm mặc định</option>}
              {uniqueProducts.map(p => (
                <option key={p.productId} value={p.productId}>
                  {p.productCode} — {p.productName}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            data-testid="btn-view-stock-card-s12dn"
            className="ui-btn ui-btn-primary ui-primary-button"
            disabled={loading}
            onClick={() => void handleViewStockCard()}
          >
            {loading ? 'Đang tải Thẻ Kho...' : 'Xem Thẻ Kho S12-DN'}
          </button>
          <button
            type="button"
            data-testid="btn-export-stock-card-s12dn"
            className="ui-btn ui-btn-secondary"
            disabled={exporting}
            onClick={() => void handleExportExcel()}
          >
            {exporting ? 'Đang xuất Excel...' : 'Xuất Thẻ Kho S12-DN (.xlsx)'}
          </button>
          <button
            type="button"
            data-testid="btn-print-stock-card-s12dn"
            className="ui-btn ui-btn-secondary"
            onClick={handlePrintStockCard}
          >
            In Thẻ Kho (S12-DN)
          </button>
        </div>

        {notice && (
          <div role="status" data-testid="stock-card-s12dn-notice" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontSize: 13 }}>
            {notice}
          </div>
        )}
        {error && (
          <div role="alert" data-testid="stock-card-s12dn-error" style={{ padding: '8px 12px', borderRadius: 8, background: '#fef2f2', border: '1px solid #fca5a5', color: '#991b1b', fontSize: 13 }}>
            {error}
          </div>
        )}

        {report && (
          <div data-testid="stock-card-s12dn-preview" style={{ padding: 12, borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', gap: 10, marginBottom: 10, fontSize: 13 }}>
              <div>
                <UiBadge tone="success">{report.templateCode || 'Mẫu số S12-DN'}</UiBadge>{' '}
                <strong>{report.productCode} — {report.productName}</strong> ({formatWarehouseLabel(report.warehouseName, undefined, report.warehouseId ?? resolvedWhId) || `Kho #${resolvedWhId}`})
              </div>
              <div style={{ display: 'flex', gap: 14, fontVariantNumeric: 'tabular-nums' }}>
                <span><strong>Số dư đầu kỳ:</strong> {report.openingBalance ?? 0}</span>
                <span><strong>Tổng Nhập:</strong> +{report.totalInboundQuantity ?? 0}</span>
                <span><strong>Tổng Xuất:</strong> -{report.totalOutboundQuantity ?? 0}</span>
                <span><strong>Số dư cuối kỳ:</strong> {report.closingBalance ?? 0}</span>
              </div>
            </div>
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Bảng chi tiết Thẻ Kho S12-DN">
                <thead>
                  <tr>
                    <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Ngày ghi sổ</th>
                    <th style={{ minWidth: 115, whiteSpace: 'nowrap' }}>Chứng từ Nhập</th>
                    <th style={{ minWidth: 115, whiteSpace: 'nowrap' }}>Chứng từ Xuất</th>
                    <th style={{ minWidth: 140 }}>Diễn giải</th>
                    <th style={{ textAlign: 'right', minWidth: 88, whiteSpace: 'nowrap' }}>SL Nhập</th>
                    <th style={{ textAlign: 'right', minWidth: 88, whiteSpace: 'nowrap' }}>SL Xuất</th>
                    <th style={{ textAlign: 'right', minWidth: 105, whiteSpace: 'nowrap' }}>Số dư lũy kế</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, idx) => (
                    <tr key={line.transactionId ?? idx}>
                      <td style={{ whiteSpace: 'nowrap' }}>{line.transactionDate ? new Date(line.transactionDate).toLocaleDateString('vi-VN') : '—'}</td>
                      <td style={{ whiteSpace: 'nowrap' }}><span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{line.inboundDocumentCode || ((line.inboundQuantity ?? 0) > 0 ? line.documentCode : '—')}</span></td>
                      <td style={{ whiteSpace: 'nowrap' }}><span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{line.outboundDocumentCode || ((line.outboundQuantity ?? 0) > 0 ? line.documentCode : '—')}</span></td>
                      <td>{line.description || line.movementType || 'Giao dịch kho'}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: '#047857' }}>
                        {line.inboundQuantity ?? 0}
                      </td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: '#b91c1c' }}>
                        {line.outboundQuantity ?? 0}
                      </td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700 }}>
                        {line.runningBalance ?? 0}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        )}
      </div>
    </UiCard>
  );
}

export default StockCardS12DnPanel;
